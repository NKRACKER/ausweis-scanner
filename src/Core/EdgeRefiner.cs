using System;
using System.Collections.Generic;
using System.Linq;

namespace AusweisScanner.Core;

/// <summary>
/// Feinbestimmung der Kartenkanten in voller Scan-Auflösung. Die grobe Erkennung arbeitet auf ca. 85 dpi;
/// hier wird je Kante an 9 Stellen quer zur Kante nach der stärksten Helligkeitskante nahe der groben Kante gesucht
/// und eine robuste Gerade (Theil–Sen) durch diese Punkte gelegt. Ausreißer (Fotokanten, Aufdruck, Schatten) fallen heraus.
/// Aus den vier Geraden ergeben sich Kantenlage und eine kleine Winkelkorrektur; die Karte bleibt ein Rechteck.
/// </summary>
public static class EdgeRefiner
{
    /// <summary>Suchfenster (mm) nach innen/außen um die grobe Kante und Breite (mm) des Vorrangs der groben Lage – Feinabstimmung (Messung am Beispielscan).</summary>
    public static double InMm = 2.5, OutMm = 3.0, SigmaMm = 1.2;

    /// <returns>true, wenn mindestens eine Kante verfeinert wurde.</returns>
    public static bool Refine(byte[] g, int w, int h, ref PointD center, ref PointD la, ref PointD sa, ref double longLen, ref double shortLen, double ppm)
    {
        double inMm = InMm, outMm = OutMm;
        // Kanten: 0 = +la (rechts), 1 = -la (links), 2 = +sa (unten), 3 = -sa (oben)
        var n = new[] { la, la * -1, sa, sa * -1 };
        var t = new[] { sa, sa, la, la };
        double[] half = { longLen / 2, longLen / 2, shortLen / 2, shortLen / 2 };
        double[] tlen = { shortLen, shortLen, longLen, longLen };
        var a = new double[4]; var b = new double[4]; var ok = new bool[4];
        double inPx = inMm * ppm, outPx = outMm * ppm; const double step = 0.5;
        int cnt = (int)((inPx + outPx) / step) + 1;
        double sigma = SigmaMm * ppm;

        for (int k = 0; k < 4; k++)
        {
            var pts = new List<(double tau, double o, double s)>();
            for (int j = 0; j < 9; j++)
            {
                double tau = (-0.4 + 0.1 * j) * tlen[k], segHalf = 0.05 * tlen[k];
                var m = new double[cnt]; bool valid = true;
                for (int i = 0; i < cnt && valid; i++)
                {
                    double o = -inPx + i * step, sum = 0; int c = 0;
                    for (double s = tau - segHalf; s <= tau + segHalf; s += 1)
                    {
                        int x = (int)Math.Round(center.X + n[k].X * (half[k] + o) + t[k].X * s), y = (int)Math.Round(center.Y + n[k].Y * (half[k] + o) + t[k].Y * s);
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        sum += g[y * w + x]; c++;
                    }
                    if (c < 4) valid = false; else m[i] = sum / c;
                }
                if (!valid) continue;
                var sm = new double[cnt]; for (int i = 0; i < cnt; i++) sm[i] = (m[Math.Max(0, i - 1)] + m[i] + m[Math.Min(cnt - 1, i + 1)]) / 3;
                int best = -1; double bestScore = 0, bestD = 0;
                for (int i = 2; i < cnt - 2; i++)
                {
                    double d = Math.Abs(sm[i + 2] - sm[i - 2]) / 2, o = -inPx + i * step;      // Anstieg je Pixel
                    double sc = d * Math.Exp(-(o / sigma) * (o / sigma));                       // Nähe zur groben Kante bevorzugen
                    if (sc > bestScore) { bestScore = sc; best = i; bestD = d; }
                }
                if (best < 0 || bestD < 2.5) continue;
                pts.Add((tau, -inPx + best * step, bestD));
            }
            if (pts.Count < 5) continue;
            // Theil–Sen: Steigung = Median aller Paar-Steigungen, Achsenabschnitt = Median der Reste
            var slopes = new List<double>();
            for (int i = 0; i < pts.Count; i++) for (int j = i + 1; j < pts.Count; j++) slopes.Add((pts[j].o - pts[i].o) / (pts[j].tau - pts[i].tau));
            double slope = Median(slopes), icpt = Median(pts.Select(p => p.o - slope * p.tau).ToList());
            var inl = pts.Where(p => Math.Abs(p.o - (icpt + slope * p.tau)) < 0.45 * ppm).ToList();
            if (inl.Count < 4) continue;
            // kleinste Quadrate auf den Inliern
            double mt = inl.Average(p => p.tau), mo = inl.Average(p => p.o), sxx = inl.Sum(p => (p.tau - mt) * (p.tau - mt));
            double bb = sxx > 1e-9 ? inl.Sum(p => (p.tau - mt) * (p.o - mo)) / sxx : 0;
            a[k] = mo - bb * mt; b[k] = Math.Abs(bb) < 0.03 ? bb : 0;    // Winkelkorrekturen > ~1,7° sind unplausibel → ignorieren
            ok[k] = true;
        }
        if (!ok.Any(x => x)) return false;

        // Winkelkorrektur aus den Steigungen (Vorzeichen: siehe Herleitung – oben/unten ±δ, rechts/links ∓δ)
        var ds = new List<double>();
        if (ok[2]) ds.Add(b[2]); if (ok[3]) ds.Add(-b[3]); if (ok[1]) ds.Add(b[1]); if (ok[0]) ds.Add(-b[0]);
        double delta = ds.Count >= 2 ? ds.Average() : 0;
        if (delta != 0)
        {
            double cs = Math.Cos(delta), sn = Math.Sin(delta);
            var nla = la * cs + sa * sn; var nsa = la * (-sn) + sa * cs; la = nla; sa = nsa;
        }
        // Lage: Mittelpunkt und Größe aus den Kantenabständen
        double dl = (ok[0] ? a[0] : 0) - (ok[1] ? a[1] : 0), ds2 = (ok[2] ? a[2] : 0) - (ok[3] ? a[3] : 0);
        longLen += (ok[0] ? a[0] : 0) + (ok[1] ? a[1] : 0); shortLen += (ok[2] ? a[2] : 0) + (ok[3] ? a[3] : 0);
        center = center + la * (dl / 2) + sa * (ds2 / 2);
        return true;
    }

    static double Median(List<double> v) { v.Sort(); int c = v.Count; return c == 0 ? 0 : c % 2 == 1 ? v[c / 2] : (v[c / 2 - 1] + v[c / 2]) / 2; }
}
