using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace AusweisScanner.Core;

/// <summary>Minimaler PNG-Encoder (RGB, Sub-Filter, mit dpi-Angabe). Spart System.Drawing im Kern.</summary>
public static class PngWriter
{
    public static byte[] Encode(Raster r, int dpi = 300)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
        var ihdr = new byte[13];
        Be(ihdr, 0, r.Width); Be(ihdr, 4, r.Height); ihdr[8] = 8; ihdr[9] = 2; // 8 Bit, RGB
        Chunk(ms, "IHDR", ihdr);
        int ppm = (int)Math.Round(dpi / 0.0254);
        var phys = new byte[9]; Be(phys, 0, ppm); Be(phys, 4, ppm); phys[8] = 1;
        Chunk(ms, "pHYs", phys);

        var raw = new byte[(r.Width * 3 + 1) * r.Height];
        for (int y = 0; y < r.Height; y++)
        {
            int o = y * (r.Width * 3 + 1), s = y * r.Width * 3; raw[o] = 1; // Filter "Sub"
            for (int i = 0; i < r.Width * 3; i++) raw[o + 1 + i] = (byte)(r.Data[s + i] - (i >= 3 ? r.Data[s + i - 3] : 0));
        }
        using var z = new MemoryStream();
        z.WriteByte(0x78); z.WriteByte(0x9C);
        using (var ds = new DeflateStream(z, CompressionLevel.Optimal, true)) ds.Write(raw, 0, raw.Length);
        uint a = 1, b = 0; foreach (var by in raw) { a = (a + by) % 65521; b = (b + a) % 65521; }
        var ad = new byte[4]; Be(ad, 0, (int)((b << 16) | a)); z.Write(ad, 0, 4);
        Chunk(ms, "IDAT", z.ToArray());
        Chunk(ms, "IEND", new byte[0]);
        return ms.ToArray();
    }

    static void Be(byte[] d, int o, int v) { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; Be(len, 0, data.Length); s.Write(len, 0, 4);
        var t = Encoding.ASCII.GetBytes(type); s.Write(t, 0, 4); s.Write(data, 0, data.Length);
        uint crc = 0xFFFFFFFF; crc = Crc(crc, t); crc = Crc(crc, data);
        var c = new byte[4]; Be(c, 0, (int)~crc); s.Write(c, 0, 4);
    }

    static uint[]? _tbl;
    static uint Crc(uint crc, byte[] d)
    {
        if (_tbl == null) { _tbl = new uint[256]; for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; _tbl[n] = c; } }
        foreach (var b in d) crc = _tbl[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}

