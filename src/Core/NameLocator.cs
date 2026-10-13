using System;
using System.Collections.Generic;
using System.Linq;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Core.Tests")]

namespace AusweisScanner.Core;

public sealed class NameCandidate
{
    public ParsedName Name = new ParsedName();
    public double Score;
    internal int Pass;   // Nummer des OCR-Durchlaufs (für die Abstimmung zwischen Durchläufen)
    /// <summary>"Profilfeld", "Beschriftung" oder "Schätzung" – wird dem Nutzer neben dem Vorschlag gezeigt.</summary>
    public string Source = "";
    public override string ToString() => $"{Name} [{Source} {Score:0.00}]";
}

/// <summary>
/// Findet den Namen auf einer (entzerrten, Querformat-)Karte in drei Stufen, jeweils mit Plausibilitätsbewertung:
/// 1. Profilfelder (feste Bereiche), 2. Beschriftungen („Name:“/„Vorname“), 3. Heuristik (größte namensähnliche Zeile).
/// Das Ergebnis ist eine nach Score sortierte Liste – die UI zeigt den besten Treffer vorbelegt und die übrigen als Alternativen.
/// </summary>
public sealed class NameLocator
{
    readonly IOcrEngine _ocr;
    public NameLocator(IOcrEngine ocr) { _ocr = ocr; }

    public List<NameCandidate> Locate(Raster card, Profile profile)
    {
        var res = new List<NameCandidate>();
        if (profile.Fields.Count > 0)
        {
            var plain = FromFields(card, profile, false).ToList();
            if (!plain.Any(c => c.Score >= 0.78))        // unsicher: zweiter Durchlauf mit aufbereitetem Bild, Abstimmung
            {
                var enh = FromFields(card, profile, true).ToList(); foreach (var c in enh) c.Pass = 1;
                plain.AddRange(enh);
            }
            res.AddRange(Merge(plain));
        }
        bool good = res.Any(c => c.Score >= 0.7);
        if (!good && (profile.UseAnchors || profile.UseHeuristic))
        {
            // Mehrere OCR-Durchläufe (normal, aufbereitet und stark vergrößert); stimmen mehrere überein, steigt die Sicherheit.
            var all = new List<NameCandidate>(); var variants = new Func<Raster>[]
            {
                () => Prepare(card, 1400), () => Prepare(ImageEnhance.Normalize(card), 2000), () => Prepare(card, 2400),
            };
            for (int pass = 0; pass < variants.Length; pass++)
            {
                var full = _ocr.Recognize(variants[pass]());
                var found = new List<NameCandidate>();
                if (profile.UseAnchors) found.AddRange(FromAnchors(full, profile));
                if (profile.UseHeuristic) found.AddRange(FromHeuristic(full, profile));
                foreach (var c in found) c.Pass = pass;
                all.AddRange(found);
                var merged = Merge(all);
                // früh aufhören: schon im ersten Durchlauf sehr sicher, oder zwei Durchläufe stimmen auf einem plausiblen Namen überein
                if (merged.Count > 0 && (merged[0].Score >= 0.88 || (pass >= 1 && merged[0].Score >= 0.8 && all.Where(c => Similar(c.Name, merged[0].Name)).Select(c => c.Pass).Distinct().Count() >= 2))) break;
            }
            res.AddRange(Merge(all));
        }
        return res.GroupBy(c => (c.Name.Last.ToLowerInvariant(), c.Name.First.ToLowerInvariant()))
                  .Select(g => g.OrderByDescending(c => c.Score).First())
                  .OrderByDescending(c => c.Score).ToList();
    }

