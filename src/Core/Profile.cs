using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AusweisScanner.Core;

public enum NameFormat { Auto, LastCommaFirst, FirstLast, LastFirst }

/// <summary>Ein Textfeld auf dem Ausweis, relativ zur Karte (0..1, Karte im Querformat, Ursprung links oben).</summary>
public sealed class FieldRegion
{
    /// <summary>"name" = Vor- und Nachname im selben Feld, "last" = nur Nachname, "first" = nur Vorname.</summary>
    public string Role = "name";
    public double X, Y, W, H;
    /// <summary>Feste Vorsilben, die vor dem Parsen entfernt werden (z. B. "Name:").</summary>
    public List<string> Strip = new List<string>();
}

/// <summary>
/// Beschreibt, wie der Name auf einem Ausweis-Design zu finden ist. Neues Design = neue JSON-Datei im Ordner "profiles".
/// Ohne Felder (Profil „Automatisch“) wird nach Beschriftungen ("Name", "Vorname" …) gesucht, danach per Heuristik.
/// </summary>
public sealed class Profile
{
    public string Name = "Automatisch";
    public string Description = "";
    public string ValidFrom = "";
    /// <summary>Kartenmaß in mm (0 = beliebig). Wird zur Filterung beim Erkennen genutzt.</summary>
    public double CardWidthMm, CardHeightMm;
    /// <summary>Leserichtung des Textes relativ zur Querformat-Karte: landscape | portrait | any</summary>
    public string Orientation = "landscape";
    public NameFormat Format = NameFormat.Auto;
    public List<FieldRegion> Fields = new List<FieldRegion>();
    public List<string> LastLabels = new List<string> { "Nachname", "Familienname", "Name", "Surname" };
    public List<string> FirstLabels = new List<string> { "Vorname", "Vornamen", "Rufname", "Given" };
    /// <summary>Zusätzliche Wörter, die NIE Teil eines Namens sind (Schulname, "Schülerausweis" …).</summary>
    public List<string> Stopwords = new List<string>();
    public bool UseAnchors = true, UseHeuristic = true;
    /// <summary>Datei, aus der das Design geladen wurde (nicht gespeichert) – zum Bearbeiten/Umbenennen.</summary>
    public string FilePath = "";

    public override string ToString() => Name;   // Anzeige in Auswahllisten

    public static Profile Auto() => new Profile { Name = "Automatisch", Description = "Sucht Beschriftungen wie „Name“/„Vorname“, sonst die größte namensähnliche Zeile." };

    public IDictionary<string, object?> ToDict()
    {
        var d = new Dictionary<string, object?>
        {
            ["name"] = Name, ["description"] = Description, ["valid_from"] = ValidFrom,
            ["card_mm"] = new List<object?> { CardWidthMm, CardHeightMm },
            ["orientation"] = Orientation, ["name_format"] = Format.ToString(),
            ["use_anchors"] = UseAnchors, ["use_heuristic"] = UseHeuristic,
            ["last_labels"] = LastLabels.Cast<object?>().ToList(), ["first_labels"] = FirstLabels.Cast<object?>().ToList(),
            ["stopwords"] = Stopwords.Cast<object?>().ToList(),
            ["fields"] = Fields.Select(f => (object?)new Dictionary<string, object?>
            { ["role"] = f.Role, ["x"] = f.X, ["y"] = f.Y, ["w"] = f.W, ["h"] = f.H, ["strip"] = f.Strip.Cast<object?>().ToList() }).ToList(),
        };
        return d;
    }

    public static Profile FromDict(IDictionary<string, object?> d)
    {
        var p = new Profile
        {
            Name = MiniJson.Str(d, "name", "Profil"), Description = MiniJson.Str(d, "description"), ValidFrom = MiniJson.Str(d, "valid_from"),
            Orientation = MiniJson.Str(d, "orientation", "landscape"),
            UseAnchors = MiniJson.Bool(d, "use_anchors", true), UseHeuristic = MiniJson.Bool(d, "use_heuristic", true),
        };
        if (d.TryGetValue("card_mm", out var cm) && cm is List<object?> l && l.Count >= 2 && l[0] is double w && l[1] is double h) { p.CardWidthMm = w; p.CardHeightMm = h; }
        if (Enum.TryParse<NameFormat>(MiniJson.Str(d, "name_format", "Auto"), true, out var nf)) p.Format = nf;
        var ll = MiniJson.StrList(d, "last_labels"); if (ll.Count > 0) p.LastLabels = ll;
        var fl = MiniJson.StrList(d, "first_labels"); if (fl.Count > 0) p.FirstLabels = fl;
        p.Stopwords = MiniJson.StrList(d, "stopwords");
        foreach (var f in MiniJson.ObjList(d, "fields"))
            p.Fields.Add(new FieldRegion
            {
                Role = MiniJson.Str(f, "role", "name").ToLowerInvariant(),
                X = MiniJson.Num(f, "x"), Y = MiniJson.Num(f, "y"), W = MiniJson.Num(f, "w"), H = MiniJson.Num(f, "h"),
                Strip = MiniJson.StrList(f, "strip"),
            });
        return p;
    }

    public string ToJson() => MiniJson.Write(ToDict());
    public static Profile FromJson(string json) => FromDict((IDictionary<string, object?>)(MiniJson.Parse(json) ?? throw new FormatException("Leeres Profil")));
}

/// <summary>Profile aus einem Ordner (*.json) laden/speichern. Defekte Dateien werden gemeldet, nicht verschluckt.</summary>
public static class ProfileStore
{
    public static List<Profile> Load(string dir, List<string>? problems = null)
    {
        var list = new List<Profile> { Profile.Auto() };
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var p = Profile.FromJson(File.ReadAllText(f)); p.FilePath = f;
                list.RemoveAll(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                list.Add(p);
            }
            catch (Exception e) { problems?.Add($"{Path.GetFileName(f)}: {e.Message}"); }
        }
        return list;
    }

    public static string Save(string dir, Profile p)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FileNamer.Sanitize(p.Name, false).Replace(' ', '_') + ".json");
        File.WriteAllText(path, p.ToJson());
        return path;
    }
}