/// <summary>Einseitiges PDF mit einem JPEG-Bild in Kartengröße. Keine Metadaten (Datenschutz: kein Autor/Software/Datum).</summary>
public static class PdfWriter
{
    public static byte[] SingleJpegPage(byte[] jpeg, int pxW, int pxH, double pageWmm, double pageHmm)
    {
        double wp = pageWmm / 25.4 * 72, hp = pageHmm / 25.4 * 72;
        string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        var content = $"q {F(wp)} 0 0 {F(hp)} 0 0 cm /Im0 Do Q";
        using var ms = new MemoryStream(); var offs = new List<long>();
        void Raw(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
        Raw("%PDF-1.4\n");
        void Obj(string body) { offs.Add(ms.Position); Raw($"{offs.Count} 0 obj\n{body}\nendobj\n"); }
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(wp)} {F(hp)}] /Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>");
        Obj($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        offs.Add(ms.Position);
        Raw($"5 0 obj\n<< /Type /XObject /Subtype /Image /Width {pxW} /Height {pxH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
        ms.Write(jpeg, 0, jpeg.Length); Raw("\nendstream\nendobj\n");
        long xref = ms.Position;
        Raw($"xref\n0 {offs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offs) Raw(o.ToString("D10") + " 00000 n \n");
        Raw($"trailer\n<< /Size {offs.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}

public sealed class PdfImage
{
    public byte[]? Jpeg;       // direkt als JPEG ablegbar/ladbar
    public Raster? Pixels;     // unkomprimierte (Flate) RGB/Grau-Bilder
}

/// <summary>
/// Holt eingebettete Seitenbilder aus von Scannern erzeugten PDFs (JPEG/DCT oder Flate-RGB/Grau ohne Predictor).
/// Kein vollständiger PDF-Renderer – reicht für Scanner-Software, die pro Seite ein Bild ablegt.
/// </summary>
public static class PdfImages
{
    public static List<PdfImage> Extract(byte[] pdf, List<string>? problems = null)
    {
        var res = new List<PdfImage>();
        var s = Encoding.GetEncoding(28591).GetString(pdf);
        int pos = 0;
        while (true)
        {
            int st = s.IndexOf("stream", pos, StringComparison.Ordinal);
            if (st < 0) break;
            if (st >= 3 && string.CompareOrdinal(s, st - 3, "end", 0, 3) == 0) { pos = st + 6; continue; }
            int objStart = s.LastIndexOf(" obj", st, StringComparison.Ordinal);
            string dict = objStart >= 0 ? s.Substring(objStart, st - objStart) : "";
            int dataStart = st + 6;
            if (dataStart < s.Length && s[dataStart] == '\r') dataStart++;
            if (dataStart < s.Length && s[dataStart] == '\n') dataStart++;
            int end = s.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (end < 0) break;
            pos = end + 9;
            if (!Regex.IsMatch(dict, @"/Subtype\s*/Image")) continue;
            int dlen = end - dataStart;
            while (dlen > 0 && (pdf[dataStart + dlen - 1] == '\n' || pdf[dataStart + dlen - 1] == '\r')) dlen--;
            var data = new byte[dlen]; Buffer.BlockCopy(pdf, dataStart, data, 0, dlen);
            if (dict.Contains("/DCTDecode")) { res.Add(new PdfImage { Jpeg = data }); continue; }
            if (dict.Contains("/FlateDecode"))
            {
                int w = Int(dict, "/Width"), h = Int(dict, "/Height"), bpc = Int(dict, "/BitsPerComponent");
                if (dict.Contains("/Predictor") && Int(dict, "/Predictor") > 1) { problems?.Add("PDF-Bild mit Predictor wird nicht unterstützt – bitte Scanner auf JPEG-Ausgabe stellen oder als JPG/PNG speichern."); continue; }
                bool rgb = dict.Contains("/DeviceRGB"), gray = dict.Contains("/DeviceGray");
                if (w <= 0 || h <= 0 || bpc != 8 || !(rgb || gray)) { problems?.Add("PDF-Bildformat wird nicht unterstützt – bitte als JPG/PNG speichern."); continue; }
                try
                {
                    int ch = rgb ? 3 : 1; var raw = new byte[w * h * ch];
                    using (var ds = new DeflateStream(new MemoryStream(data, 2, data.Length - 2), CompressionMode.Decompress))
                    { int got = 0, n; while (got < raw.Length && (n = ds.Read(raw, got, raw.Length - got)) > 0) got += n; }
                    var r = new Raster(w, h);
                    for (int i = 0; i < w * h; i++)
                        if (rgb) { r.Data[i * 3] = raw[i * 3]; r.Data[i * 3 + 1] = raw[i * 3 + 1]; r.Data[i * 3 + 2] = raw[i * 3 + 2]; }
                        else r.Data[i * 3] = r.Data[i * 3 + 1] = r.Data[i * 3 + 2] = raw[i];
                    res.Add(new PdfImage { Pixels = r });
                }
                catch (Exception e) { problems?.Add("PDF-Bild nicht lesbar: " + e.Message); }
            }
            else problems?.Add("PDF-Bild mit nicht unterstütztem Filter übersprungen.");
        }
        return res;
    }

    static int Int(string dict, string key)
    {
        var m = Regex.Match(dict, Regex.Escape(key) + @"\s+(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }
}