    // ---------- Stufe 1: Profilfelder ----------
    IEnumerable<NameCandidate> FromFields(Raster card, Profile p, bool enhance)
    {
        string last = "", first = "", both = "";
        foreach (var f in p.Fields)
        {
            int x = (int)Math.Round(f.X * card.Width), y = (int)Math.Round(f.Y * card.Height);
            int w = (int)Math.Round(f.W * card.Width), h = (int)Math.Round(f.H * card.Height);
            if (w < 8 || h < 8) continue;
            Raster crop;
            try { crop = card.Crop(x, y, w, h); } catch (ArgumentException) { continue; }
            var text = _ocr.Recognize(Prepare(enhance ? ImageEnhance.Normalize(crop) : crop, 0, enhance ? 140 : 110)).Text;
            text = NameParser.StripLabels(text, f.Strip);
            if (f.Role == "last") last = (last + " " + text).Trim();
            else if (f.Role == "first") first = (first + " " + text).Trim();
            else both = (both + " " + text).Trim();
        }
        if (last.Length > 0 || first.Length > 0)
        {
            var n = new ParsedName(NameParser.TitleCase(NameParser.Clean(last).Replace(",", " ")), NameParser.TitleCase(NameParser.Clean(first).Replace(",", " ")));
            yield return new NameCandidate { Name = n, Score = 0.95 * NameParser.Plausibility(n, p.Stopwords), Source = "Profilfeld" };
        }
        if (both.Length > 0)
        {
            var n = NameParser.Parse(both, p.Format);
            yield return new NameCandidate { Name = n, Score = 0.95 * NameParser.Plausibility(n, p.Stopwords), Source = "Profilfeld" };
        }
    }

    // ---------- Abstimmung zwischen Durchläufen ----------
    static string Key(ParsedName n) => (n.Last + " " + n.First).ToLowerInvariant().Trim();

    /// <summary>Gleicher Name bis auf eine verlesene Stelle (bei Namen ab 6 Zeichen)?</summary>
    internal static bool Similar(ParsedName a, ParsedName b)
    {
        string x = Fold(Key(a)), y = Fold(Key(b));
        return x == y || (Math.Min(x.Length, y.Length) >= 6 && Levenshtein(x, y) <= 1);
    }

    /// <summary>Typische OCR-Verwechslungen angleichen (rn↔m, cl↔d, vv↔w, i/l/1/|, 0↔o), damit „Eickrnann“ und „Eickmann“ als dieselbe Person gelten.</summary>
    static string Fold(string s) => s.Replace("rn", "m").Replace("vv", "w").Replace("cl", "d").Replace('i', 'l').Replace('1', 'l').Replace('|', 'l').Replace('0', 'o').Replace('5', 's');

    /// <summary>
    /// Fasst Kandidaten mehrerer Durchläufe zusammen: gleiche (oder um 1 Zeichen abweichende) Namen bilden eine Gruppe; die Schreibweise mit dem besten Score gewinnt,
    /// jeder weitere Durchlauf, der sie bestätigt, erhöht die Sicherheit.
    /// </summary>
    public static List<NameCandidate> Merge(IEnumerable<NameCandidate> all)
    {
        var clusters = new List<List<NameCandidate>>();
        foreach (var c in all.OrderByDescending(c => c.Score))
        {
            var cl = clusters.FirstOrDefault(k => Similar(k[0].Name, c.Name));
            if (cl == null) clusters.Add(new List<NameCandidate> { c }); else cl.Add(c);
        }
        return clusters.Select(k =>
        {
            int passes = k.Select(c => c.Pass).Distinct().Count();
            return new NameCandidate { Name = k[0].Name, Source = k[0].Source, Pass = k[0].Pass, Score = Math.Min(0.99, k[0].Score + 0.07 * (passes - 1)) };
        }).OrderByDescending(c => c.Score).ToList();
    }

    // ---------- Stufe 2: Beschriftungen ----------
    IEnumerable<NameCandidate> FromAnchors(OcrResult ocr, Profile p)
    {
        var firstVal = ValueForLabels(ocr, p.FirstLabels, null, out var firstLine);
        var lastVal = ValueForLabels(ocr, p.LastLabels, firstLine, out _);
        if (!string.IsNullOrWhiteSpace(lastVal) && !string.IsNullOrWhiteSpace(firstVal))
        {
            var n = new ParsedName(NameParser.TitleCase(NameParser.Clean(lastVal).Replace(",", " ")), NameParser.TitleCase(NameParser.Clean(firstVal).Replace(",", " ")));
            yield return new NameCandidate { Name = n, Score = 0.95 * NameParser.Plausibility(n, p.Stopwords), Source = "Beschriftung" };
        }
        else if (!string.IsNullOrWhiteSpace(lastVal))
        {
            var n = NameParser.Parse(lastVal, p.Format);
            yield return new NameCandidate { Name = n, Score = 0.9 * NameParser.Plausibility(n, p.Stopwords), Source = "Beschriftung" };
        }
    }

