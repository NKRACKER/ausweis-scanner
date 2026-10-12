using System;
using System.Collections.Generic;
using AusweisScanner.Core;

namespace AusweisScanner.Tests;

/// <summary>Erzeugt synthetische Flachbettscans (A4) mit Karten – ohne Schrift, nur Form/Kontrast.</summary>
public static class SyntheticScan
{
    public sealed class Card
    {
        public double CxMm, CyMm, AngleDeg, WMm = 85.6, HMm = 54.0;
        public byte Fill = 250;      // Kartenfarbe (Grau)
        public bool Border = true;   // dünne Kante/Schatten wie im echten Scan
    }

    public static Raster Make(IEnumerable<Card> cards, byte lid = 20, double dpi = 150, int seed = 1, bool dust = false, double noise = 3)
    {
        int w = (int)(210 / 25.4 * dpi), h = (int)(297 / 25.4 * dpi);
        var img = new Raster(w, h);
        var rnd = new Random(seed);
        for (int i = 0; i < img.Data.Length; i += 3)
        {
            byte v = (byte)Math.Max(0, Math.Min(255, lid + rnd.NextDouble() * noise * 2 - noise));
            img.Data[i] = img.Data[i + 1] = img.Data[i + 2] = v;
        }
        double pxmm = dpi / 25.4;
        foreach (var c in cards) Draw(img, c, pxmm, rnd);
        if (dust)
            for (int k = 0; k < 40; k++)
            {
                int x = rnd.Next(10, w - 10), y = rnd.Next(10, h - 10), r = rnd.Next(1, 4);
                for (int yy = -r; yy <= r; yy++) for (int xx = -r; xx <= r; xx++) img.Set(x + xx, y + yy, 90, 90, 90);
            }
        return img;
    }

    static void Draw(Raster img, Card c, double pxmm, Random rnd)
    {
        double a = c.AngleDeg * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double cx = c.CxMm * pxmm, cy = c.CyMm * pxmm, hw = c.WMm * pxmm / 2, hh = c.HMm * pxmm / 2, rad = 3.5 * pxmm;
        double ext = Math.Sqrt(hw * hw + hh * hh) + 6;
        for (int y = (int)Math.Max(0, cy - ext); y < Math.Min(img.Height, cy + ext); y++)
            for (int x = (int)Math.Max(0, cx - ext); x < Math.Min(img.Width, cx + ext); x++)
            {
                double dx = x - cx, dy = y - cy;
                double u = dx * cos + dy * sin, v = -dx * sin + dy * cos;       // Kartenkoordinaten (px)
                double qx = Math.Abs(u) - (hw - rad), qy = Math.Abs(v) - (hh - rad);
                double dist = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0) - rad; // <0 innen
                if (dist > 2.5) continue;
                if (dist > 0) { if (c.Border) img.Set(x, y, 150, 150, 150); continue; }           // Schattenrand 2,5 px
                if (c.Border && dist > -1.2) { img.Set(x, y, 200, 200, 200); continue; }          // Kartenkante
                byte f = c.Fill;
                // Inhalt: Foto links, "Textzeilen" rechts, farbiger Balken oben – schafft Kanten im Inneren
                double nu = (u + hw) / (2 * hw), nv = (v + hh) / (2 * hh);
                if (nu > 0.06 && nu < 0.33 && nv > 0.2 && nv < 0.85) f = 90;
                else if (nu > 0.4 && nu < 0.92 && ((nv > 0.30 && nv < 0.38) || (nv > 0.5 && nv < 0.56))) f = 60;
                else if (nv < 0.12) f = (byte)(c.Fill - 70);
                img.Set(x, y, f, f, (byte)Math.Min(255, f + (c.Fill < 200 ? 0 : 0)));
            }
    }
}
