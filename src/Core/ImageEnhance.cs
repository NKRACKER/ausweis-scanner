using System;

namespace AusweisScanner.Core;

/// <summary>Bildaufbereitung für die Texterkennung: Beleuchtung glätten (Schatten/Vignette/Glanz-Verläufe), Kontrast strecken, leicht schärfen.</summary>
public static class ImageEnhance
{
    /// <summary>Graustufenbild (als RGB) mit gleichmäßigem hellem Hintergrund und kräftigem Text.</summary>
    public static Raster Normalize(Raster src)
    {
        int w = src.Width, h = src.Height;
        var gray = src.ToGray();
        // Hintergrundhelligkeit = starker Weichzeichner (Integralbild, Radius ~ 1/12 der Kartenhöhe), danach Division
        int rad = Math.Max(8, Math.Min(w, h) / 12);
        var bg = BoxBlur(gray, w, h, rad);
        var flat = new double[w * h];
        for (int i = 0; i < flat.Length; i++) flat[i] = Math.Min(255.0, gray[i] / Math.Max(24.0, bg[i]) * 232.0);
        // Kontrast: 1 %-/99 %-Perzentil auf 0..255 strecken
        var hist = new int[256]; foreach (var v in flat) hist[(int)Math.Max(0, Math.Min(255, v))]++;
        int lo = Percentile(hist, flat.Length, 0.01), hi = Percentile(hist, flat.Length, 0.99);
        if (hi - lo < 40) { lo = Math.Max(0, hi - 40); }
        var stretched = new double[w * h];
        for (int i = 0; i < stretched.Length; i++) stretched[i] = Math.Max(0, Math.Min(255, (flat[i] - lo) * 255.0 / Math.Max(1, hi - lo)));
        // leichtes Unscharf-Maskieren (Radius 2) macht verwaschene Strichkanten klarer
        var q = new byte[w * h]; for (int i = 0; i < q.Length; i++) q[i] = (byte)stretched[i];
        var soft = BoxBlur(q, w, h, 2);
        var o = new Raster(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                byte v = (byte)Math.Max(0, Math.Min(255, stretched[i] + 0.8 * (stretched[i] - soft[i])));
                o.Set(x, y, v, v, v);
            }
        return o;
    }

    static int Percentile(int[] hist, int total, double p)
    {
        long acc = 0, target = (long)(total * p);
        for (int i = 0; i < 256; i++) { acc += hist[i]; if (acc >= target) return i; }
        return 255;
    }

    /// <summary>Mittelwertfilter mit quadratischem Fenster (Integralbild, Ränder klemmen).</summary>
    static double[] BoxBlur(byte[] g, int w, int h, int r)
    {
        var integ = new long[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            long row = 0;
            for (int x = 0; x < w; x++) { row += g[y * w + x]; integ[(y + 1) * (w + 1) + x + 1] = integ[y * (w + 1) + x + 1] + row; }
        }
        var o = new double[w * h];
        for (int y = 0; y < h; y++)
        {
            int y0 = Math.Max(0, y - r), y1 = Math.Min(h, y + r + 1);
            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - r), x1 = Math.Min(w, x + r + 1);
                long s = integ[y1 * (w + 1) + x1] - integ[y0 * (w + 1) + x1] - integ[y1 * (w + 1) + x0] + integ[y0 * (w + 1) + x0];
                o[y * w + x] = (double)s / ((x1 - x0) * (y1 - y0));
            }
        }
        return o;
    }
}
