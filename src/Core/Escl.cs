using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace AusweisScanner.Core;

public sealed class EsclCapabilities
{
    public int MaxWidth = 2550, MaxHeight = 3507;      // in 1/300 Zoll (A4 Standard, falls das Gerät nichts meldet)
    public List<int> Resolutions = new List<int>();
    public List<string> ColorModes = new List<string>();
    public bool HasPlaten = true;
    public string Model = "";
}

/// <summary>
/// eSCL/AirScan-Client (HTTP, treiberlos). Die meisten Netzwerk-/WLAN-Scanner und Multifunktionsgeräte der letzten ~10 Jahre
/// sprechen das (Apple AirScan, Mopria Scan). Wird ausschließlich auf ausdrücklichen Wunsch genutzt und nur gegen die gewählte Adresse.
/// </summary>
public sealed class EsclClient : IDisposable
{
    static readonly XNamespace Scan = "http://schemas.hp.com/imaging/escl/2011/05/03";
    static readonly XNamespace Pwg = "http://www.pwg.org/schemas/2010/12/sm";
    readonly HttpClient _http; readonly bool _ownHttp;
    public string BaseUrl { get; }

    /// <param name="hostOrUrl">"192.168.1.20", "192.168.1.20:8080", "http://host/eSCL" oder "https://host:443/eSCL"</param>
    public EsclClient(string hostOrUrl, HttpClient? http = null)
    {
        BaseUrl = NormalizeBase(hostOrUrl);
        _ownHttp = http == null; _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public static string NormalizeBase(string s)
    {
        s = s.Trim().TrimEnd('/');
        if (s.Length == 0) throw new ArgumentException("Adresse fehlt");
        if (!s.Contains("://")) s = "http://" + s;
        var u = new Uri(s);
        var path = u.AbsolutePath.TrimEnd('/');
        if (path.Length == 0) path = "/eSCL";
        return u.GetLeftPart(UriPartial.Authority) + path;
    }

    public EsclCapabilities GetCapabilities(CancellationToken ct = default)
    {
        var xml = Run(_http.GetStringAsync(BaseUrl + "/ScannerCapabilities"), ct);
        var doc = XDocument.Parse(xml);
        var caps = new EsclCapabilities();
        caps.Model = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "MakeAndModel")?.Value ?? "";
        var platen = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "PlatenInputCaps");
        caps.HasPlaten = platen != null;
        if (platen != null)
        {
            int Int(string n, int def) => int.TryParse(platen.Descendants().FirstOrDefault(e => e.Name.LocalName == n)?.Value, out var v) ? v : def;
            caps.MaxWidth = Int("MaxWidth", caps.MaxWidth); caps.MaxHeight = Int("MaxHeight", caps.MaxHeight);
            foreach (var e in platen.Descendants().Where(e => e.Name.LocalName == "XResolution"))
                if (int.TryParse(e.Value, out var r) && !caps.Resolutions.Contains(r)) caps.Resolutions.Add(r);
            foreach (var e in platen.Descendants().Where(e => e.Name.LocalName == "ColorMode")) if (!caps.ColorModes.Contains(e.Value)) caps.ColorModes.Add(e.Value);
        }
        return caps;
    }

    public static string BuildScanSettings(EsclCapabilities caps, int dpi, string colorMode = "RGB24")
    {
        var d = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Scan + "ScanSettings", new XAttribute(XNamespace.Xmlns + "scan", Scan), new XAttribute(XNamespace.Xmlns + "pwg", Pwg),
                new XElement(Pwg + "Version", "2.0"),
                new XElement(Pwg + "ScanRegions", new XElement(Pwg + "ScanRegion",
                    new XElement(Pwg + "ContentRegionUnits", "escl:ThreeHundredthsOfInches"),
                    new XElement(Pwg + "XOffset", 0), new XElement(Pwg + "YOffset", 0),
                    new XElement(Pwg + "Width", caps.MaxWidth), new XElement(Pwg + "Height", caps.MaxHeight))),
                new XElement(Pwg + "InputSource", "Platen"),
                new XElement(Scan + "ColorMode", colorMode),
                new XElement(Scan + "XResolution", dpi), new XElement(Scan + "YResolution", dpi),
                new XElement(Pwg + "DocumentFormat", "image/jpeg")));
        return d.Declaration + "\n" + d.Root;
    }

    public static int PickResolution(EsclCapabilities caps, int wanted)
        => caps.Resolutions.Count == 0 ? wanted : caps.Resolutions.OrderBy(r => Math.Abs(r - wanted)).First();

    /// <summary>Scannt die gesamte Auflagefläche und liefert die JPEG-Daten.</summary>
    public byte[] ScanJpeg(int dpi = 300, EsclCapabilities? caps = null, CancellationToken ct = default, int timeoutSeconds = 90)
    {
        caps ??= GetCapabilities(ct);
        string color = caps.ColorModes.Count == 0 || caps.ColorModes.Contains("RGB24") ? "RGB24" : caps.ColorModes[0];
        var body = new StringContent(BuildScanSettings(caps, PickResolution(caps, dpi), color), Encoding.UTF8, "text/xml");
        var resp = Run(_http.PostAsync(BaseUrl + "/ScanJobs", body), ct);
        if (resp.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException(resp.StatusCode == HttpStatusCode.ServiceUnavailable || resp.StatusCode == HttpStatusCode.Conflict
                ? "Der Scanner ist gerade beschäftigt oder nicht bereit (Deckel zu? Papierfach?)."
                : $"Scanner lehnte den Auftrag ab (HTTP {(int)resp.StatusCode}).");
        var loc = resp.Headers.Location ?? throw new InvalidOperationException("Scanner lieferte keine Auftrags-Adresse.");
        var jobUrl = loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(BaseUrl), loc).ToString();
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var r = Run(_http.GetAsync(jobUrl.TrimEnd('/') + "/NextDocument", HttpCompletionOption.ResponseContentRead), ct);
                if (r.StatusCode == HttpStatusCode.OK) return Run(r.Content.ReadAsByteArrayAsync(), ct);
                if (r.StatusCode != HttpStatusCode.ServiceUnavailable && r.StatusCode != HttpStatusCode.NotFound)
                    throw new InvalidOperationException($"Scan fehlgeschlagen (HTTP {(int)r.StatusCode}).");
                if (DateTime.UtcNow > until) throw new TimeoutException("Der Scanner hat nicht rechtzeitig geliefert.");
                Thread.Sleep(700);
            }
        }
        finally { try { Run(_http.DeleteAsync(jobUrl), CancellationToken.None); } catch { /* Job-Aufräumen ist best effort */ } }
    }

    static T Run<T>(System.Threading.Tasks.Task<T> t, CancellationToken ct)
    {
        try { t.Wait(ct); return t.Result; }
        catch (AggregateException e) { throw e.InnerException ?? e; }
    }

    public void Dispose() { if (_ownHttp) _http.Dispose(); }
}

