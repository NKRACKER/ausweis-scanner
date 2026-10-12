using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AusweisScanner.Core;

namespace AusweisScanner.App;

sealed class ScanRequest
{
    public int Dpi = 300;
    /// <summary>Treiber-/Windows-eigenen Scan-Dialog zeigen (Vorschau, Bereich, Helligkeit …) statt stillem Schnellscan.</summary>
    public bool DriverDialog;
    public CancellationToken Cancel;
}

/// <summary>Ein Scanner, egal über welches Verfahren er angesprochen wird. Scan() blockiert – immer aus einem Hintergrund-Thread aufrufen.</summary>
abstract class ScanDevice
{
    public string Id = "", Name = "", Backend = "";
    public abstract BitmapCodec.Loaded Scan(ScanRequest rq);
    public override string ToString() => Name + "   [" + Backend + "]";
}

static class Sta
{
    public static T Run<T>(Func<T> f)
    {
        T result = default!; Exception? err = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception e) { err = e; } });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (err != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(err).Throw();
        return result;
    }
}

static class ScanBackends
{
    public static List<ScanDevice> EnumerateAll(Settings s, List<string> notes, bool network)
    {
        var all = new List<ScanDevice>();
        try { all.AddRange(Wia.Enumerate(notes)); } catch (Exception e) { notes.Add("WIA: " + e.Message); }
        try { all.AddRange(TwainBackend.Enumerate(notes)); } catch (Exception e) { notes.Add("TWAIN: " + e.Message); }
        if (network || s.EsclEnabled)
        {
            try { all.AddRange(EsclBackend.Enumerate(s, notes)); } catch (Exception e) { notes.Add("Netzwerkscanner: " + e.Message); }
        }
        return all;
    }

    public static string Friendly(Exception e)
    {
        if (e is COMException ce)
        {
            switch ((uint)ce.HResult)
            {
                case 0x80210001: return "Unbekannter Scannerfehler. Scanner aus- und wieder einschalten und erneut versuchen.";
                case 0x80210002: case 0x80210003: case 0x80210004: return "Der Scanner meldet ein Papierproblem (Einzug statt Glasplatte?). Bitte Ausweise auf die Glasplatte legen.";
                case 0x80210005: case 0x80210015: return "Der Scanner ist nicht erreichbar. Eingeschaltet und verbunden?";
                case 0x80210006: return "Der Scanner ist gerade beschäftigt. Kurz warten und erneut versuchen.";
                case 0x80210016: return "Der Scannerdeckel ist offen (bei Geräten mit Sensor). Deckel schließen oder Treiber-Dialog verwenden.";
                case 0x80210017: return "Die Scanner-Lampe ist aus oder noch nicht aufgewärmt. Kurz warten und erneut versuchen.";
                case 0x80210064: return "Der Scan wurde abgebrochen.";
                case 0x80210009: return "Das Scannen wurde am Gerät unterbrochen.";
            }
        }
        return e.Message;
    }
}

