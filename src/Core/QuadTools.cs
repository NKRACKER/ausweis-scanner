using System;
using System.Collections.Generic;
using System.Linq;

namespace AusweisScanner.Core;

/// <summary>Geometrie für von Hand markierte Karten: Ecken ordnen, Treffer prüfen, Karte daraus bauen.</summary>
public static class QuadTools
{
    static double Dist(PointD a, PointD b) { double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }

    /// <summary>Beliebig angeklickte vier Ecken → links-oben, rechts-oben, rechts-unten, links-unten; Kante 0→1 ist die lange Seite.</summary>
    public static PointD[] Normalize(IList<PointD> pts)
    {
        if (pts.Count != 4) throw new ArgumentException("Vier Ecken erforderlich.");
        double cx = pts.Average(p => p.X), cy = pts.Average(p => p.Y);
        var s = pts.OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx)).ToArray();   // Bildkoordinaten (y nach unten): aufsteigender Winkel = im Uhrzeigersinn
        int start = Dist(s[0], s[1]) >= Dist(s[1], s[2]) ? 0 : 1;
        int alt = (start + 2) % 4;                                            // gleichwertiger Start um 180° versetzt → den oben-links liegenden nehmen
        if (s[alt].X + s[alt].Y < s[start].X + s[start].Y) start = alt;
        return Enumerable.Range(0, 4).Select(i => s[(start + i) % 4]).ToArray();
    }

    /// <summary>Punkt im (konvexen) Viereck?</summary>
    public static bool Contains(IList<PointD> q, PointD p)
    {
        int sign = 0;
        for (int i = 0; i < q.Count; i++)
        {
            var a = q[i]; var b = q[(i + 1) % q.Count];
            double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            int sg = cross > 0 ? 1 : cross < 0 ? -1 : 0;
            if (sg == 0) continue;
            if (sign == 0) sign = sg; else if (sign != sg) return false;
        }
        return true;
    }

    /// <summary>Erkannte Karte aus vier (bereits geordneten) Ecken – Maße in Pixel und mm aus der Scan-Auflösung.</summary>
    public static DetectedCard ToCard(PointD[] c, double dpi)
    {
        double w = Dist(c[0], c[1]), h = Dist(c[0], c[3]);
        return new DetectedCard
        {
            Corners = new[] { c[0], c[1], c[2], c[3] }, WidthPx = w, HeightPx = h,
            WidthMm = w / dpi * 25.4, HeightMm = h / dpi * 25.4,
            AngleDeg = Math.Atan2(c[1].Y - c[0].Y, c[1].X - c[0].X) * 180 / Math.PI,
            Rectangularity = 1, Confidence = 1,
        };
    }
}
