using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AusweisScanner.Core;

/// <summary>Programmeinstellungen. Enthalten bewusst KEINE Personendaten (nur Optionen, Ordner, Geräte-IDs).</summary>
public sealed class Settings
{
    public string OutputDir = "";
    public string Format = "jpg";               // jpg | pdf | png
    public int JpegQuality = 92;
    public int Dpi = 300;
    public string Template = "{Nachname}_{Vorname}";
    public bool TransliterateUmlauts = false;
    public string ProfileName = "Automatisch";
    public string LastDevice = "";
    public bool CheckUpdates = true;            // beim Start bei GitHub nachsehen, ob es eine neue Version gibt (ohne personenbezogene Daten)
    public bool UseDriverDialog = false;        // Hersteller-Dialog (Vorschau/Bereich) statt Schnellscan
    public bool EsclEnabled = false;            // Netzwerkscanner-Suche nur auf ausdrücklichen Wunsch
    public List<string> EsclHosts = new List<string>();
    public string WatchFolder = "";
    public double EdgeInsetMm = 0.6;
    /// <summary>Bekannte Kartengröße: "id1" (Scheckkarte 85,6 × 54 mm, Standard) | "custom" (eigene Maße) | "auto" (keine Vorgabe). Dient dem exakten Einpassen des Rahmens.</summary>
    public string CardSizeMode = "id1";
    public double CardWidthMm = CardSizes.Id1Width, CardHeightMm = CardSizes.Id1Height;

    public IDictionary<string, object?> ToDict() => new Dictionary<string, object?>
    {
        ["output_dir"] = OutputDir, ["format"] = Format, ["jpeg_quality"] = JpegQuality, ["dpi"] = Dpi, ["template"] = Template,
        ["transliterate_umlauts"] = TransliterateUmlauts, ["profile"] = ProfileName,
        ["last_device"] = LastDevice, ["check_updates"] = CheckUpdates, ["use_driver_dialog"] = UseDriverDialog, ["escl_enabled"] = EsclEnabled, ["escl_hosts"] = EsclHosts.Cast<object?>().ToList(),
        ["watch_folder"] = WatchFolder, ["edge_inset_mm"] = EdgeInsetMm, ["card_size_mode"] = CardSizeMode, ["card_width_mm"] = CardWidthMm, ["card_height_mm"] = CardHeightMm,
    };

    public static Settings FromDict(IDictionary<string, object?> d)
    {
        var s = new Settings();
        s.OutputDir = MiniJson.Str(d, "output_dir"); s.Format = MiniJson.Str(d, "format", "jpg");
        s.JpegQuality = (int)MiniJson.Num(d, "jpeg_quality", 92); s.Dpi = (int)MiniJson.Num(d, "dpi", 300);
        s.Template = MiniJson.Str(d, "template", "{Nachname}_{Vorname}");
        s.TransliterateUmlauts = MiniJson.Bool(d, "transliterate_umlauts");
        s.ProfileName = MiniJson.Str(d, "profile", "Automatisch"); s.LastDevice = MiniJson.Str(d, "last_device");
        s.CheckUpdates = MiniJson.Bool(d, "check_updates", true); s.UseDriverDialog = MiniJson.Bool(d, "use_driver_dialog"); s.EsclEnabled = MiniJson.Bool(d, "escl_enabled"); s.EsclHosts = MiniJson.StrList(d, "escl_hosts");
        s.WatchFolder = MiniJson.Str(d, "watch_folder"); s.EdgeInsetMm = MiniJson.Num(d, "edge_inset_mm", 0.6);
        s.CardSizeMode = MiniJson.Str(d, "card_size_mode", "id1"); s.CardWidthMm = MiniJson.Num(d, "card_width_mm", CardSizes.Id1Width); s.CardHeightMm = MiniJson.Num(d, "card_height_mm", CardSizes.Id1Height);
        if (s.CardSizeMode != "id1" && s.CardSizeMode != "custom" && s.CardSizeMode != "auto") s.CardSizeMode = "id1";
        if (s.CardWidthMm < 30 || s.CardWidthMm > 160 || s.CardHeightMm < 30 || s.CardHeightMm > 160) { s.CardWidthMm = CardSizes.Id1Width; s.CardHeightMm = CardSizes.Id1Height; }
        if (s.Dpi < 75 || s.Dpi > 1200) s.Dpi = 300;
        if (s.JpegQuality < 30 || s.JpegQuality > 100) s.JpegQuality = 92;
        return s;
    }

    public static Settings Load(string path)
    {
        try { return File.Exists(path) ? FromDict((IDictionary<string, object?>)MiniJson.Parse(File.ReadAllText(path))!) : new Settings(); }
        catch { return new Settings(); }   // defekte Datei: mit Standardwerten weiterarbeiten
    }

    public void Save(string path) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, MiniJson.Write(ToDict())); }
}