// ============================================================ WIA ============================================================
sealed class WiaDevice : ScanDevice
{
    const string FmtBmp = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}", FmtPng = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";

    public override BitmapCodec.Loaded Scan(ScanRequest rq) => Sta.Run(() => ScanSta(rq));

    BitmapCodec.Loaded ScanSta(ScanRequest rq)
    {
        dynamic? file = null;
        if (rq.DriverDialog)
        {
            // Windows-/Treiber-Dialog (Gerät wählt Windows: bei mehreren Scannern erscheint eine Auswahl)
            dynamic dlg = Activator.CreateInstance(Type.GetTypeFromProgID("WIA.CommonDialog")!)!;
            file = dlg.ShowAcquireImage(1, 1, 0, FmtBmp, false, true, false);
            if (file == null) throw new OperationCanceledException("Scan abgebrochen.");
        }
        else
        {
            dynamic mgr = Activator.CreateInstance(Type.GetTypeFromProgID("WIA.DeviceManager")!)!;
            dynamic? dev = null;
            foreach (dynamic info in mgr.DeviceInfos) if ((string)info.DeviceID == Id) { dev = info.Connect(); break; }
            if (dev == null) throw new InvalidOperationException("Der Scanner wurde nicht gefunden (ausgeschaltet oder abgesteckt?).");

            SetProp(dev.Properties, 3088, 1);                           // Dokumentenhandling: Flachbett
            double bedW = 8268, bedH = 11693;                           // 1/1000 Zoll, Standard A4
            double w = GetNum(dev.Properties, 3074), h = GetNum(dev.Properties, 3075);
            if (w > 1000 && h > 1000) { bedW = w; bedH = h; }
            dynamic item = dev.Items[1];
            dynamic ip = item.Properties;
            SetProp(ip, 6146, 1);                                       // Intent: Farbe
            SetProp(ip, 4103, 3); SetProp(ip, 4104, 24);                // Datentyp Farbe, 24 Bit
            SetProp(ip, 6147, rq.Dpi); SetProp(ip, 6148, rq.Dpi);       // Auflösung
            SetProp(ip, 6149, 0); SetProp(ip, 6150, 0);                 // Start oben links
            SetProp(ip, 6151, (int)(bedW / 1000.0 * rq.Dpi)); SetProp(ip, 6152, (int)(bedH / 1000.0 * rq.Dpi));  // ganze Glasplatte
            try { file = item.Transfer(FmtPng); }
            catch (COMException) { file = item.Transfer(FmtBmp); }       // manche Treiber können nur BMP
        }
        byte[] bytes = (byte[])file.FileData.BinaryData;
        return BitmapCodec.FromBytes(bytes);
    }

    static void SetProp(dynamic props, int id, object val)
    {
        try { foreach (dynamic p in props) if ((int)p.PropertyID == id) { p.Value = val; return; } }
        catch { /* Treiber unterstützt die Eigenschaft nicht → Treiber-Standard */ }
    }

    static double GetNum(dynamic props, int id)
    {
        try { foreach (dynamic p in props) if ((int)p.PropertyID == id) return Convert.ToDouble((object)p.Value); } catch { }
        return 0;
    }
}

static class Wia
{
    public static IEnumerable<ScanDevice> Enumerate(List<string> notes)
    {
        var res = new List<ScanDevice>();
        var t = Type.GetTypeFromProgID("WIA.DeviceManager");
        if (t == null) { notes.Add("WIA: Windows-Bildkomponente fehlt."); return res; }
        dynamic mgr = Activator.CreateInstance(t)!;
        foreach (dynamic info in mgr.DeviceInfos)
        {
            if ((int)info.Type != 1) continue;   // 1 = Scanner
            string name = "Scanner";
            try { foreach (dynamic p in info.Properties) if ((string)p.Name == "Name") name = (string)p.Value; } catch { }
            res.Add(new WiaDevice { Id = (string)info.DeviceID, Name = name, Backend = "WIA" });
        }
        return res;
    }
}

// ============================================================ eSCL / AirScan ============================================================
sealed class EsclDeviceEntry : ScanDevice
{
    public string Url = "";
    public override BitmapCodec.Loaded Scan(ScanRequest rq)
    {
        var host = new Uri(Url).Host;
        // Selbstsignierte Zertifikate sind bei Netzwerkscannern üblich – akzeptiert wird NUR für genau dieses Gerät.
        ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None || (sender is HttpWebRequest r && string.Equals(r.RequestUri.Host, host, StringComparison.OrdinalIgnoreCase));
        using var client = new EsclClient(Url);
        var jpeg = client.ScanJpeg(rq.Dpi, null, rq.Cancel);
        return BitmapCodec.FromBytes(jpeg);
    }
}

