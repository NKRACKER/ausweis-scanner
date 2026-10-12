using System;
using System.IO;

namespace AusweisScanner.App;

/// <summary>Datenordner: bevorzugt neben der .exe (portabel, z. B. USB-Stick); ist der nicht beschreibbar (Netzlaufwerk/Programmordner), dann %LOCALAPPDATA%.</summary>
static class Paths
{
    static string? _data;
    public static string ExeDir => AppDomain.CurrentDomain.BaseDirectory;

    public static string DataDir
    {
        get
        {
            if (_data != null) return _data;
            var portable = Path.Combine(ExeDir, "Ausweis-Scanner-Daten");
            if (CanWrite(portable)) return _data = portable;
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ausweis-Scanner");
            Directory.CreateDirectory(local);
            return _data = local;
        }
    }

    public static string SettingsFile => Path.Combine(DataDir, "einstellungen.json");
    public static string ProfilesDir => Path.Combine(DataDir, "profile");

    static bool CanWrite(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var t = Path.Combine(dir, ".schreibtest");
            File.WriteAllText(t, "x"); File.Delete(t); return true;
        }
        catch { return false; }
    }

    public static string DefaultOutputDir()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(string.IsNullOrEmpty(docs) ? ExeDir : docs, "Schuelerausweise");
    }
}
