using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>Eine erkannte Karte samt Namensvorschlägen – das, was die UI anzeigt und der Nutzer bestätigt.</summary>
sealed class CardItem
{
    public Raster Image = null!;
    public string Last = "", First = "";
    public List<NameCandidate> Candidates = new List<NameCandidate>();
    public double Score;          // Güte des besten Vorschlags (0 = nichts gefunden)
    public string Source = "";    // Profilfeld / Beschriftung / Schätzung / (manuell)
    public string ProfileUsed = "";
    public double Dpi = 300;
    public bool Excluded;
    public List<string> Warnings = new List<string>();
    public ScanInfo? Scan;        // Gesamtscan, aus dem die Karte stammt (nur im Arbeitsspeicher)
    public PointD[]? Corners;     // Lage im Gesamtscan
}

/// <summary>Ein Quadrat/Viereck im Gesamtscan. Item == null: Karte ist (noch) nicht in der Liste.</summary>
sealed class ScanQuad
{
    public PointD[] Corners = new PointD[4];
    public CardItem? Item;
    public bool Include;          // nur im Scan-Dialog: soll in die Liste
    public bool Manual;
    public bool Dirty;            // Ecken von Hand verändert → Karte muss neu ausgeschnitten werden
}

/// <summary>Der volle Scan mit allen Rahmen – bleibt für die Ansicht „Gesamten Scan prüfen“ im Speicher, bis die Liste geleert wird.</summary>
sealed class ScanInfo
{
    public Raster Image = null!;
    public double Dpi = 300;
    public List<ScanQuad> Quads = new List<ScanQuad>();
}

sealed class PipelineResult
{
    public List<CardItem> Cards = new List<CardItem>();
    public List<string> Warnings = new List<string>();
    public string ProfileUsed = "";
    public ScanInfo? Scan;
}

static class Pipeline
{
    /// <summary>Nimmt ein A4-Blatt an und rundet auf gängige Scan-Auflösungen.</summary>
    public static double EstimateDpiFromPage(Raster r)
    {
        double est = Math.Max(r.Width, r.Height) / (297 / 25.4);
        foreach (var d in new[] { 100, 150, 200, 300, 400, 600, 1200 }) if (Math.Abs(est - d) / d < 0.12) return d;
        return 0;
    }

    /// <summary>Scan → Karten finden → ausschneiden → (optional) Name lesen. Ohne OCR bleiben die Namen leer (manuelle Eingabe).</summary>
    public static PipelineResult Process(Raster scan, double scanDpi, Settings settings, IList<Profile> profiles, Profile selected, IOcrEngine? ocr, Action<string>? progress = null)
    {
        var res = new PipelineResult();
        double dpi = scanDpi > 0 ? scanDpi : settings.Dpi;
        progress?.Invoke("Suche Ausweise im Scan …");
        DetectionResult det = null!;
        // Dateien tragen oft falsche dpi-Angaben (72/96): findet die Erkennung nichts, werden plausible Auflösungen durchprobiert.
        foreach (var tryDpi in new[] { dpi, EstimateDpiFromPage(scan), 300, 150, 200, 600 }.Where(d => d > 0).Distinct())
        {
            var opt = new DetectorOptions { Dpi = tryDpi, EdgeInsetMm = settings.EdgeInsetMm };
            if (selected.CardWidthMm > 0 && selected.CardHeightMm > 0) { opt.ExpectedWidthMm = selected.CardWidthMm; opt.ExpectedHeightMm = selected.CardHeightMm; }
            det = CardDetector.Detect(scan, opt);
            if (det.Cards.Count > 0) { if (Math.Abs(tryDpi - dpi) > 1) res.Warnings.Add($"Auflösung des Bildes unklar – mit {tryDpi:0} dpi ausgewertet."); dpi = tryDpi; break; }
        }
        res.Warnings.AddRange(det.Warnings);

        var loc = ocr != null ? new NameLocator(ocr) : null;
        bool auto = selected.Name == "Automatisch";
        var matcher = loc != null && auto ? new ProfileMatcher(loc, profiles) : null;

        var info = new ScanInfo { Image = scan, Dpi = dpi };
        res.Scan = info;
        int n = 0;
        foreach (var dc in det.Cards)
        {
            n++; progress?.Invoke($"Lese Ausweis {n} von {det.Cards.Count} …");
            var item = ReadCard(info, dc, loc, matcher, selected);
            res.Cards.Add(item);
            info.Quads.Add(new ScanQuad { Corners = dc.Corners, Item = item });
        }
        res.ProfileUsed = matcher?.LastUsed?.Name ?? selected.Name;
        if (det.Cards.Count == 0) res.Warnings.Add("Keine Ausweise erkannt. Tipps: Karten mit Abstand (≥ 5 mm) zueinander und zum Rand auflegen, Deckel offen lassen oder eine dunkle Unterlage (z. B. schwarzes Papier) darüberlegen, Glasplatte sauber. Oder den Ausweis im Scan selbst markieren.");
        return res;
    }