public sealed class EsclDevice
{
    public string Name = "", Host = "", RootPath = "eSCL", Model = "";
    public int Port = 80; public bool Tls;
    public string BaseUrl => $"{(Tls ? "https" : "http")}://{Host}:{Port}/{RootPath.Trim('/')}";
    public override string ToString() => $"{(string.IsNullOrEmpty(Model) ? Name : Model)} ({Host})";
}

/// <summary>mDNS/Bonjour-Suche nach AirScan-Scannern (_uscan._tcp / _uscans._tcp) im lokalen Netz. Keine Verbindung ins Internet.</summary>
public static class MdnsDiscovery
{
    public sealed class Rec { public string Name = ""; public int Type; public string Target = ""; public int Port; public string Ip = ""; public Dictionary<string, string> Txt = new Dictionary<string, string>(); }

    public static List<EsclDevice> Find(int timeoutMs = 2500)
    {
        var all = new List<Rec>();
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = false };
            udp.Client.ReceiveTimeout = 400;
            var ep = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
            foreach (var svc in new[] { "_uscan._tcp.local", "_uscans._tcp.local" })
            { var q = BuildQuery(svc); udp.Send(q, q.Length, ep); }
            var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < end)
            {
                try { var from = new IPEndPoint(IPAddress.Any, 0); var pkt = udp.Receive(ref from); all.AddRange(ParsePacket(pkt)); }
                catch (SocketException) { /* Timeout → weiter warten */ }
            }
        }
        catch (Exception) { /* kein Netz/Firewall: einfach keine Treffer */ }
        return Combine(all);
    }

    public static List<EsclDevice> Combine(List<Rec> recs)
    {
        var res = new List<EsclDevice>();
        foreach (var srv in recs.Where(r => r.Type == 33))
        {
            var inst = srv.Name; bool tls = inst.IndexOf("_uscans._tcp", StringComparison.OrdinalIgnoreCase) >= 0;
            if (inst.IndexOf("_uscan", StringComparison.OrdinalIgnoreCase) < 0) continue;
            var ip = recs.FirstOrDefault(r => r.Type == 1 && string.Equals(r.Name, srv.Target, StringComparison.OrdinalIgnoreCase))?.Ip;
            if (string.IsNullOrEmpty(ip)) continue;
            var txt = recs.Where(r => r.Type == 16 && string.Equals(r.Name, inst, StringComparison.OrdinalIgnoreCase)).SelectMany(r => r.Txt).ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
            var dev = new EsclDevice
            {
                Name = inst.Split(new[] { "._" }, StringSplitOptions.None)[0], Host = ip!, Port = srv.Port == 0 ? 80 : srv.Port, Tls = tls,
                RootPath = txt.TryGetValue("rs", out var rs) && rs.Length > 0 ? rs : "eSCL", Model = txt.TryGetValue("ty", out var ty) ? ty : "",
            };
            if (!res.Any(d => d.Host == dev.Host && d.Port == dev.Port)) res.Add(dev);
        }
        return res;
    }

    public static byte[] BuildQuery(string name)
    {
        var ms = new System.IO.MemoryStream();
        ms.Write(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, 0, 12);       // ID 0, 1 Frage
        foreach (var label in name.Split('.')) { var b = Encoding.ASCII.GetBytes(label); ms.WriteByte((byte)b.Length); ms.Write(b, 0, b.Length); }
        ms.WriteByte(0); ms.Write(new byte[] { 0, 12, 0, 1 }, 0, 4);              // PTR, IN
        return ms.ToArray();
    }

    public static List<Rec> ParsePacket(byte[] p)
    {
        var res = new List<Rec>();
        try
        {
            if (p.Length < 12) return res;
            int qd = U16(p, 4), an = U16(p, 6), ns = U16(p, 8), ar = U16(p, 10), pos = 12;
            for (int i = 0; i < qd; i++) { ReadName(p, ref pos); pos += 4; }
            for (int i = 0; i < an + ns + ar; i++)
            {
                var name = ReadName(p, ref pos); int type = U16(p, pos); pos += 8; int len = U16(p, pos); pos += 2;
                int end = pos + len; var r = new Rec { Name = name, Type = type };
                if (type == 1 && len == 4) r.Ip = $"{p[pos]}.{p[pos + 1]}.{p[pos + 2]}.{p[pos + 3]}";
                else if (type == 12) { int q = pos; r.Target = ReadName(p, ref q); }
                else if (type == 33) { r.Port = U16(p, pos + 4); int q = pos + 6; r.Target = ReadName(p, ref q); }
                else if (type == 16)
                {
                    int q = pos;
                    while (q < end) { int l = p[q++]; var s = Encoding.UTF8.GetString(p, q, Math.Min(l, end - q)); q += l; int eq = s.IndexOf('='); if (eq > 0) r.Txt[s.Substring(0, eq)] = s.Substring(eq + 1); }
                }
                res.Add(r); pos = end;
            }
        }
        catch (IndexOutOfRangeException) { /* abgeschnittenes/defektes Paket: bisherige Records behalten */ }
        catch (ArgumentException) { }
        return res;
    }

    static int U16(byte[] p, int o) => (p[o] << 8) | p[o + 1];

    static string ReadName(byte[] p, ref int pos)
    {
        var parts = new List<string>(); int cur = pos; bool jumped = false; int guard = 0;
        while (guard++ < 64)
        {
            int l = p[cur];
            if (l == 0) { cur++; break; }
            if ((l & 0xC0) == 0xC0) { int ptr = ((l & 0x3F) << 8) | p[cur + 1]; if (!jumped) pos = cur + 2; jumped = true; cur = ptr; continue; }
            parts.Add(Encoding.UTF8.GetString(p, cur + 1, l)); cur += 1 + l;
        }
        if (!jumped) pos = cur;
        return string.Join(".", parts);
    }
}
