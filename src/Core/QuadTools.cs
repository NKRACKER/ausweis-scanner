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

    static PointD Unit(PointD a, PointD b) { double d = Dist(a, b); return d < 1e-9 ? new PointD(1, 0) : new PointD((b.X - a.X) / d, (b.Y - a.Y) / d); }
    static double Dot(PointD a, PointD b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Ecke <paramref name="idx"/> an Punkt <paramref name="p"/> ziehen: die gegenüberliegende Ecke bleibt stehen, die Karte behält ihre Drehung und bleibt ein Rechteck.</summary>
    public static PointD[] DragCorner(PointD[] q, int idx, PointD p, double minSize = 6)
    {
        var o = q[0]; var u = Unit(q[0], q[1]); var v = Unit(q[0], q[3]);
        double W = Dist(q[0], q[1]), H = Dist(q[0], q[3]);
        // Koordinaten (entlang u, entlang v) vom Ursprung q[0]; Ecken: 0=(0,0) 1=(W,0) 2=(W,H) 3=(0,H)
        double[][] c = { new[] { 0.0, 0 }, new[] { W, 0 }, new[] { W, H }, new[] { 0.0, H } };
        var opp = c[(idx + 2) % 4];
        var d = new PointD(p.X - o.X, p.Y - o.Y);
        double a = Dot(d, u), b = Dot(d, v);
        if (Math.Abs(a - opp[0]) < minSize) a = opp[0] + (a >= opp[0] ? minSize : -minSize);
        if (Math.Abs(b - opp[1]) < minSize) b = opp[1] + (b >= opp[1] ? minSize : -minSize);
        double u0 = Math.Min(a, opp[0]), u1 = Math.Max(a, opp[0]), v0 = Math.Min(b, opp[1]), v1 = Math.Max(b, opp[1]);
        PointD P(double x, double y) => new PointD(o.X + u.X * x + v.X * y, o.Y + u.Y * x + v.Y * y);
        return new[] { P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1) };
    }

    /// <summary>Ganze Kante parallel verschieben (Kante 0 = oben, 1 = rechts, 2 = unten, 3 = links; bezogen auf die normierten Ecken). Die Karte bleibt ein Rechteck mit derselben Drehung.</summary>
    public static PointD[] DragEdge(PointD[] q, int edge, PointD p, double minSize = 6)
    {
        var o = q[0]; var u = Unit(q[0], q[1]); var v = Unit(q[0], q[3]);
        double W = Dist(q[0], q[1]), H = Dist(q[0], q[3]);
        var d = new PointD(p.X - o.X, p.Y - o.Y);
        double a = Dot(d, u), b = Dot(d, v), u0 = 0, u1 = W, v0 = 0, v1 = H;
        switch (((edge % 4) + 4) % 4)
        {
            case 0: v0 = Math.Min(b, v1 - minSize); break;
            case 1: u1 = Math.Max(a, u0 + minSize); break;
            case 2: v1 = Math.Max(b, v0 + minSize); break;
            default: u0 = Math.Min(a, u1 - minSize); break;
        }
        PointD P(double x, double y) => new PointD(o.X + u.X * x + v.X * y, o.Y + u.Y * x + v.Y * y);
        return new[] { P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1) };
    }

    /// <summary>Karte um ihre Mitte drehen, sodass Ecke <paramref name="idx"/> zum Punkt <paramref name="p"/> zeigt (Größe bleibt).</summary>
    public static PointD[] RotateTo(PointD[] q, int idx, PointD p)
    {
        double cx = q.Average(t => t.X), cy = q.Average(t => t.Y);
        double da = Math.Atan2(p.Y - cy, p.X - cx) - Math.Atan2(q[idx].Y - cy, q[idx].X - cx);
        double cs = Math.Cos(da), sn = Math.Sin(da);
        return q.Select(t => new PointD(cx + (t.X - cx) * cs - (t.Y - cy) * sn, cy + (t.X - cx) * sn + (t.Y - cy) * cs)).ToArray();
    }

    public static PointD[] Move(PointD[] q, double dx, double dy) => q.Select(t => new PointD(t.X + dx, t.Y + dy)).ToArray();
}
