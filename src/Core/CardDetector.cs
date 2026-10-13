using System;
using System.Collections.Generic;
using System.Linq;

namespace AusweisScanner.Core;

public sealed class DetectorOptions
{
    public double Dpi = 300;
    /// <summary>Kürzeste Kartenseite (mm). Kleinere Objekte (Staub, Zettelchen) werden ignoriert.</summary>
    public double MinShortMm = 35;
    /// <summary>Längste Kartenseite (mm).</summary>
    public double MaxLongMm = 140;
    /// <summary>Seitenverhältnis lang/kurz: ID-1-Karte 1,59, A6 1,41.</summary>
    public double MinAspect = 1.15, MaxAspect = 2.1;
    /// <summary>0 = beliebige Größe im Rahmen oben; sonst Sollmaß (aus dem Profil) mit Toleranz.</summary>
    public double ExpectedWidthMm = 0, ExpectedHeightMm = 0;
    public double SizeTolerance = 0.2;
    public double MinRectangularity = 0.88;
    /// <summary>Rand, der vom erkannten Rechteck abgezogen wird (entfernt Kantenschatten/Scannerhintergrund).</summary>
    public double EdgeInsetMm = 0.6;
    public int WorkMaxSide = 1000;
    /// <summary>
    /// Schließradien (mm), die nacheinander probiert werden; das Ergebnis mit den meisten Karten gewinnt (bei Gleichstand der größere Radius).
    /// Große Radien schließen lückenhafte Ränder heller Karten auf hellem Deckel, kleine trennen eng liegende Karten.
    /// </summary>
    public double[] CloseMm = { 2.0, 1.2, 0.6 };
    /// <summary>Randstreifen des Scans (mm), der ignoriert wird: Scanrand, Schatten und Rahmen am Bildrand würden sonst alles zu einem Objekt verbinden.</summary>
    public double BorderBandMm = 1.0;
}

public sealed class DetectedCard
{
    /// <summary>Ecken im Scan (Pixel, volle Auflösung): links-oben, rechts-oben, rechts-unten, links-unten. Oben→rechts ist die lange Seite.</summary>
    public PointD[] Corners = new PointD[4];
    public double WidthPx, HeightPx, WidthMm, HeightMm;
    public double AngleDeg;
    public double Rectangularity, Confidence;
    public bool TouchesEdge;
    public List<string> Warnings = new List<string>();
}

public sealed class DetectionResult
{
    public List<DetectedCard> Cards = new List<DetectedCard>();
    public List<string> Warnings = new List<string>();
}

/// <summary>
/// Findet Karten auf einem Flachbettscan, unabhängig vom Kartendesign (nur Form/Kontrast zählen):
/// Hintergrund wird aus dem Scanrand geschätzt; Vordergrund = Farbabweichung ODER starke Kanten
/// (letzteres für helle Karten auf weißem Deckel), dann Löcher füllen, Komponenten, minimales Rechteck.
/// </summary>
public static class CardDetector
{
    public static DetectionResult Detect(Raster scan, DetectorOptions? opt = null)
    {
        opt ??= new DetectorOptions();
        DetectionResult? best = null;
        foreach (var cl in opt.CloseMm.Length > 0 ? opt.CloseMm : new[] { 0.6 })
        {
            var r = DetectWith(scan, opt, cl);
            if (best == null || r.Cards.Count > best.Cards.Count) best = r;
        }
        return best!;
    }

