using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>Brücke Raster ⇄ System.Drawing (nur hier und in der UI wird GDI+ benutzt).</summary>
static class BitmapCodec
{
    public sealed class Loaded { public Raster Image = null!; public double Dpi; }

    /// <summary>JPG/PNG/BMP/TIFF/GIF oder PDF (Scanner-PDFs mit eingebettetem Bild).</summary>
    public static List<Loaded> LoadFile(string path, List<string>? problems = null)
    {
        var bytes = File.ReadAllBytes(path);
        var res = new List<Loaded>();
        if (bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F')
        {
            foreach (var img in PdfImages.Extract(bytes, problems))
            {
                if (img.Pixels != null) res.Add(new Loaded { Image = img.Pixels, Dpi = 0 });
                else if (img.Jpeg != null) res.Add(FromBytes(img.Jpeg));
            }
            if (res.Count == 0) problems?.Add("Im PDF wurde kein Scan-Bild gefunden.");
            return res;
        }
        res.Add(FromBytes(bytes));
        return res;
    }

    public static Loaded FromBytes(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var bmp = new Bitmap(ms);   // erste Seite bei Mehrseiten-TIFF
        return new Loaded { Image = ToRaster(bmp), Dpi = bmp.HorizontalResolution > 50 && bmp.HorizontalResolution < 2400 ? bmp.HorizontalResolution : 0 };
    }

    public static Raster ToRaster(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        var r = new Raster(w, h);
        var rect = new Rectangle(0, 0, w, h);
        var bd = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[Math.Abs(bd.Stride)];
            for (int y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, w * 3);
                for (int x = 0, o = y * w * 3; x < w; x++, o += 3) { r.Data[o] = row[x * 3 + 2]; r.Data[o + 1] = row[x * 3 + 1]; r.Data[o + 2] = row[x * 3]; }
            }
        }
        finally { src.UnlockBits(bd); }
        return r;
    }

    public static Bitmap ToBitmap(Raster r)
    {
        var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, r.Width, r.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[Math.Abs(bd.Stride)];
            for (int y = 0; y < r.Height; y++)
            {
                for (int x = 0, o = y * r.Width * 3; x < r.Width; x++, o += 3) { row[x * 3] = r.Data[o + 2]; row[x * 3 + 1] = r.Data[o + 1]; row[x * 3 + 2] = r.Data[o]; }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, r.Width * 3);
            }
        }
        finally { bmp.UnlockBits(bd); }
        return bmp;
    }

    public static byte[] EncodeJpeg(Raster r, int quality, int dpi)
    {
        using var bmp = ToBitmap(r);
        bmp.SetResolution(dpi, dpi);
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Max(30, Math.Min(100, quality)));
        using var ms = new MemoryStream();
        bmp.Save(ms, codec, ep);
        return ms.ToArray();
    }

    public static Bitmap Thumbnail(Raster r, int maxW, int maxH)
    {
        double k = Math.Min((double)maxW / r.Width, (double)maxH / r.Height);
        var small = k < 1 ? r.Resize(Math.Max(1, (int)(r.Width * k)), Math.Max(1, (int)(r.Height * k))) : r;
        return ToBitmap(small);
    }
}