static class EsclBackend
{
    public static IEnumerable<ScanDevice> Enumerate(Settings s, List<string> notes)
    {
        var res = new List<ScanDevice>();
        foreach (var d in MdnsDiscovery.Find())
            res.Add(new EsclDeviceEntry { Id = d.BaseUrl, Url = d.BaseUrl, Name = d.ToString(), Backend = "Netzwerk (AirScan)" });
        foreach (var h in s.EsclHosts)
        {
            try
            {
                var url = EsclClient.NormalizeBase(h);
                if (res.Any(r => ((EsclDeviceEntry)r).Url.Equals(url, StringComparison.OrdinalIgnoreCase))) continue;
                res.Add(new EsclDeviceEntry { Id = url, Url = url, Name = h, Backend = "Netzwerk (AirScan, manuell)" });
            }
            catch (Exception e) { notes.Add("Netzwerkadresse „" + h + "“: " + e.Message); }
        }
        if (res.Count == 0) notes.Add("Netzwerk: kein AirScan-Scanner gefunden (Geräte-IP unter Einstellungen eintragen).");
        return res;
    }
}

// ============================================================ TWAIN ============================================================
sealed class TwainDevice : ScanDevice
{
    public override BitmapCodec.Loaded Scan(ScanRequest rq) { var p = TwainDsm.Prepare(); if (p.Length > 0) throw new InvalidOperationException(p); return Run(rq); }

    BitmapCodec.Loaded Run(ScanRequest rq) => TwainBackend.RunOnLoopThread<BitmapCodec.Loaded>(TimeSpan.FromMinutes(5), (form, done, fail) =>
    {
        var twain = new NTwain.TwainSession(NTwain.Data.TWIdentity.CreateFromAssembly(NTwain.Data.DataGroups.Image, Assembly.GetEntryAssembly()!));
        BitmapCodec.Loaded? got = null; string? error = null;
        void Finish() { try { if (twain.IsSourceOpen) twain.CurrentSource.Close(); } catch { } try { if (twain.IsDsmOpen) twain.Close(); } catch { } if (got != null) done(got); else fail(new InvalidOperationException(error ?? "Der Scanner hat keine Daten geliefert (abgebrochen?).")); }
        twain.DataTransferred += (s, e) =>
        {
            try
            {
                using var st = e.GetNativeImageStream();
                if (st != null) { using var ms = new System.IO.MemoryStream(); st.CopyTo(ms); got = BitmapCodec.FromBytes(ms.ToArray()); }
            }
            catch (Exception ex) { error = ex.Message; }
        };
        twain.TransferError += (s, e) => error = "Übertragungsfehler vom Scanner.";
        twain.SourceDisabled += (s, e) => Finish();
        var rc = twain.Open(new NTwain.WindowsFormsMessageLoopHook(form.Handle));
        if (rc != NTwain.Data.ReturnCode.Success && !twain.IsDsmOpen) throw new InvalidOperationException(TwainBackend.NoDsmText);
        var src = twain.GetSources().FirstOrDefault(x => x.Name == Name) ?? throw new InvalidOperationException("TWAIN-Scanner nicht mehr vorhanden.");
        src.Open();
        try { src.Capabilities.ICapXferMech.SetValue(NTwain.Data.XferMech.Native); } catch { }
        try { src.Capabilities.ICapUnits.SetValue(NTwain.Data.Unit.Inches); } catch { }
        try { src.Capabilities.ICapPixelType.SetValue(NTwain.Data.PixelType.RGB); } catch { }
        try { src.Capabilities.ICapXResolution.SetValue(rq.Dpi); src.Capabilities.ICapYResolution.SetValue(rq.Dpi); } catch { }
        var mode = rq.DriverDialog ? NTwain.SourceEnableMode.ShowUI : NTwain.SourceEnableMode.NoUI;
        var enabled = src.Enable(mode, false, form.Handle);
        if (enabled != NTwain.Data.ReturnCode.Success) { error = "Der Scanner ließ sich nicht starten."; Finish(); }
    });
}