    static DetectionResult DetectWith(Raster scan, DetectorOptions opt, double closeMm)
    {
        var res = new DetectionResult();
        double scale = Math.Min(1.0, (double)opt.WorkMaxSide / Math.Max(scan.Width, scan.Height));
        int w = Math.Max(16, (int)Math.Round(scan.Width * scale)), h = Math.Max(16, (int)Math.Round(scan.Height * scale));
        scale = (double)w / scan.Width;
        var small = scan.Resize(w, h);
        double pxPerMm = opt.Dpi / 25.4 * scale;

        var model = MakeModel(small, Gradient(small));
        var gray = small.ToGray(); double bgGray = (model[0] * 77 + model[1] * 150 + model[2] * 29) / 256;
        var mask = BuildMask(small, pxPerMm, closeMm, opt.BorderBandMm, model);
        int bandPx = (int)Math.Round(opt.BorderBandMm * pxPerMm);
        var comps = Label(mask, w, h);
        double minArea = 0.5 * opt.MinShortMm * opt.MinShortMm * 1.15 * pxPerMm * pxPerMm;

        foreach (var c in comps)
        {
            if (c.Area < minArea * 0.25) continue;                 // Staub/Kratzer: still ignorieren
            var hull = ConvexHull(c.Boundary);
            if (hull.Count < 3) continue;
            MinRect(hull, out var center, out var u, out var v, out double lu, out double lv);
            double longLen = Math.Max(lu, lv), shortLen = Math.Min(lu, lv);
            var longAxis = lu >= lv ? u : v;
            if (longAxis.X < -1e-9 || (Math.Abs(longAxis.X) <= 1e-9 && longAxis.Y < 0)) longAxis = longAxis * -1;
            var shortAxis = new PointD(-longAxis.Y, longAxis.X);   // zeigt (bei Langseite nach rechts) nach unten

            double Lmm = longLen / pxPerMm, Smm = shortLen / pxPerMm;
            double fill = c.Area / Math.Max(1.0, longLen * shortLen);
            // Rechteckigkeit über die konvexe Hülle: Löcher in der Maske (weiße Kartenflächen auf weißem Deckel) bestrafen nicht;
            // die Füllung muss aber noch zeigen, dass das Objekt kein verstreuter Staubhaufen ist.
            double rect = fill >= 0.45 ? PolyArea(hull) / Math.Max(1.0, longLen * shortLen) : fill;
            string where = $"bei x≈{(int)(c.CenterX / scale)}, y≈{(int)(c.CenterY / scale)}";

            if (c.Area < minArea) continue;
            if (Smm < opt.MinShortMm * 0.8 || Lmm > opt.MaxLongMm * 1.25)
            {
                if (Lmm > opt.MaxLongMm * 1.25 && Smm > opt.MinShortMm) res.Warnings.Add($"Objekt {where} ist zu groß für eine Karte ({Lmm:0}×{Smm:0} mm) – berühren sich Karten?");
                continue;
            }
            double aspect = longLen / Math.Max(1.0, shortLen);
            if (aspect < opt.MinAspect || aspect > opt.MaxAspect) { res.Warnings.Add($"Objekt {where} ignoriert (Seitenverhältnis {aspect:0.00} passt nicht zu einer Karte)."); continue; }
            if (opt.ExpectedWidthMm > 0 && opt.ExpectedHeightMm > 0)
            {
                double eL = Math.Max(opt.ExpectedWidthMm, opt.ExpectedHeightMm), eS = Math.Min(opt.ExpectedWidthMm, opt.ExpectedHeightMm);
                if (Math.Abs(Lmm - eL) / eL > opt.SizeTolerance || Math.Abs(Smm - eS) / eS > opt.SizeTolerance)
                { res.Warnings.Add($"Objekt {where} ignoriert (Größe {Lmm:0}×{Smm:0} mm statt {eL:0}×{eS:0} mm)."); continue; }
            }
            if (rect < opt.MinRectangularity)
            {
                res.Warnings.Add($"Objekt {where} nicht rechteckig genug ({rect:P0}) – Karten mit etwas Abstand auflegen, nicht aneinander oder an den Rand.");
                continue;
            }

            TrimShadow(gray, w, h, bgGray, ref center, longAxis, shortAxis, ref longLen, ref shortLen, pxPerMm);
            double insetPx = opt.EdgeInsetMm * pxPerMm;
            double L = longLen - 2 * insetPx, S = shortLen - 2 * insetPx;
            var card = new DetectedCard
            {
                Corners = new[]
                {
                    Up(center - longAxis * (L / 2) - shortAxis * (S / 2), scale),
                    Up(center + longAxis * (L / 2) - shortAxis * (S / 2), scale),
                    Up(center + longAxis * (L / 2) + shortAxis * (S / 2), scale),
                    Up(center - longAxis * (L / 2) + shortAxis * (S / 2), scale),
                },
                WidthPx = L / scale, HeightPx = S / scale,
                WidthMm = Lmm, HeightMm = Smm,
                AngleDeg = Math.Atan2(longAxis.Y, longAxis.X) * 180 / Math.PI,
                Rectangularity = rect,
                TouchesEdge = c.MinX <= bandPx + 2 || c.MinY <= bandPx + 2 || c.MaxX >= w - bandPx - 3 || c.MaxY >= h - bandPx - 3,
            };
            card.Confidence = Math.Min(1.0, Math.Max(0, (rect - 0.8) / 0.18));
            if (card.TouchesEdge) { card.Confidence *= 0.6; card.Warnings.Add("Karte liegt am Scanrand – evtl. abgeschnitten."); }
            res.Cards.Add(card);
        }
        res.Cards = SortReadingOrder(res.Cards);
        return res;
    }

