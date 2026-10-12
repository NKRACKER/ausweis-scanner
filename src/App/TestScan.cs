using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace AusweisScanner.App;

/// <summary>
/// Entwickler-Werkzeug: erzeugt einen synthetischen A4-Flachbettscan mit mehreren Fantasie-Ausweisen in drei Layouts
/// (mit Beschriftung "Name:", mit Beschriftung über dem Wert, ganz ohne Beschriftung), teils gedreht/auf dem Kopf.
/// Dient der automatischen End-zu-End-Prüfung unter Windows; enthält keine echten Personendaten.
/// </summary>
static class TestScan
{
    sealed class Person { public string Last = "", First = ""; public int Design; public double Angle; }

    public static void Make(string dir, bool darkLid, bool umlauts, int seed)
    {
        Directory.CreateDirectory(dir);
        var rnd = new Random(seed);
        var people = new List<Person>
        {
            new Person { Last = "Mustermann", First = "Max", Design = 0, Angle = -4 },
            new Person { Last = "Schmidt", First = "Lena Sophie", Design = 1, Angle = 3 },
            new Person { Last = "Becker", First = "Tom", Design = 2, Angle = 0 },
            new Person { Last = "Weber", First = "Julia", Design = 0, Angle = 181 },       // auf dem Kopf
            new Person { Last = "Fischer", First = "Anna", Design = 1, Angle = 92 },       // quer aufgelegt
            new Person { Last = "Wagner", First = "Paul", Design = 2, Angle = 8 },
        };
        if (umlauts) { people[0].Last = "Müller"; people[0].First = "Jörg"; people[5].Last = "Köhler"; people[5].First = "Bärbel"; }

        const int dpi = 300; int W = (int)(210 / 25.4 * dpi), H = (int)(297 / 25.4 * dpi);
        using var bed = new Bitmap(W, H, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bed))
        {
            g.Clear(darkLid ? Color.FromArgb(28, 28, 30) : Color.FromArgb(238, 238, 236));
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            double[] cx = { 52, 158 }, cy = { 50, 148, 246 };   // ≥ 14 mm Abstand auch bei gedrehten Karten
            for (int i = 0; i < people.Count; i++)
            {
                using var card = RenderCard(people[i], dpi);
                float px = (float)(cx[i % 2] / 25.4 * dpi), py = (float)(cy[i / 2] / 25.4 * dpi);
                var st = g.Save();
                g.TranslateTransform(px, py); g.RotateTransform((float)people[i].Angle);
                using (var sh = new SolidBrush(Color.FromArgb(darkLid ? 30 : 70, 0, 0, 0))) g.FillRectangle(sh, -card.Width / 2f + 4, -card.Height / 2f + 4, card.Width, card.Height);
                g.DrawImage(card, -card.Width / 2f, -card.Height / 2f, card.Width, card.Height);
                g.Restore(st);
            }
        }
        // leichtes Scanner-Rauschen
        var bd = bed.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[Math.Abs(bd.Stride)];
            for (int y = 0; y < H; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, W * 3);
                for (int x = 0; x < W * 3; x++) row[x] = (byte)Math.Max(0, Math.Min(255, row[x] + rnd.Next(-3, 4)));
                System.Runtime.InteropServices.Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, W * 3);
            }
        }
        finally { bed.UnlockBits(bd); }
        bed.SetResolution(dpi, dpi);
        bed.Save(Path.Combine(dir, "scan.png"), ImageFormat.Png);
        File.WriteAllText(Path.Combine(dir, "expected.txt"), string.Join("\n", people.Select(p => p.Last + "_" + p.First)) + "\n", new UTF8Encoding(false));
    }

    static Bitmap RenderCard(Person p, int dpi)
    {
        int w = (int)(85.6 / 25.4 * dpi), h = (int)(54.0 / 25.4 * dpi);
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.White);
        Font F(float px, bool bold = false) => new Font("Arial", px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Color.FromArgb(20, 20, 20)); using var grey = new SolidBrush(Color.FromArgb(110, 110, 110));
        using var blue = new SolidBrush(Color.FromArgb(40, 90, 160)); using var white = new SolidBrush(Color.White);
        void Photo()
        {
            using var b = new SolidBrush(Color.FromArgb(185, 190, 200)); g.FillRectangle(b, 24, 190, 270, 360);
            using var d = new SolidBrush(Color.FromArgb(120, 125, 140)); g.FillEllipse(d, 94, 240, 130, 150); g.FillEllipse(d, 54, 400, 210, 220);
        }
        switch (p.Design)
        {
            case 0:
                g.FillRectangle(blue, 0, 0, w, 120); g.DrawString("Schülerausweis", F(62, true), white, 24, 26);
                g.DrawString("Gymnasium Musterstadt", F(34), ink, 330, 140); Photo();
                g.DrawString("Name: " + p.Last + ", " + p.First, F(46, true), ink, 330, 250);
                g.DrawString("Klasse: 7b", F(36), ink, 330, 340); g.DrawString("Gültig bis: 31.07.2027", F(34), grey, 330, 410);
                break;
            case 1:
                g.DrawString("Stadtschule Beispiel", F(34), grey, 24, 24); Photo();
                g.DrawString("Nachname", F(26), grey, 330, 150); g.DrawString(p.Last.ToUpperInvariant(), F(60, true), ink, 326, 180);
                g.DrawString("Vorname", F(26), grey, 330, 290); g.DrawString(p.First, F(56), ink, 326, 320);
                g.DrawString("Geb.: 01.02.2012", F(30), grey, 330, 450);
                break;
            default:
                g.FillRectangle(blue, 0, 0, 40, h); g.DrawString("REALSCHULE AM PARK", F(38, true), blue, 80, 40);
                g.DrawString("SCHÜLERAUSWEIS", F(30), grey, 80, 100);
                g.DrawString(p.First + " " + p.Last, F(70, true), ink, 80, 260);
                g.DrawString("gültig bis 31.07.2027", F(30), grey, 80, 450);
                break;
        }
        using var pen = new Pen(Color.FromArgb(150, 150, 150), 3); g.DrawRectangle(pen, 1, 1, w - 3, h - 3);
        return bmp;
    }
}
