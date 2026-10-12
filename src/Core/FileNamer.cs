using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AusweisScanner.Core;

public sealed class NamingOptions
{
    /// <summary>Platzhalter: {Nachname} {Vorname} {Klasse} {Datum}</summary>
    public string Template = "{Nachname}_{Vorname}";
    public bool TransliterateUmlauts = false;
    public int MaxLength = 120;
}

public static class FileNamer
{
    static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    public static string BaseName(ParsedName n, NamingOptions? o = null, IDictionary<string, string>? extra = null)
    {
        o ??= new NamingOptions();
        string Get(string k) => extra != null && extra.TryGetValue(k, out var v) ? v : "";
        var s = o.Template
            .Replace("{Nachname}", Sanitize(n.Last, o.TransliterateUmlauts))
            .Replace("{Vorname}", Sanitize(n.First, o.TransliterateUmlauts))
            .Replace("{Klasse}", Sanitize(Get("Klasse"), o.TransliterateUmlauts))
            .Replace("{Datum}", Sanitize(Get("Datum"), false));
        s = Regex.Replace(s, @"([_\- ])\1+", "$1");       // doppelte Trenner durch leere Felder
        s = s.Trim('_', '-', ' ', '.');
        // Sanitize für die gesamte Vorlage (falls der Nutzer Sonderzeichen in die Vorlage schrieb)
        s = Sanitize(s, false);
        if (s.Length == 0) s = "Unbekannt";
        if (s.Length > o.MaxLength) s = s.Substring(0, o.MaxLength).TrimEnd('_', '-', ' ', '.');
        if (Reserved.Contains(s)) s = "_" + s;
        return s;
    }

    /// <summary>Entfernt verbotene Dateinamen-Zeichen; optional ä→ae usw.</summary>
    public static string Sanitize(string s, bool translit)
    {
        if (translit)
            s = s.Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("Ä", "Ae").Replace("Ö", "Oe").Replace("Ü", "Ue").Replace("ß", "ss");
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c < 32 || "\\/:*?\"<>|".IndexOf(c) >= 0) continue;
            sb.Append(c);
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim(' ', '.');
    }

    /// <summary>Liefert einen noch nicht vergebenen Dateinamen (…_2, …_3). Überschreibt nie.</summary>
    public static string Unique(string baseName, string ext, Func<string, bool> exists, ISet<string>? taken = null)
    {
        string name = baseName + ext; int n = 2;
        while ((taken != null && taken.Contains(name.ToLowerInvariant())) || exists(name)) name = baseName + "_" + n++ + ext;
        taken?.Add(name.ToLowerInvariant());
        return name;
    }
}