    /// <summary>
    /// Findet die Karte in einem von Hand gezogenen Bereich (<paramref name="roi"/> in Scan-Pixeln): Der Bereich muss nur grob um die Karte liegen.
    /// Liefert die genauen vier Ecken oder null, wenn dort nichts Kartenähnliches gefunden wird.
    /// </summary>
    public static DetectedCard? FitCard(Raster scan, RectD roi, DetectorOptions? opt = null)
    {
        opt ??= new DetectorOptions();
        double scale = Math.Min(1.0, (double)opt.WorkMaxSide / Math.Max(scan.Width, scan.Height));
        int w = Math.Max(16, (int)Math.Round(scan.Width * scale)), h = Math.Max(16, (int)Math.Round(scan.Height * scale));
        scale = (double)w / scan.Width;
        var small = scan.Resize(w, h);
        double pxPerMm = opt.Dpi / 25.4 * scale;
        var model = MakeModel(small, Gradient(small));          // Hintergrund immer aus dem ganzen Scan (im Ausschnitt läge der Rand auf der Karte)

        int pad = (int)Math.Round(10 * pxPerMm);
        int x0 = Math.Max(0, (int)Math.Floor(roi.X * scale) - pad), y0 = Math.Max(0, (int)Math.Floor(roi.Y * scale) - pad);
        int x1 = Math.Min(w, (int)Math.Ceiling(roi.Right * scale) + pad), y1 = Math.Min(h, (int)Math.Ceiling(roi.Bottom * scale) + pad);
        if (x1 - x0 < 8 || y1 - y0 < 8) return null;
        var sub = small.Crop(x0, y0, x1 - x0, y1 - y0);
        var subGray = sub.ToGray(); double bgGray = (model[0] * 77 + model[1] * 150 + model[2] * 29) / 256;
        double cx = roi.X * scale + roi.W * scale / 2 - x0, cy = roi.Y * scale + roi.H * scale / 2 - y0;   // Mitte des Bereichs im Ausschnitt

        foreach (var cl in new[] { 2.0, 1.2, 0.6 })
        {
            var mask = BuildMask(sub, pxPerMm, cl, 0, model);
            Comp? bestC = null; List<PointD>? bestHull = null; double bestArea = 0;
            foreach (var c in Label(mask, sub.Width, sub.Height))
            {
                if (c.Area < 0.5 * opt.MinShortMm * opt.MinShortMm * pxPerMm * pxPerMm) continue;
                var hull = ConvexHull(c.Boundary);
                if (hull.Count < 3 || !QuadTools.Contains(hull.Select(p => p).ToList(), new PointD(cx, cy))) continue;   // muss die Mitte des gezogenen Bereichs enthalten
                if (c.Area > bestArea) { bestC = c; bestHull = hull; bestArea = c.Area; }
            }
            if (bestC == null || bestHull == null) continue;
            MinRect(bestHull, out var center, out var u, out var v, out double lu, out double lv);
            double longLen = Math.Max(lu, lv), shortLen = Math.Min(lu, lv);
            double Lmm = longLen / pxPerMm, Smm = shortLen / pxPerMm, aspect = longLen / Math.Max(1.0, shortLen);
            if (Smm < opt.MinShortMm * 0.8 || Lmm > opt.MaxLongMm * 1.25 || aspect < opt.MinAspect || aspect > opt.MaxAspect) continue;   // zu klein/groß/unförmig → mit kleinerem Radius erneut (evtl. mit Nachbarn verbunden)
            if (PolyArea(bestHull) / Math.Max(1.0, longLen * shortLen) < 0.85) continue;   // Teilstück oder Gebilde statt einer Karte
            var longAxis = lu >= lv ? u : v;
            if (longAxis.X < -1e-9 || (Math.Abs(longAxis.X) <= 1e-9 && longAxis.Y < 0)) longAxis = longAxis * -1;
            var shortAxis = new PointD(-longAxis.Y, longAxis.X);
            TrimShadow(subGray, sub.Width, sub.Height, bgGray, ref center, longAxis, shortAxis, ref longLen, ref shortLen, pxPerMm);
            double insetPx = opt.EdgeInsetMm * pxPerMm, L = longLen - 2 * insetPx, S = shortLen - 2 * insetPx;
            PointD Back(PointD p) => new PointD((p.X + x0) / scale, (p.Y + y0) / scale);
            return new DetectedCard
            {
                Corners = new[]
                {
                    Back(center - longAxis * (L / 2) - shortAxis * (S / 2)), Back(center + longAxis * (L / 2) - shortAxis * (S / 2)),
                    Back(center + longAxis * (L / 2) + shortAxis * (S / 2)), Back(center - longAxis * (L / 2) + shortAxis * (S / 2)),
                },
                WidthPx = L / scale, HeightPx = S / scale, WidthMm = Lmm, HeightMm = Smm,
                AngleDeg = Math.Atan2(longAxis.Y, longAxis.X) * 180 / Math.PI, Rectangularity = PolyArea(bestHull) / Math.Max(1.0, longLen * shortLen), Confidence = 1,
            };
        }
        return null;
    }