    /// <summary>Eine Karte ausschneiden und (falls Texterkennung da ist) den Namen suchen.</summary>
    static CardItem ReadCard(ScanInfo scan, DetectedCard dc, NameLocator? loc, ProfileMatcher? matcher, Profile selected)
    {
        var card = CardExtractor.Extract(scan.Image, dc);
        var item = new CardItem { Dpi = scan.Dpi, Scan = scan, Corners = dc.Corners };
        item.Warnings.AddRange(dc.Warnings);
        if (loc != null)
        {
            try
            {
                Profile used = selected;
                card = OrientationResolver.Resolve(card, selected.Orientation,
                    c => matcher != null ? matcher.Locate(c, out used) : loc.Locate(c, selected),
                    out var cands, out _);
                item.Candidates = cands; item.ProfileUsed = used.Name;
                if (cands.Count > 0)
                {
                    var b = cands[0]; item.Last = b.Name.Last; item.First = b.Name.First; item.Score = b.Score; item.Source = b.Source;
                }
                if (item.Score < 0.7) item.Warnings.Add(cands.Count == 0 ? "Kein Name erkannt – bitte eintippen." : "Name unsicher – bitte prüfen.");
            }
            catch (Exception e) { item.Warnings.Add("Texterkennung fehlgeschlagen: " + e.Message); }
        }
        item.Image = card;
        return item;
    }

    /// <summary>Von Hand markierte Karte (vier Ecken im Gesamtscan) ausschneiden und lesen.</summary>
    public static CardItem FromCorners(ScanInfo scan, PointD[] corners, IList<Profile> profiles, Profile selected, IOcrEngine? ocr)
    {
        var loc = ocr != null ? new NameLocator(ocr) : null;
        var matcher = loc != null && selected.Name == "Automatisch" ? new ProfileMatcher(loc, profiles) : null;
        return ReadCard(scan, QuadTools.ToCard(corners, scan.Dpi), loc, matcher, selected);
    }
}

static class Relocator
{
    /// <summary>Namen nach manuellem Drehen einer Karte neu suchen (keine erneute Lagesuche).</summary>
    public static void Run(CardItem item, IList<Profile> profiles, Profile selected, IOcrEngine ocr)
    {
        var loc = new NameLocator(ocr);
        List<NameCandidate> cands = selected.Name == "Automatisch" ? new ProfileMatcher(loc, profiles).Locate(item.Image, out var used) : loc.Locate(item.Image, selected);
        item.Candidates = cands;
        if (cands.Count > 0) { var b = cands[0]; item.Last = b.Name.Last; item.First = b.Name.First; item.Score = b.Score; item.Source = b.Source; }
        else { item.Score = 0; item.Source = ""; }
        item.Warnings.Clear();
        if (item.Score < 0.7) item.Warnings.Add(cands.Count == 0 ? "Kein Name erkannt – bitte eintippen." : "Name unsicher – bitte prüfen.");
    }
}

sealed class SaveResult
{
    public List<string> Written = new List<string>();
    public List<string> Errors = new List<string>();
}

static class Saver
{
    public static string Ext(string format) => format == "pdf" ? ".pdf" : format == "png" ? ".png" : ".jpg";

    /// <summary>Speichert jede nicht ausgeschlossene Karte als eigene Datei. Überschreibt nie (hängt _2, _3 … an). Schreibt erst temporär, dann umbenennen.</summary>
    public static SaveResult Save(IEnumerable<CardItem> items, Settings s, string outDir, string? classLabel = null)
    {
        var res = new SaveResult();
        Directory.CreateDirectory(outDir);
        var naming = new NamingOptions { Template = s.Template, TransliterateUmlauts = s.TransliterateUmlauts };
        var extra = new Dictionary<string, string> { ["Klasse"] = classLabel ?? "", ["Datum"] = DateTime.Now.ToString("yyyy-MM-dd") };
        var taken = new HashSet<string>();
        string ext = Ext(s.Format);
        foreach (var it in items.Where(i => !i.Excluded))
        {
            try
            {
                var baseName = FileNamer.BaseName(new ParsedName(it.Last.Trim(), it.First.Trim()), naming, extra);
                var name = FileNamer.Unique(baseName, ext, n => File.Exists(Path.Combine(outDir, n)), taken);
                var path = Path.Combine(outDir, name); var tmp = path + ".tmp";
                byte[] data;
                if (s.Format == "png") data = PngWriter.Encode(it.Image, (int)Math.Round(it.Dpi));
                else
                {
                    var jpeg = BitmapCodec.EncodeJpeg(it.Image, s.JpegQuality, (int)Math.Round(it.Dpi));
                    data = s.Format == "pdf" ? PdfWriter.SingleJpegPage(jpeg, it.Image.Width, it.Image.Height, it.Image.Width / it.Dpi * 25.4, it.Image.Height / it.Dpi * 25.4) : jpeg;
                }
                File.WriteAllBytes(tmp, data); File.Move(tmp, path);
                res.Written.Add(path);
            }
            catch (Exception e) { res.Errors.Add(e.Message); }
        }
        return res;
    }
}
