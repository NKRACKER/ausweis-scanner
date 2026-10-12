using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AusweisScanner.Core;

namespace AusweisScanner.App;

static class Diagnostics
{
    /// <summary>Technischer Zustandsbericht – bewusst ohne Personendaten, damit er bedenkenlos weitergegeben werden kann.</summary>
    public static string Report(Settings s, bool includeNetworkScan = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ausweis-Scanner {typeof(Program).Assembly.GetName().Version}  ({(Environment.Is64BitProcess ? "64" : "32")}-Bit-Prozess)");
        sb.AppendLine($"Zeit: {DateTime.Now:yyyy-MM-dd HH:mm}   Windows: {Environment.OSVersion.Version}   .NET: {Environment.Version}");
        sb.AppendLine($"Admin: {IsAdmin()}   Datenordner: {Paths.DataDir}");
        sb.AppendLine();
        sb.AppendLine("[Texterkennung]");
        var langs = WinOcr.InstalledLanguages();
        sb.AppendLine(WinOcr.IsSupported ? (langs.Count == 0 ? "  Komponente da, aber keine Sprache installiert" : "  Sprachen: " + string.Join(", ", langs)) : "  nicht verfügbar (Windows 10+ nötig)");
        var o = WinOcr.TryCreate(out var problem); sb.AppendLine("  Status: " + (o != null ? o.Description : problem));
        sb.AppendLine();
        sb.AppendLine("[Scanner]");
        var notes = new List<string>();
        var devs = ScanBackends.EnumerateAll(s, notes, includeNetworkScan);
        if (devs.Count == 0) sb.AppendLine("  keine gefunden");
        foreach (var d in devs) sb.AppendLine("  - " + d);
        foreach (var n in notes) sb.AppendLine("  Hinweis: " + n);
        sb.AppendLine();
        sb.AppendLine("[TWAIN-Dateien]");
        foreach (var f in new[] { @"C:\Windows\twain_32.dll", @"C:\Windows\System32\twaindsm.dll", @"C:\Windows\SysWOW64\twaindsm.dll", System.IO.Path.Combine(Paths.ExeDir, "twaindsm.dll") })
            sb.AppendLine($"  {f}: {(System.IO.File.Exists(f) ? "vorhanden" : "fehlt")}");
        return sb.ToString();
    }

    static bool IsAdmin()
    {
        try { return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }
}
