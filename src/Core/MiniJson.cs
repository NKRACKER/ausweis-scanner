using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AusweisScanner.Core;

/// <summary>
/// Kleiner JSON-Leser/-Schreiber für Einstellungen und Profile. Absichtlich ohne Abhängigkeit
/// (System.Text.Json würde unter .NET Framework Zusatz-DLLs mitziehen; die .exe soll eine Datei bleiben).
/// Objekte = Dictionary&lt;string, object?&gt;, Listen = List&lt;object?&gt;, Zahlen = double, dazu string/bool/null.
/// </summary>
public static class MiniJson
{
    public static object? Parse(string s)
    {
        int i = 0; var v = ReadValue(s, ref i); SkipWs(s, ref i);
        if (i != s.Length) throw new FormatException($"Unerwartetes Zeichen an Position {i}");
        return v;
    }

    public static string Write(object? v, bool indent = true)
    {
        var sb = new StringBuilder(); WriteValue(sb, v, indent ? 0 : -1); return sb.ToString();
    }

    static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

    static object? ReadValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length) throw new FormatException("Unerwartetes Ende");
        char c = s[i];
        if (c == '{')
        {
            var d = new Dictionary<string, object?>(); i++; SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i); var k = ReadString(s, ref i); SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException($"':' erwartet an Position {i}");
                i++; d[k] = ReadValue(s, ref i); SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw new FormatException($"',' oder '}}' erwartet an Position {i}");
            }
        }
        if (c == '[')
        {
            var l = new List<object?>(); i++; SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ReadValue(s, ref i)); SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return l; }
                throw new FormatException($"',' oder ']' erwartet an Position {i}");
            }
        }
        if (c == '"') return ReadString(s, ref i);
        if (Match(s, i, "true")) { i += 4; return true; }
        if (Match(s, i, "false")) { i += 5; return false; }
        if (Match(s, i, "null")) { i += 4; return null; }
        int st = i;
        while (i < s.Length && ("+-0123456789.eE".IndexOf(s[i]) >= 0)) i++;
        if (st == i) throw new FormatException($"Unerwartetes Zeichen '{c}' an Position {i}");
        return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
    }

    static bool Match(string s, int i, string w) => string.CompareOrdinal(s, i, w, 0, w.Length) == 0;

    static string ReadString(string s, ref int i)
    {
        if (i >= s.Length || s[i] != '"') throw new FormatException($"'\"' erwartet an Position {i}");
        i++; var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= s.Length) break;
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    if (i + 4 > s.Length) throw new FormatException("Ungültiges \\u");
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                default: sb.Append(e); break; // " \ /
            }
        }
        throw new FormatException("String nicht beendet");
    }

    static void WriteValue(StringBuilder sb, object? v, int lvl)
    {
        switch (v)
        {
            case null: sb.Append("null"); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case string str: WriteString(sb, str); break;
            case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
            case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
            case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
            case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
            case IDictionary<string, object?> dict:
                if (dict.Count == 0) { sb.Append("{}"); break; }
                sb.Append('{'); bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(','); first = false; NewLine(sb, lvl + 1);
                    WriteString(sb, kv.Key); sb.Append(lvl >= 0 ? ": " : ":"); WriteValue(sb, kv.Value, lvl < 0 ? -1 : lvl + 1);
                }
                NewLine(sb, lvl); sb.Append('}'); break;
            case System.Collections.IEnumerable list:
                var items = new List<object?>(); foreach (var o in list) items.Add(o);
                if (items.Count == 0) { sb.Append("[]"); break; }
                bool simple = true; foreach (var o in items) if (o is IDictionary<string, object?> || o is System.Collections.IEnumerable && !(o is string)) simple = false;
                sb.Append('[');
                for (int k = 0; k < items.Count; k++)
                {
                    if (k > 0) sb.Append(simple ? ", " : ",");
                    if (!simple) NewLine(sb, lvl + 1);
                    WriteValue(sb, items[k], lvl < 0 ? -1 : lvl + 1);
                }
                if (!simple) NewLine(sb, lvl);
                sb.Append(']'); break;
            default: throw new ArgumentException("Nicht serialisierbar: " + v.GetType().Name);
        }
    }

    static void NewLine(StringBuilder sb, int lvl)
    {
        if (lvl < 0) return;
        sb.Append('\n').Append(' ', lvl * 2);
    }

    static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                    break;
            }
        sb.Append('"');
    }

    // --- Lese-Helfer für tolerantes Auslesen (fehlende/falsche Felder -> Default) ---
    public static string Str(IDictionary<string, object?> d, string key, string def = "") => d.TryGetValue(key, out var v) && v is string s ? s : def;
    public static double Num(IDictionary<string, object?> d, string key, double def = 0) => d.TryGetValue(key, out var v) && v is double n ? n : def;
    public static bool Bool(IDictionary<string, object?> d, string key, bool def = false) => d.TryGetValue(key, out var v) && v is bool b ? b : def;
    public static List<string> StrList(IDictionary<string, object?> d, string key)
    {
        var r = new List<string>();
        if (d.TryGetValue(key, out var v) && v is List<object?> l) foreach (var o in l) if (o is string s) r.Add(s);
        return r;
    }
    public static IDictionary<string, object?>? Obj(IDictionary<string, object?> d, string key) => d.TryGetValue(key, out var v) ? v as IDictionary<string, object?> : null;
    public static List<IDictionary<string, object?>> ObjList(IDictionary<string, object?> d, string key)
    {
        var r = new List<IDictionary<string, object?>>();
        if (d.TryGetValue(key, out var v) && v is List<object?> l) foreach (var o in l) if (o is IDictionary<string, object?> m) r.Add(m);
        return r;
    }
}
