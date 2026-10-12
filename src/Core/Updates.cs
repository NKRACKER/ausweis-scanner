using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AusweisScanner.Core;

public sealed class UpdateInfo
{
    public string Tag = "", PageUrl = "", Notes = "", AssetName = "", AssetUrl = "", SumsUrl = "";
    public Version Version = new Version(0, 0, 0);
    public long Size;
}

/// <summary>Reine Logik der Update-Prüfung (ohne Netzwerk): Versionsvergleich, Auswahl des passenden Programms im Release, Prüfsummen-Zeile.</summary>
public static class UpdateLogic
{
    public const string SumsFileName = "SHA256SUMS.txt";

    public static bool TryParseVersion(string? tag, out Version v)
    {
        v = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var m = Regex.Match(tag!.Trim(), @"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return false;
        v = new Version(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                        m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0);
        return true;
    }

    public static string AssetNameFor(bool is64BitProcess) => is64BitProcess ? "Ausweis-Scanner-x64.exe" : "Ausweis-Scanner-x86.exe";

    /// <summary>Wertet die Antwort von <c>/releases/latest</c> aus. Liefert null, wenn kein neueres, vollständiges Release vorliegt.</summary>
    public static UpdateInfo? FromReleaseJson(string json, Version current, bool is64BitProcess)
    {
        if (!(MiniJson.Parse(json) is IDictionary<string, object?> d)) return null;
        if (MiniJson.Bool(d, "draft") || MiniJson.Bool(d, "prerelease")) return null;
        var tag = MiniJson.Str(d, "tag_name");
        if (!TryParseVersion(tag, out var latest) || latest <= Normalize(current)) return null;
        var want = AssetNameFor(is64BitProcess);
        var info = new UpdateInfo { Tag = tag, Version = latest, PageUrl = MiniJson.Str(d, "html_url"), Notes = MiniJson.Str(d, "body") };
        foreach (var a in MiniJson.ObjList(d, "assets"))
        {
            var name = MiniJson.Str(a, "name"); var url = MiniJson.Str(a, "browser_download_url");
            if (name == want) { info.AssetName = name; info.AssetUrl = url; info.Size = (long)MiniJson.Num(a, "size"); }
            else if (name == SumsFileName) info.SumsUrl = url;
        }
        // Ohne Programm UND Prüfsumme wird nichts angeboten: ungeprüft wird nie installiert.
        return info.AssetUrl.Length > 0 && info.SumsUrl.Length > 0 && IsHttps(info.AssetUrl) && IsHttps(info.SumsUrl) ? info : null;
    }

    static bool IsHttps(string u) => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Assembly-Versionen haben vier Teile (0.1.0.0); verglichen wird Major.Minor.Build.</summary>
    public static Version Normalize(Version v) => new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>Format von <c>sha256sum</c>: "&lt;64 Hex&gt;  Dateiname" oder "&lt;hash&gt; *Dateiname".</summary>
    public static string? ExpectedSha256(string sumsText, string fileName)
    {
        foreach (var raw in sumsText.Replace("\r", "").Split('\n'))
        {
            var m = Regex.Match(raw.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (m.Success && string.Equals(m.Groups[2].Value.Trim(), fileName, StringComparison.Ordinal)) return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }
}