    static PointD Up(PointD p, double scale) => new PointD(p.X / scale, p.Y / scale);

    static List<DetectedCard> SortReadingOrder(List<DetectedCard> cards)
    {
        if (cards.Count < 2) return cards;
        double CY(DetectedCard c) => c.Corners.Average(p => p.Y);
        double CX(DetectedCard c) => c.Corners.Average(p => p.X);
        double rowTol = cards.Average(c => Math.Min(c.WidthPx, c.HeightPx)) * 0.6;
        var byY = cards.OrderBy(CY).ToList();
        var rows = new List<List<DetectedCard>>();
        foreach (var c in byY)
        {
            var row = rows.FirstOrDefault(r => Math.Abs(r.Average(CY) - CY(c)) < rowTol);
            if (row == null) rows.Add(new List<DetectedCard> { c }); else row.Add(c);
        }
        return rows.OrderBy(r => r.Average(CY)).SelectMany(r => r.OrderBy(CX)).ToList();
    }

    // ---------- Maske ----------
    /// <summary>Sobel-Betrag (für Kanten).</summary>
    static int[] Gradient(Raster img)
    {
        int w = img.Width, h = img.Height;
        var gray = img.ToGray();
        var grad = new int[w * h];
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                int gx = -gray[i - w - 1] - 2 * gray[i - 1] - gray[i + w - 1] + gray[i - w + 1] + 2 * gray[i + 1] + gray[i + w + 1];
                int gy = -gray[i - w - 1] - 2 * gray[i - w] - gray[i - w + 1] + gray[i + w - 1] + 2 * gray[i + w] + gray[i + w + 1];
                grad[i] = (Math.Abs(gx) + Math.Abs(gy)) / 4;
            }
        return grad;
    }

    /// <summary>Hintergrundmodell aus dem Scanrand: {R, G, B, Farbschwelle, Kantenschwelle}.</summary>
    static double[] MakeModel(Raster img, int[] grad)
    {
        int w = img.Width, h = img.Height;
        int ring = Math.Max(2, (int)(0.015 * Math.Min(w, h)));
        var rs = new List<byte>(); var gs = new List<byte>(); var bs = new List<byte>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (x < ring || y < ring || x >= w - ring || y >= h - ring) { img.Get(x, y, out var r, out var g, out var b); rs.Add(r); gs.Add(g); bs.Add(b); }
        byte br = Median(rs), bg = Median(gs), bb = Median(bs);
        var ringDist = new List<byte>(); var ringGrad = new List<byte>();
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
                if (x < ring || y < ring || x >= w - ring || y >= h - ring)
                {
                    img.Get(x, y, out var r, out var g, out var b);
                    ringDist.Add((byte)Math.Max(Math.Abs(r - br), Math.Max(Math.Abs(g - bg), Math.Abs(b - bb))));
                    ringGrad.Add((byte)Math.Min(255, grad[y * w + x]));
                }
        return new double[] { br, bg, bb, Clamp(Median(ringDist) * 6 + 18, 22, 70), Clamp(Median(ringGrad) * 4 + 18, 24, 90) };
    }

    /// <param name="model">Hintergrundmodell; null = aus dem Rand von <paramref name="img"/> bestimmen (bei Ausschnitten das des ganzen Scans übergeben).</param>
    static bool[] BuildMask(Raster img, double pxPerMm, double closeMm, double bandMm, double[]? model = null)
    {
        int w = img.Width, h = img.Height;
        var grad = Gradient(img);
        model ??= MakeModel(img, grad);
        double br = model[0], bg = model[1], bb = model[2], thr = model[3], egThr = model[4];

        var mask = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                img.Get(x, y, out var r, out var g, out var b);
                double d = Math.Max(Math.Abs(r - br), Math.Max(Math.Abs(g - bg), Math.Abs(b - bb)));
                mask[y * w + x] = d > thr || grad[y * w + x] > egThr;
            }

        int band = (int)Math.Round(bandMm * pxPerMm);
        if (band > 0) for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (x < band || y < band || x >= w - band || y >= h - band) mask[y * w + x] = false;
        int rad = Math.Max(1, (int)Math.Round(closeMm * pxPerMm));
        mask = Erode(Dilate(mask, w, h, rad), w, h, rad);   // Lücken in der Kontur schließen
        FillHoles(mask, w, h);
        mask = Dilate(Erode(mask, w, h, 1), w, h, 1);        // dünne Linien (Scanrand, Kratzer) entfernen
        return mask;
    }

    static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    static byte Median(List<byte> l)
    {
        if (l.Count == 0) return 0;
        var cnt = new int[256]; foreach (var b in l) cnt[b]++;
        int half = l.Count / 2, acc = 0;
        for (int i = 0; i < 256; i++) { acc += cnt[i]; if (acc > half) return (byte)i; }
        return 255;
    }

    static bool[] Dilate(bool[] m, int w, int h, int r) => Morph(m, w, h, r, true);
    static bool[] Erode(bool[] m, int w, int h, int r) => Morph(m, w, h, r, false);

    // separierbares Max-/Min-Filter (Rechteck-Strukturelement). Außerhalb des Bildes: neutral (Dilatation: leer, Erosion: voll)
    static bool[] Morph(bool[] m, int w, int h, int r, bool dilate)
    {
        var tmp = new bool[m.Length]; var o = new bool[m.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool acc = !dilate;
                for (int k = -r; k <= r; k++)
                {
                    int xx = x + k; bool v = xx < 0 || xx >= w ? !dilate : m[y * w + xx];
                    if (dilate ? v : !v) { acc = dilate; break; }
                }
                tmp[y * w + x] = acc;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool acc = !dilate;
                for (int k = -r; k <= r; k++)
                {
                    int yy = y + k; bool v = yy < 0 || yy >= h ? !dilate : tmp[yy * w + x];
                    if (dilate ? v : !v) { acc = dilate; break; }
                }
                o[y * w + x] = acc;
            }
        return o;
    }

    static void FillHoles(bool[] m, int w, int h)
    {
        var outside = new bool[m.Length]; var st = new Stack<int>();
        void Push(int x, int y) { int i = y * w + x; if (!m[i] && !outside[i]) { outside[i] = true; st.Push(i); } }
        for (int x = 0; x < w; x++) { Push(x, 0); Push(x, h - 1); }
        for (int y = 0; y < h; y++) { Push(0, y); Push(w - 1, y); }
        while (st.Count > 0)
        {
            int i = st.Pop(), x = i % w, y = i / w;
            if (x > 0) Push(x - 1, y); if (x < w - 1) Push(x + 1, y);
            if (y > 0) Push(x, y - 1); if (y < h - 1) Push(x, y + 1);
        }
        for (int i = 0; i < m.Length; i++) if (!outside[i]) m[i] = true;
    }

    // ---------- Komponenten ----------
    sealed class Comp
    {
        public int Area, MinX = int.MaxValue, MinY = int.MaxValue, MaxX = -1, MaxY = -1;
        public double SumX, SumY;
        public List<PointD> Boundary = new List<PointD>();
        public double CenterX => SumX / Area;
        public double CenterY => SumY / Area;
    }

    static List<Comp> Label(bool[] m, int w, int h)
    {
        var seen = new bool[m.Length]; var comps = new List<Comp>(); var st = new Stack<int>();
        for (int s = 0; s < m.Length; s++)
        {
            if (!m[s] || seen[s]) continue;
            var c = new Comp(); seen[s] = true; st.Push(s);
            while (st.Count > 0)
            {
                int i = st.Pop(), x = i % w, y = i / w;
                c.Area++; c.SumX += x; c.SumY += y;
                if (x < c.MinX) c.MinX = x; if (x > c.MaxX) c.MaxX = x; if (y < c.MinY) c.MinY = y; if (y > c.MaxY) c.MaxY = y;
                bool edge = false;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) { edge = true; continue; }
                        int ni = ny * w + nx;
                        if (!m[ni]) { edge = true; continue; }
                        if (!seen[ni]) { seen[ni] = true; st.Push(ni); }
                    }
                if (edge) c.Boundary.Add(new PointD(x, y));
            }
            comps.Add(c);
        }
        return comps;
    }

    // ---------- Geometrie ----------
    /// <summary>
    /// Der Scanner wirft einen weichen Schatten neben die Karte; er gehört zur erkannten Fläche, aber nicht zur Karte.
    /// Je Kante wird bis zu 3,5 mm nach innen nach der stärksten Helligkeitskante gesucht und die Kante dorthin gesetzt –
    /// aber nur, wenn der Streifen davor deutlich dunkler als der Hintergrund ist (echter Schatten). So fressen Foto-Kanten im Inneren den Zuschnitt nicht an.
    /// </summary>
    static void TrimShadow(byte[] g, int w, int h, double bgGray, ref PointD center, PointD la, PointD sa, ref double longLen, ref double shortLen, double ppm)
    {
        var dirs = new[] { la, la * -1, sa, sa * -1 };
        var tang = new[] { sa, sa, la, la };
        double[] half = { longLen / 2, longLen / 2, shortLen / 2, shortLen / 2 };
        double[] tlen = { shortLen, shortLen, longLen, longLen };
        var off = new double[4];
        double inPx = 3.5 * ppm, outPx = 0.6 * ppm; const double step = 0.5;
        int n = (int)((inPx + outPx) / step) + 1;
        for (int k = 0; k < 4; k++)
        {
            var m = new double[n]; bool ok = true;
            for (int j = 0; j < n && ok; j++)
            {
                double o = -inPx + j * step, sum = 0; int cnt = 0;
                for (double t = -0.35 * tlen[k]; t <= 0.35 * tlen[k]; t += 1)
                {
                    int x = (int)Math.Round(center.X + dirs[k].X * (half[k] + o) + tang[k].X * t), y = (int)Math.Round(center.Y + dirs[k].Y * (half[k] + o) + tang[k].Y * t);
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    sum += g[y * w + x]; cnt++;
                }
                if (cnt < 8) ok = false; else m[j] = sum / cnt;
            }
            if (!ok) continue;
            var sm = new double[n]; for (int j = 0; j < n; j++) sm[j] = (m[Math.Max(0, j - 1)] + m[j] + m[Math.Min(n - 1, j + 1)]) / 3;
            int best = -1; double bestD = 0, localD = 0;
            for (int j = 2; j < n - 2; j++)
            {
                double d = Math.Abs(sm[j + 1] - sm[j - 1]), o = -inPx + j * step;
                if (d > bestD) { bestD = d; best = j; }
                if (Math.Abs(o) <= 0.4 * ppm && d > localD) localD = d;
            }
            if (best < 0 || bestD < 6 || bestD < 1.5 * Math.Max(1, localD)) continue;
            double oBest = -inPx + best * step;
            if (oBest >= -0.3 * ppm) continue;                                  // Kante liegt schon (fast) richtig
            // Streifen zwischen Kante und Spitze: dunkler als der Hintergrund (Schatten) und die Karte dahinter deutlich heller?
            double strip = 0; int sc = 0; for (int j = best; j < n; j++) { if (-inPx + j * step > 0) break; strip += sm[j]; sc++; }
            int inside = Math.Max(0, best - (int)(1.0 * ppm / step));
            double cardLevel = sm[inside];
            if (sc == 0 || strip / sc > bgGray - 6 || cardLevel - strip / sc < 10) continue;
            off[k] = oBest;
        }
        longLen += off[0] + off[1]; shortLen += off[2] + off[3];
        center = center + la * ((off[0] - off[1]) / 2) + sa * ((off[2] - off[3]) / 2);
    }

    static double PolyArea(List<PointD> h)
    {
        double a = 0;
        for (int i = 0; i < h.Count; i++) { var p = h[i]; var q = h[(i + 1) % h.Count]; a += p.X * q.Y - q.X * p.Y; }
        return Math.Abs(a) / 2;
    }

    static List<PointD> ConvexHull(List<PointD> pts)
    {
        var p = pts.OrderBy(a => a.X).ThenBy(a => a.Y).ToList();
        if (p.Count < 3) return p;
        double Cross(PointD o, PointD a, PointD b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<PointD>();
        foreach (var a in p) { while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], a) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(a); }
        int lower = hull.Count + 1;
        for (int i = p.Count - 2; i >= 0; i--) { var a = p[i]; while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], a) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(a); }
        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    /// <summary>Minimales umschließendes Rechteck (Kanten der Hülle als Kandidaten-Richtungen). u,v = Einheitsachsen, lu/lv = Längen.</summary>
    static void MinRect(List<PointD> hull, out PointD center, out PointD u, out PointD v, out double lu, out double lv)
    {
        double best = double.MaxValue; center = default; u = default; v = default; lu = lv = 0;
        for (int i = 0; i < hull.Count; i++)
        {
            var e = hull[(i + 1) % hull.Count] - hull[i]; double len = Math.Sqrt(e.X * e.X + e.Y * e.Y);
            if (len < 1e-9) continue;
            var a = new PointD(e.X / len, e.Y / len); var b = new PointD(-a.Y, a.X);
            double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
            foreach (var p in hull)
            {
                double pa = p.X * a.X + p.Y * a.Y, pb = p.X * b.X + p.Y * b.Y;
                if (pa < minA) minA = pa; if (pa > maxA) maxA = pa; if (pb < minB) minB = pb; if (pb > maxB) maxB = pb;
            }
            double area = (maxA - minA) * (maxB - minB);
            if (area < best)
            {
                best = area; u = a; v = b; lu = maxA - minA; lv = maxB - minB;
                double ca = (minA + maxA) / 2, cb = (minB + maxB) / 2;
                center = new PointD(a.X * ca + b.X * cb, a.Y * ca + b.Y * cb);
            }
        }
        lu += 1; lv += 1; // Randpixel mitzählen
    }
}

public static class CardExtractor
{
    /// <summary>Schneidet die Karte entzerrt aus dem Scan (Querformat, Ausgabe in voller Scan-Auflösung).</summary>
    public static Raster Extract(Raster scan, DetectedCard c)
    {
        int W = Math.Max(8, (int)Math.Round(c.WidthPx)), H = Math.Max(8, (int)Math.Round(c.HeightPx));
        var tl = c.Corners[0]; var tr = c.Corners[1]; var bl = c.Corners[3];
        var ux = (tr - tl) * (1.0 / W); var vy = (bl - tl) * (1.0 / H);
        var o = new Raster(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var p = tl + ux * (x + 0.5) + vy * (y + 0.5);
                scan.Sample(p.X - 0.5, p.Y - 0.5, out var r, out var g, out var b);
                o.Set(x, y, r, g, b);
            }
        return o;
    }
}
