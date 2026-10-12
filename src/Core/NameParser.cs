using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AusweisScanner.Core;

public sealed class ParsedName
{
    public string Last = "", First = "";
    public ParsedName() { }
    public ParsedName(string last, string first) { Last = last; First = first; }
    public override string ToString() => First.Length > 0 ? $"{Last}, {First}" : Last;
}

/// <summary>Zerlegt OCR-Rohtext in Nachname/Vorname und räumt typische OCR-/Ausweis-Eigenheiten auf.</summary>
public static class NameParser
{
    static readonly HashSet<string> Particles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "von", "van", "de", "der", "den", "di", "da", "du", "la", "le", "zu", "zur", "zum", "vom", "bin", "ibn", "al", "el", "dos", "das", "del", "della", "ter", "ten", "op", "te", "ben", "af", "av" };

    public static readonly string[] DefaultStopwords =
    {
        "schülerausweis", "schuelerausweis", "ausweis", "schüler", "schule", "gymnasium", "realschule", "gesamtschule", "grundschule", "hauptschule", "berufskolleg",
        "berufsschule", "oberschule", "mittelschule", "sekundarschule", "förderschule", "gültig", "gueltig", "bis", "klasse", "geburtsdatum", "geboren", "geb", "datum",
        "unterschrift", "stempel", "schuljahr", "ausgestellt", "nummer", "nr", "id", "student", "card", "school", "name", "vorname", "nachname", "stadt", "straße", "strasse",
        "mitglied", "bibliothek", "ticket", "verkehrsverbund", "jahr", "und", "der", "die", "das", "des", "im", "für",
    };

    static readonly Regex LabelPrefix = new Regex(@"^\s*(familienname|nachname|vornamen|vorname|rufname|surname|name)\s*[:;.\-]*\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Clean(string raw)
    {
        var s = raw.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        var sb = new StringBuilder();
        foreach (var tok in Regex.Split(s, @"\s+"))
        {
            if (tok.Length == 0) continue;
            var t = FixDigits(tok);
            var o = new StringBuilder();
            foreach (char c in t)
            {
                if (char.IsLetter(c) || c == '-' || c == '\'' || c == '’' || c == ',') o.Append(c == '’' ? '\'' : c);
                else if (c == '|' || c == '¦') { /* OCR-Rauschen */ }
                // alles andere (Ziffern, Punkte, Symbole) fällt weg
            }
            if (o.Length == 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(o);
        }
        return Regex.Replace(sb.ToString(), @"\s*,\s*", ", ").Trim(' ', ',', '-', '\'');
    }

    // Ziffern in überwiegend aus Buchstaben bestehenden Wörtern sind fast immer verlesene Buchstaben.
    static string FixDigits(string tok)
    {
        int letters = tok.Count(char.IsLetter), digits = tok.Count(char.IsDigit);
        if (digits == 0 || letters < 3 || letters < digits * 2) return tok;
        bool upper = tok.Where(char.IsLetter).All(char.IsUpper);
        var sb = new StringBuilder();
        foreach (char c in tok)
        {
            switch (c)
            {
                case '0': sb.Append(upper ? 'O' : 'o'); break;
                case '1': sb.Append(upper ? 'I' : 'l'); break;
                case '5': sb.Append(upper ? 'S' : 's'); break;
                case '8': sb.Append('B'); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    public static string StripLabels(string raw, IEnumerable<string>? strip = null)
    {
        var s = raw.Trim();
        if (strip != null)
            foreach (var p in strip)
                if (!string.IsNullOrWhiteSpace(p) && s.StartsWith(p.Trim(), StringComparison.OrdinalIgnoreCase)) s = s.Substring(p.Trim().Length).TrimStart(' ', ':', ';', '.');
        for (int i = 0; i < 3; i++) { var m = LabelPrefix.Match(s); if (!m.Success || m.Length == 0) break; s = s.Substring(m.Length); }
        return s;
    }

    public static ParsedName Parse(string raw, NameFormat fmt = NameFormat.Auto, IEnumerable<string>? strip = null)
    {
        var text = Clean(StripLabels(raw, strip));
        if (text.Length == 0) return new ParsedName();

        int comma = text.IndexOf(',');
        if (comma >= 0 && fmt != NameFormat.FirstLast && fmt != NameFormat.LastFirst)
        {
            var left = text.Substring(0, comma).Trim(); var right = text.Substring(comma + 1).Replace(',', ' ').Trim();
            if (left.Length > 0 && right.Length > 0) return Norm(left, right);
        }
        text = text.Replace(",", " ");
        var tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count == 1) return Norm(tokens[0], "");

        bool LastFirstOrder()
        {
            if (fmt == NameFormat.LastFirst) return true;
            if (fmt != NameFormat.Auto) return false;
            // "MUSTERMANN Max": führende Großbuchstaben-Wörter, danach gemischte Schreibweise
            bool IsAllCaps(string t) => t.Any(char.IsLetter) && t.Where(char.IsLetter).All(char.IsUpper);
            return IsAllCaps(tokens[0]) && !IsAllCaps(tokens[tokens.Count - 1]);
        }

        if (LastFirstOrder())
        {
            int n = 1;
            if (fmt == NameFormat.Auto) { while (n < tokens.Count - 1 && (IsCaps(tokens[n]) || Particles.Contains(tokens[n]))) n++; }
            return Norm(string.Join(" ", tokens.Take(n)), string.Join(" ", tokens.Skip(n)));
        }
        int i = tokens.Count - 1;
        while (i > 1 && Particles.Contains(tokens[i - 1])) i--;
        return Norm(string.Join(" ", tokens.Skip(i)), string.Join(" ", tokens.Take(i)));
    }

    static bool IsCaps(string t) => t.Any(char.IsLetter) && t.Where(char.IsLetter).All(char.IsUpper);

    static ParsedName Norm(string last, string first) => new ParsedName(TitleCase(last), TitleCase(first));

    /// <summary>MUSTERMANN → Mustermann, anna-maria → Anna-Maria. Gemischte Schreibweise (McDonald, DeLuca) bleibt unangetastet.</summary>
    public static string TitleCase(string s)
    {
        var words = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int w = 0; w < words.Length; w++)
        {
            var word = words[w];
            bool allUpper = word.Where(char.IsLetter).All(char.IsUpper), allLower = word.Where(char.IsLetter).All(char.IsLower);
            if (Particles.Contains(word) && (allLower || (allUpper && w > 0))) { words[w] = word.ToLowerInvariant(); continue; }
            if (!allUpper && !allLower) continue;
            var sb = new StringBuilder(); bool up = true;
            foreach (char c in word)
            {
                sb.Append(up ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                up = c == '-' || c == '\'';
            }
            words[w] = sb.ToString();
        }
        return string.Join(" ", words);
    }

    /// <summary>0..1: wie plausibel ist das ein echter Personenname?</summary>
    public static double Plausibility(ParsedName n, IEnumerable<string>? extraStopwords = null)
    {
        if (n.Last.Length == 0) return 0;
        var stop = new HashSet<string>(DefaultStopwords, StringComparer.OrdinalIgnoreCase);
        if (extraStopwords != null) foreach (var s in extraStopwords) stop.Add(s.Trim());
        var tokens = (n.Last + " " + n.First).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        double score = n.First.Length > 0 ? 0.7 : 0.35;
        foreach (var t in tokens)
        {
            var letters = t.Count(char.IsLetter);
            if (letters < 2) { score -= 0.2; continue; }
            if (letters > 24) score -= 0.3;
            if (letters < t.Length * 0.8) score -= 0.2;
            if (stop.Contains(t.Trim('-', '\''))) score *= 0.2;
            if (!Particles.Contains(t) && !char.IsUpper(t[0])) score -= 0.1;
        }
        if (tokens.Length > 5) score *= 0.5;
        if (tokens.Length >= 2 && tokens.Length <= 4) score += 0.15;
        return Math.Max(0, Math.Min(1, score));
    }
}