    static string ValueForLabels(OcrResult ocr, List<string> labels, OcrLine? skip, out OcrLine? usedLine)
    {
        usedLine = null;
        var set = labels.Select(Norm).Where(l => l.Length > 0).ToList();
        foreach (var line in ocr.Lines)
        {
            if (ReferenceEquals(line, skip)) continue;
            var words = line.Words.Count > 0 ? line.Words : line.Text.Split(' ').Select(t => new OcrWord { Text = t, Box = line.Box }).ToList();
            for (int i = 0; i < words.Count; i++)
            {
                var w = Norm(words[i].Text);
                if (w.Length == 0 || !set.Any(l => Matches(w, l))) continue;
                var rest = string.Join(" ", words.Skip(i + 1).Select(x => x.Text)).Trim(' ', ':', ';', '.');
                if (rest.Count(char.IsLetter) >= 2) { usedLine = line; return rest; }
                // Wert steht neben/unter der Beschriftung (eigene OCR-Zeile)
                var box = words[i].Box; double lh = Math.Max(line.Height, 1);
                var next = ocr.Lines.Where(o => !ReferenceEquals(o, line) && o.Text.Count(char.IsLetter) >= 2)
                    .Select(o => new { o, dy = o.Box.Y - line.Box.Bottom, dyc = Math.Abs(o.Box.CenterY - line.Box.CenterY), dx = o.Box.X - box.X })
                    .Where(t => (t.dyc < 0.6 * lh && t.o.Box.X >= box.Right - 2)                                   // rechts daneben
                             || (t.dy > -0.3 * lh && t.dy < 2.5 * lh && t.dx > -1.5 * lh && t.dx < 6 * lh))        // darunter, linksbündig
                    .OrderBy(t => Math.Min(t.dyc < 0.6 * lh ? t.dyc : double.MaxValue, Math.Max(0, t.dy)) + Math.Abs(t.dx) * 0.2).FirstOrDefault();
                if (next != null) { usedLine = line; return next.o.Text; }
            }
        }
        return "";
    }

    static string Norm(string s) => new string(s.Where(c => char.IsLetter(c)).ToArray()).ToLowerInvariant();

    static bool Matches(string word, string label)
    {
        if (word == label) return true;
        int max = label.Length <= 4 ? 0 : label.Length <= 6 ? 1 : 2;
        return max > 0 && Math.Abs(word.Length - label.Length) <= max && Levenshtein(word, label) <= max;
    }

    public static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    // ---------- Stufe 3: Heuristik ----------
    IEnumerable<NameCandidate> FromHeuristic(OcrResult ocr, Profile p)
    {
        var lines = ocr.Lines.Where(l => l.Text.Any(char.IsLetter) && !l.Text.Any(char.IsDigit)).ToList();
        if (lines.Count == 0) yield break;
        double maxH = lines.Max(l => l.Height);
        var scored = new List<NameCandidate>();
        foreach (var l in lines)
        {
            var n = NameParser.Parse(l.Text, p.Format);
            if (n.First.Length == 0) continue;                         // ohne Vor- UND Nachname zu unsicher für eine Schätzung
            double rank = 0.6 + 0.4 * (l.Height / maxH);
            scored.Add(new NameCandidate { Name = n, Score = 0.6 * NameParser.Plausibility(n, p.Stopwords) * rank, Source = "Schätzung" });
        }
        foreach (var c in scored.OrderByDescending(c => c.Score).Take(3)) yield return c;
    }

