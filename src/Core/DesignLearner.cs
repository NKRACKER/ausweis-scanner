using System;
using System.Collections.Generic;
using System.Linq;

namespace AusweisScanner.Core;

/// <summary>Eine von der Texterkennung gefundene Zeile, in Bruchteilen der Karte (0..1, Querformat, links oben).</summary>
public sealed class LayoutLine
{
    public string Text = "";
    public RectD Frac;
    public List<(string Text, RectD Frac)> Words = new List<(string, RectD)>();
}

/// <summary>
/// Hilfen zum Anlernen neuer Ausweis-Designs: zeigt, was die Texterkennung auf der Beispielkarte sieht (Zeilen als anklickbare Kästen)
/// und schlägt aus dem automatisch erkannten Namen gleich passende Felder vor – man muss nur noch bestätigen oder nachziehen.
/// </summary>
public static class DesignLearner
{
    const int Pad = 16;

    /// <summary>Alle Textzeilen der Karte als Bruchteile der Kartenfläche.</summary>
    public static List<LayoutLine> ReadLayout(Raster card, IOcrEngine ocr, int targetLong = 1400)
    {
        double k = Math.Max(card.Width, card.Height) < targetLong ? Math.Min(3.0, (double)targetLong / Math.Max(card.Width, card.Height)) : 1.0;
        if (k <= 1.01) k = 1.0;
        var res = ocr.Recognize(NameLocator.Prepare(card, targetLong));
        RectD Map(RectD b) => new RectD(Math.Max(0, (b.X - Pad) / k / card.Width), Math.Max(0, (b.Y - Pad) / k / card.Height), b.W / k / card.Width, b.H / k / card.Height);
        var lines = new List<LayoutLine>();
        foreach (var l in res.Lines)
        {
            if (string.IsNullOrWhiteSpace(l.Text)) continue;
            var ll = new LayoutLine { Text = l.Text.Trim(), Frac = Map(l.Box) };
            foreach (var w in l.Words) ll.Words.Add((w.Text, Map(w.Box)));
            lines.Add(ll);
        }
        return lines;
    }

    /// <summary>Anklickbare Zeile → Feld: leicht vergrößert (OCR-Kästen sitzen eng), nie über den Kartenrand hinaus.</summary>
    public static FieldRegion RegionFor(LayoutLine line, string role)
    {
        double padX = 0.012, padY = Math.Max(0.012, line.Frac.H * 0.25);
        double x = Math.Max(0, line.Frac.X - padX), y = Math.Max(0, line.Frac.Y - padY);
        double w = Math.Min(1 - x, line.Frac.W + 2 * padX), h = Math.Min(1 - y, line.Frac.H + 2 * padY);
        return new FieldRegion { Role = role, X = Math.Round(x, 4), Y = Math.Round(y, 4), W = Math.Round(w, 4), H = Math.Round(h, 4) };
    }

    /// <summary>
    /// Erkennt den Namen mit der Automatik und leitet daraus Felder ab (Zeile mit Nachname/Vorname; steht beides in einer Zeile, ein Feld „name“).
    /// Liefert null, wenn kein Name erkannt oder keine passende Zeile gefunden wurde.
    /// </summary>
    public static Profile? Suggest(Raster card, IOcrEngine ocr, out string explanation)
    {
        explanation = "";
        var cands = new NameLocator(ocr).Locate(card, Profile.Auto());
        if (cands.Count == 0 || cands[0].Score < 0.4) { explanation = "Es wurde kein Name erkannt – bitte die Zeilen von Hand anklicken oder Rahmen ziehen."; return null; }
        var best = cands[0]; var lines = ReadLayout(card, ocr);
        string last = best.Name.Last, first = best.Name.First;
        LayoutLine? Find(string needle)
        {
            if (needle.Length == 0) return null;
            var n = Norm(needle);
            return lines.Where(l => Norm(l.Text).Contains(n)).OrderBy(l => l.Text.Length).FirstOrDefault()
                   ?? lines.Where(l => Norm(l.Text.Replace(" ", "")).Contains(n.Replace(" ", ""))).OrderBy(l => l.Text.Length).FirstOrDefault();
        }
        var lLast = Find(last); var lFirst = Find(first.Split(' ')[0]);
        if (lLast == null) { explanation = $"Name „{best.Name}“ erkannt, aber die Zeile wurde nicht gefunden – bitte von Hand markieren."; return null; }
        var p = new Profile { Name = "Neues Design", ValidFrom = DateTime.Now.ToString("yyyy-MM-dd") };
        if (lFirst != null && !ReferenceEquals(lFirst, lLast))
        {
            p.Fields.Add(RegionFor(lLast, "last")); p.Fields.Add(RegionFor(lFirst, "first"));
            explanation = $"Vorschlag: Nachname „{last}“ und Vorname „{first}“ stehen in getrennten Zeilen.";
        }
        else
        {
            p.Fields.Add(RegionFor(lLast, "name"));
            string t = lLast.Text; int il = t.IndexOf(last, StringComparison.OrdinalIgnoreCase), iff = t.IndexOf(first.Split(' ')[0], StringComparison.OrdinalIgnoreCase);
            p.Format = t.Contains(",") ? NameFormat.LastCommaFirst : (iff >= 0 && il >= 0 && iff < il) ? NameFormat.FirstLast : NameFormat.LastFirst;
            explanation = $"Vorschlag: Name „{best.Name}“ steht in einer Zeile.";
        }
        return p;
    }

    static string Norm(string s) => new string(s.Where(c => char.IsLetter(c) || c == ' ').ToArray()).ToLowerInvariant().Trim();
}