/// <summary>Stellt sicher, dass ein TWAIN-Datenquellen-Manager geladen ist: vorhandener (neben der .exe oder im System) hat Vorrang, sonst der eingebettete.</summary>
static class TwainDsm
{
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryW(string name);
    static bool _done;

    public static string Prepare()
    {
        if (_done) return "";
        try
        {
            if (LoadLibraryW("twaindsm.dll") != IntPtr.Zero) { _done = true; return ""; }   // exe-Ordner oder System
            var res = "native.twaindsm-" + (Environment.Is64BitProcess ? "x64" : "x86") + ".dll";
            using var st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(res);
            if (st == null) return "Eingebetteter TWAIN-Manager fehlt.";
            var path = System.IO.Path.Combine(Paths.DataDir, "twaindsm.dll");
            if (!System.IO.File.Exists(path) || new System.IO.FileInfo(path).Length != st.Length)
            { using var o = System.IO.File.Create(path); st.CopyTo(o); }
            if (LoadLibraryW(path) == IntPtr.Zero) return "TWAIN-Manager ließ sich nicht laden (Fehler " + Marshal.GetLastWin32Error() + ").";
            _done = true; return "";
        }
        catch (Exception e) { return "TWAIN-Manager nicht bereitstellbar: " + e.Message; }
    }
}

static class TwainBackend
{
    public const string NoDsmText = "Kein TWAIN-Scannertreiber gefunden bzw. nutzbar. Den Treiber des Scanner-Herstellers installieren (nötig für TWAIN) – oder mit WIA/Datei-Import arbeiten.";

    public static IEnumerable<ScanDevice> Enumerate(List<string> notes)
    {
        var res = new List<ScanDevice>();
        var prep = TwainDsm.Prepare(); if (prep.Length > 0) { notes.Add("TWAIN: " + prep); return res; }
        try
        {
            RunOnLoopThread<object>(TimeSpan.FromSeconds(20), (form, done, fail) =>
            {
                var twain = new NTwain.TwainSession(NTwain.Data.TWIdentity.CreateFromAssembly(NTwain.Data.DataGroups.Image, Assembly.GetEntryAssembly()!));
                twain.Open(new NTwain.WindowsFormsMessageLoopHook(form.Handle));
                if (!twain.IsDsmOpen) { notes.Add("TWAIN: " + NoDsmText); done(new object()); return; }
                foreach (var s in twain.GetSources()) res.Add(new TwainDevice { Id = s.Name, Name = s.Name, Backend = "TWAIN" });
                twain.Close(); done(new object());
            });
        }
        catch (DllNotFoundException) { notes.Add("TWAIN: " + NoDsmText); }
        catch (Exception e) { notes.Add("TWAIN: " + e.Message); }
        return res;
    }

    /// <summary>TWAIN braucht eine Fensternachrichten-Schleife: eigener STA-Thread mit unsichtbarem Fenster.</summary>
    public static T RunOnLoopThread<T>(TimeSpan timeout, Action<Form, Action<T>, Action<Exception>> body) where T : class
    {
        T? result = null; Exception? err = null;
        var t = new Thread(() =>
        {
            using var form = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-2000, -2000), Size = new System.Drawing.Size(1, 1) };
            var timer = new System.Windows.Forms.Timer { Interval = (int)timeout.TotalMilliseconds };
            timer.Tick += (_, __) => { err ??= new TimeoutException("Der Scanner hat nicht rechtzeitig geantwortet."); form.Close(); };
            form.Shown += (_, __) =>
            {
                timer.Start();
                try { body(form, r => { result = r; form.Close(); }, e => { err = e; form.Close(); }); }
                catch (Exception e) { err = e; form.Close(); }
            };
            Application.Run(form); timer.Dispose();
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (err != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(err).Throw();
        return result!;
    }
}
