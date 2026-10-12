using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AusweisScanner.Core;
using Xunit;

namespace AusweisScanner.Tests;

public class FormatTests
{
    static Raster Gradient(int w, int h) { var r = new Raster(w, h); for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) r.Set(x, y, (byte)(x * 255 / w), (byte)(y * 255 / h), (byte)((x + y) % 256)); return r; }

    [Fact]
    public void PngRoundtrip()
    {
        var src = Gradient(97, 61); var png = PngWriter.Encode(src, 300);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
        // IDAT suchen, inflaten, Sub-Filter rückgängig machen
        int p = 8; byte[]? idat = null;
        while (p < png.Length)
        {
            int len = (png[p] << 24) | (png[p + 1] << 16) | (png[p + 2] << 8) | png[p + 3]; var type = Encoding.ASCII.GetString(png, p + 4, 4);
            if (type == "IDAT") { idat = png.Skip(p + 8).Take(len).ToArray(); }
            p += 12 + len;
        }
        Assert.NotNull(idat);
        using var ds = new DeflateStream(new MemoryStream(idat!, 2, idat!.Length - 2), CompressionMode.Decompress);
        var raw = new byte[(97 * 3 + 1) * 61]; int got = 0, n; while (got < raw.Length && (n = ds.Read(raw, got, raw.Length - got)) > 0) got += n;
        for (int y = 0; y < 61; y++)
        {
            Assert.Equal(1, raw[y * 292]);
            for (int i = 0; i < 97 * 3; i++)
            {
                int v = (raw[y * 292 + 1 + i] + (i >= 3 ? src.Data[y * 97 * 3 + i - 3] : 0)) & 255;
                Assert.Equal(src.Data[y * 97 * 3 + i], (byte)v);
            }
        }
    }

    [Fact]
    public void PdfContainsImageAndExtractsBack()
    {
        var fakeJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 0xFF, 0xD9 };
        var pdf = PdfWriter.SingleJpegPage(fakeJpeg, 1011, 638, 85.6, 54);
        var s = Encoding.ASCII.GetString(pdf.Select(b => b < 128 ? b : (byte)'?').ToArray());
        Assert.StartsWith("%PDF-1.4", s); Assert.Contains("/MediaBox [0 0 242.65 153.07]", s); Assert.EndsWith("%%EOF\n", s);
        Assert.DoesNotContain("/Author", s); Assert.DoesNotContain("/Producer", s); Assert.DoesNotContain("/CreationDate", s);
        // startxref zeigt auf "xref"
        int sx = int.Parse(s.Substring(s.LastIndexOf("startxref") + 10).Split('\n')[0]);
        Assert.Equal("xref", s.Substring(sx, 4));
        var imgs = PdfImages.Extract(pdf);
        Assert.Single(imgs); Assert.Equal(fakeJpeg, imgs[0].Jpeg);
    }

    [Fact]
    public void PdfImagesReadsFlateRgb()
    {
        int w = 4, h = 2; var raw = new byte[w * h * 3]; for (int i = 0; i < raw.Length; i++) raw[i] = (byte)(i * 9);
        var z = new MemoryStream(); z.WriteByte(0x78); z.WriteByte(0x9C); using (var ds = new DeflateStream(z, CompressionLevel.Optimal, true)) ds.Write(raw, 0, raw.Length);
        var head = Encoding.ASCII.GetBytes($"%PDF-1.4\n1 0 obj\n<< /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode >>\nstream\n");
        var tail = Encoding.ASCII.GetBytes("\nendstream\nendobj\n%%EOF");
        var pdf = head.Concat(z.ToArray()).Concat(tail).ToArray();
        var imgs = PdfImages.Extract(pdf);
        Assert.Single(imgs); Assert.Equal(raw, imgs[0].Pixels!.Data);
    }

    [Fact]
    public void PdfWithPredictorReportsProblemInsteadOfGarbage()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<< /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 >> >>\nstream\nxxxx\nendstream\nendobj");
        var problems = new System.Collections.Generic.List<string>();
        Assert.Empty(PdfImages.Extract(pdf, problems)); Assert.Single(problems);
    }
}

public class EsclTests
{
    [Theory]
    [InlineData("192.168.1.20", "http://192.168.1.20/eSCL")]
    [InlineData("192.168.1.20:8080", "http://192.168.1.20:8080/eSCL")]
    [InlineData("http://printer.local/", "http://printer.local/eSCL")]
    [InlineData("https://10.0.0.5:443/eSCL/", "https://10.0.0.5/eSCL")]
    [InlineData("http://10.0.0.5/scanner", "http://10.0.0.5/scanner")]
    public void NormalizesBase(string input, string expected) => Assert.Equal(expected, EsclClient.NormalizeBase(input));

