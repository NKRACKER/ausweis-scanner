using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>
/// Windows-eigene Texterkennung (Windows.Media.Ocr, offline, ab Windows 10). Angesprochen per Reflection/dynamic,
/// damit das Projekt ohne Windows-SDK-Referenzen (und damit auf Linux) baut und die .exe eine einzelne Datei bleibt.
/// Sprache: Deutsch bevorzugt, sonst eine andere installierte lateinische Sprache (Englisch erkennt Umlaute meist, aber schlechter).
/// </summary>
sealed class WinOcr : IOcrEngine
{
    const string W = ", Windows, ContentType=WindowsRuntime";
    static readonly Type? OcrT = Type.GetType("Windows.Media.Ocr.OcrEngine" + W);
    static readonly Type? BmpT = Type.GetType("Windows.Graphics.Imaging.SoftwareBitmap" + W);
    static readonly Type? FmtT = Type.GetType("Windows.Graphics.Imaging.BitmapPixelFormat" + W);
    static readonly Type? AsyncInfoT = Type.GetType("Windows.Foundation.IAsyncInfo" + W);
    static readonly Type? AsyncOpOpenT = Type.GetType("Windows.Foundation.IAsyncOperation`1" + W);
    static readonly Type? OcrResultT = Type.GetType("Windows.Media.Ocr.OcrResult" + W);
    static readonly Type? IBufT = Type.GetType("Windows.Storage.Streams.IBuffer" + W);
    static readonly Type? ExtT = Type.GetType("System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions, System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");

    readonly object _engine; readonly MethodInfo _asBuffer, _copyFromBuffer, _recognize, _getResults; readonly PropertyInfo _status; readonly int _maxDim;
    public string Description { get; }
    public string LanguageTag { get; }

    WinOcr(object engine, string tag, string desc)
    {
        _engine = engine; LanguageTag = tag; Description = desc;
        // Status/GetResults liegen auf den WinRT-Schnittstellen, nicht auf dem COM-Wrapper → über die Interface-Typen aufrufen (dynamic scheitert hier).
        var asyncOp = AsyncOpOpenT!.MakeGenericType(OcrResultT!);
        _status = AsyncInfoT!.GetProperty("Status")!; _getResults = asyncOp.GetMethod("GetResults")!;
        _recognize = OcrT!.GetMethod("RecognizeAsync")!;
        _asBuffer = ExtT!.GetMethod("AsBuffer", new[] { typeof(byte[]) })!;
        _copyFromBuffer = BmpT!.GetMethod("CreateCopyFromBuffer", new[] { IBufT!, FmtT!, typeof(int), typeof(int) })!;
        try { _maxDim = (int)OcrT!.GetProperty("MaxImageDimension")!.GetValue(null, null)!; } catch { _maxDim = 10000; }
    }

    public static bool IsSupported => OcrT != null && BmpT != null && FmtT != null && IBufT != null && ExtT != null && AsyncInfoT != null && AsyncOpOpenT != null && OcrResultT != null;

    public static List<string> InstalledLanguages()
    {
        var res = new List<string>();
        if (!IsSupported) return res;
        try { foreach (dynamic l in (System.Collections.IEnumerable)OcrT!.GetProperty("AvailableRecognizerLanguages")!.GetValue(null, null)!) res.Add((string)l.LanguageTag + " (" + (string)l.DisplayName + ")"); }
        catch { }
        return res;
    }

    /// <returns>Engine oder null; <paramref name="problem"/> erklärt warum nicht (Anzeige für den Nutzer).</returns>
    public static WinOcr? TryCreate(out string problem)
    {
        problem = "";
        if (!IsSupported) { problem = "Die Windows-Texterkennung gibt es erst ab Windows 10. Namen müssen von Hand eingetippt werden."; return null; }
        try
        {
            var langs = new List<(object lang, string tag)>();
            foreach (dynamic l in (System.Collections.IEnumerable)OcrT!.GetProperty("AvailableRecognizerLanguages")!.GetValue(null, null)!) langs.Add((l, (string)l.LanguageTag));
            if (langs.Count == 0)
            {
                problem = "Auf diesem PC ist kein Texterkennungs-Sprachpaket installiert. Namen müssen von Hand eingetippt werden. " +
                          "(Einstellungen → Zeit & Sprache → Sprache → Deutsch → Sprachoptionen → „Optische Zeichenerkennung“ hinzufügen – braucht Administratorrechte.)";
                return null;
            }
            var pick = langs.FirstOrDefault(x => x.tag.StartsWith("de", StringComparison.OrdinalIgnoreCase));
            if (pick.lang == null) pick = langs.FirstOrDefault(x => x.tag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (pick.lang == null) pick = langs[0];
            var eng = OcrT.GetMethod("TryCreateFromLanguage")!.Invoke(null, new[] { pick.lang });
            if (eng == null) { problem = "Die Windows-Texterkennung konnte nicht gestartet werden."; return null; }
            string note = pick.tag.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "" : " – ohne Deutsch-Paket sind Umlaute unsicherer";
            return new WinOcr(eng, pick.tag, "Windows-Texterkennung (" + pick.tag + ")" + note);
        }
        catch (Exception e) { problem = "Texterkennung nicht verfügbar: " + e.Message; return null; }
    }

    public OcrResult Recognize(Raster image)
    {
        double k = 1.0;
        if (Math.Max(image.Width, image.Height) > _maxDim) { k = (double)_maxDim / Math.Max(image.Width, image.Height); image = image.Resize((int)(image.Width * k), (int)(image.Height * k)); }
        var buf = _asBuffer.Invoke(null, new object[] { image.ToBgra() });
        var bmp = _copyFromBuffer.Invoke(null, new[] { buf, Enum.ToObject(FmtT!, 87 /* Bgra8 */), image.Width, image.Height })!;
        try
        {
            var op = _recognize.Invoke(_engine, new[] { bmp })!;
            var until = DateTime.UtcNow.AddSeconds(30);
            while (Convert.ToInt32(_status.GetValue(op, null)) == 0)   // 0 = Started
            {
                if (DateTime.UtcNow > until) throw new TimeoutException("Texterkennung hat nicht geantwortet.");
                Thread.Sleep(3);
            }
            if (Convert.ToInt32(_status.GetValue(op, null)) != 1) throw new InvalidOperationException("Texterkennung fehlgeschlagen.");   // 1 = Completed
            dynamic res = _getResults.Invoke(op, null)!;
            var o = new OcrResult();
            foreach (dynamic line in res.Lines)
            {
                var l = new OcrLine { Text = (string)line.Text };
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = 0, y1 = 0;
                foreach (dynamic w in line.Words)
                {
                    var r = w.BoundingRect; double x = (double)r.X / k, y = (double)r.Y / k, ww = (double)r.Width / k, hh = (double)r.Height / k;
                    l.Words.Add(new OcrWord { Text = (string)w.Text, Box = new RectD(x, y, ww, hh) });
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x + ww); y1 = Math.Max(y1, y + hh);
                }
                if (l.Words.Count > 0) l.Box = new RectD(x0, y0, x1 - x0, y1 - y0);
                o.Lines.Add(l);
            }
            return o;
        }
        finally { try { ((dynamic)bmp).Dispose(); } catch { } }
    }
}