    // ---------- Bildaufbereitung für die OCR ----------
    /// <summary>Lange Kante auf <paramref name="targetLong"/> bzw. Zeilenhöhe auf <paramref name="targetHeight"/> bringen und weißen Rand anfügen (OCR liest Randtext schlecht).</summary>
    public static Raster Prepare(Raster r, int targetLong = 0, int targetHeight = 0)
    {
        double k = 1.0;
        if (targetHeight > 0 && r.Height < targetHeight) k = Math.Min(3.0, (double)targetHeight / r.Height);
        else if (targetLong > 0 && Math.Max(r.Width, r.Height) < targetLong) k = Math.Min(3.0, (double)targetLong / Math.Max(r.Width, r.Height));
        var s = k > 1.01 ? r.Resize((int)(r.Width * k), (int)(r.Height * k)) : r;
        const int pad = 16;
        var o = new Raster(s.Width + 2 * pad, s.Height + 2 * pad);
        s.Get(0, 0, out var br, out var bg, out var bb);
        o.Fill(br, bg, bb);
        for (int y = 0; y < s.Height; y++) Buffer.BlockCopy(s.Data, y * s.Width * 3, o.Data, ((y + pad) * o.Width + pad) * 3, s.Width * 3);
        return o;
    }
}

/// <summary>Probiert die Kartenlage (0°/180° bzw. ±90°) durch, bis ein plausibler Name gefunden ist – die Karte kann beliebig aufs Scannerbett gelegt werden.</summary>
public static class OrientationResolver
{
    /// <param name="locate">Liefert die Kandidaten einer Drehlage.</param>
    /// <returns>Gedrehte Karte (im Uhrzeigersinn) + Kandidaten dieser Lage.</returns>
    public static Raster Resolve(Raster card, string orientation, Func<Raster, List<NameCandidate>> locate, out List<NameCandidate> best, out int quarterTurns, double accept = 0.55)
    {
        // Das Profil gibt die erwartete Lage vor (zuerst probiert); gedreht aufgelegte Karten werden trotzdem gefunden.
        int[] order = orientation == "portrait" ? new[] { 1, 3, 0, 2 } : new[] { 0, 2, 1, 3 };
        Raster? bestImg = null; best = new List<NameCandidate>(); quarterTurns = 0; double bestScore = -1;
        foreach (var t in order)
        {
            var img = card.Rotate(t);
            var cands = locate(img);
            double sc = cands.Count > 0 ? cands[0].Score : 0;
            if (sc > bestScore) { bestScore = sc; bestImg = img; best = cands; quarterTurns = t; }
            if (sc >= accept) break;
        }
        return bestImg ?? card;
    }
}

/// <summary>
/// „Automatisch“: probiert die gespeicherten Profile (Ausweis-Designs) durch und nimmt das, das einen plausiblen Namen liefert;
/// das Gewinner-Profil wird für die nächsten Karten zuerst probiert (ein Scan enthält meist gleiche Designs).
/// Findet kein Profil etwas, greift das Profil „Automatisch“ (Beschriftung/Heuristik).
/// </summary>
public sealed class ProfileMatcher
{
    readonly NameLocator _loc; readonly List<Profile> _profiles; Profile? _sticky;
    public ProfileMatcher(NameLocator loc, IEnumerable<Profile> profiles) { _loc = loc; _profiles = profiles.ToList(); }
    public Profile? LastUsed => _sticky;

    public List<NameCandidate> Locate(Raster card, out Profile used, double accept = 0.7)
    {
        var withFields = _profiles.Where(p => p.Fields.Count > 0).ToList();
        if (_sticky != null && withFields.Remove(_sticky)) withFields.Insert(0, _sticky);
        List<NameCandidate>? best = null; Profile? bestP = null;
        foreach (var p in withFields)
        {
            var c = _loc.Locate(card, new Profile { Name = p.Name, Fields = p.Fields, Format = p.Format, Stopwords = p.Stopwords, UseAnchors = false, UseHeuristic = false });
            if (c.Count > 0 && (best == null || c[0].Score > best[0].Score)) { best = c; bestP = p; }
            if (best != null && best[0].Score >= accept) break;
        }
        if (best != null && best[0].Score >= accept) { _sticky = bestP; used = bestP!; return best; }
        var auto = _profiles.FirstOrDefault(p => p.Fields.Count == 0) ?? Profile.Auto();
        var fallback = _loc.Locate(card, auto);
        // Profil-Treffer mit niedrigem Score bleiben als Alternative erhalten
        if (best != null) fallback = fallback.Concat(best).OrderByDescending(c => c.Score).ToList();
        used = auto; return fallback;
    }
}