    [Fact]
    public void ScanFlowAgainstMockScanner()
    {
        int port; using (var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
        using var srv = new HttpListener(); srv.Prefixes.Add($"http://127.0.0.1:{port}/"); srv.Start();
        string? postedXml = null; bool deleted = false; int polls = 0; var image = new byte[] { 0xFF, 0xD8, 9, 9, 9, 0xFF, 0xD9 };
        var loop = Task.Run(() =>
        {
            while (srv.IsListening)
            {
                HttpListenerContext c; try { c = srv.GetContext(); } catch { break; }
                var rq = c.Request; var rs = c.Response; var path = rq.Url!.AbsolutePath;
                if (rq.HttpMethod == "GET" && path == "/eSCL/ScannerCapabilities")
                {
                    var xml = "<scan:ScannerCapabilities xmlns:scan=\"http://schemas.hp.com/imaging/escl/2011/05/03\" xmlns:pwg=\"http://www.pwg.org/schemas/2010/12/sm\"><pwg:MakeAndModel>MockScan 3000</pwg:MakeAndModel><scan:Platen><scan:PlatenInputCaps><scan:MaxWidth>2480</scan:MaxWidth><scan:MaxHeight>3508</scan:MaxHeight><scan:SettingProfiles><scan:SettingProfile><scan:ColorModes><scan:ColorMode>Grayscale8</scan:ColorMode><scan:ColorMode>RGB24</scan:ColorMode></scan:ColorModes><scan:SupportedResolutions><scan:DiscreteResolutions><scan:DiscreteResolution><scan:XResolution>200</scan:XResolution></scan:DiscreteResolution><scan:DiscreteResolution><scan:XResolution>300</scan:XResolution></scan:DiscreteResolution></scan:DiscreteResolutions></scan:SupportedResolutions></scan:SettingProfile></scan:SettingProfiles></scan:PlatenInputCaps></scan:Platen></scan:ScannerCapabilities>";
                    var b = Encoding.UTF8.GetBytes(xml); rs.ContentType = "text/xml"; rs.OutputStream.Write(b, 0, b.Length);
                }
                else if (rq.HttpMethod == "POST" && path == "/eSCL/ScanJobs")
                { postedXml = new StreamReader(rq.InputStream).ReadToEnd(); rs.StatusCode = 201; rs.AddHeader("Location", $"http://127.0.0.1:{port}/eSCL/ScanJobs/job1"); }
                else if (rq.HttpMethod == "GET" && path == "/eSCL/ScanJobs/job1/NextDocument")
                { if (++polls < 3) rs.StatusCode = 503; else { rs.ContentType = "image/jpeg"; rs.OutputStream.Write(image, 0, image.Length); } }
                else if (rq.HttpMethod == "DELETE") deleted = true;
                else rs.StatusCode = 404;
                rs.Close();
            }
        });
        try
        {
            using var cl = new EsclClient($"127.0.0.1:{port}");
            var caps = cl.GetCapabilities();
            Assert.Equal("MockScan 3000", caps.Model); Assert.Equal(2480, caps.MaxWidth); Assert.Contains(300, caps.Resolutions); Assert.Contains("RGB24", caps.ColorModes);
            var got = cl.ScanJpeg(300, caps);
            Assert.Equal(image, got); Assert.True(polls >= 3);
            Assert.Contains("<pwg:InputSource>Platen</pwg:InputSource>", postedXml); Assert.Contains("<scan:XResolution>300</scan:XResolution>", postedXml);
            Assert.Contains("<pwg:Width>2480</pwg:Width>", postedXml); Assert.Contains("image/jpeg", postedXml);
            Assert.True(deleted);
        }
        finally { srv.Stop(); srv.Close(); }
    }

    [Fact]
    public void BusyScannerGivesUnderstandableError()
    {
        int port; using (var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
        using var srv = new HttpListener(); srv.Prefixes.Add($"http://127.0.0.1:{port}/"); srv.Start();
        Task.Run(() => { while (srv.IsListening) { try { var c = srv.GetContext(); c.Response.StatusCode = 503; c.Response.Close(); } catch { break; } } });
        try { using var cl = new EsclClient($"127.0.0.1:{port}"); var e = Assert.Throws<InvalidOperationException>(() => cl.ScanJpeg(300, new EsclCapabilities())); Assert.Contains("beschäftigt", e.Message); }
        finally { srv.Stop(); srv.Close(); }
    }

    [Fact]
    public void MdnsPacketParsingAndCombining()
    {
        // Antwortpaket wie von einem AirScan-Gerät: PTR + SRV + TXT + A (mit Namenskompression nicht nötig)
        var ms = new MemoryStream();
        void U16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        void Name(string n) { foreach (var l in n.Split('.')) { var b = Encoding.UTF8.GetBytes(l); ms.WriteByte((byte)b.Length); ms.Write(b, 0, b.Length); } ms.WriteByte(0); }
        void Rec(string name, int type, byte[] data) { Name(name); U16(type); U16(1); U16(0); U16(120); U16(data.Length); ms.Write(data, 0, data.Length); }
        byte[] Sub(Action<MemoryStream> f) { var tmp = new MemoryStream(); var keep = ms; ms = tmp; f(tmp); ms = keep; return tmp.ToArray(); }
        U16(0); U16(0x8400); U16(0); U16(4); U16(0); U16(0);
        var inst = "Mock Scanner._uscan._tcp.local";
        Rec("_uscan._tcp.local", 12, Sub(m => Name(inst)));
        Rec(inst, 33, Sub(m => { U16(0); U16(0); U16(8080); Name("mockhost.local"); }));
        Rec(inst, 16, Sub(m => { foreach (var kv in new[] { "txtvers=1", "rs=eSCL", "ty=Mock Scan 3000" }) { var b = Encoding.UTF8.GetBytes(kv); m.WriteByte((byte)b.Length); m.Write(b, 0, b.Length); } }));
        Rec("mockhost.local", 1, new byte[] { 192, 168, 1, 77 });
        var devs = MdnsDiscovery.Combine(MdnsDiscovery.ParsePacket(ms.ToArray()));
        var d = Assert.Single(devs);
        Assert.Equal("192.168.1.77", d.Host); Assert.Equal(8080, d.Port); Assert.False(d.Tls); Assert.Equal("Mock Scan 3000", d.Model);
        Assert.Equal("http://192.168.1.77:8080/eSCL", d.BaseUrl);
        Assert.Empty(MdnsDiscovery.ParsePacket(new byte[] { 1, 2, 3 }));                                  // Müll → keine Exception
        Assert.True(MdnsDiscovery.BuildQuery("_uscan._tcp.local").Length > 12);
    }
}
