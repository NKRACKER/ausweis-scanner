using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace AusweisScanner.App;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Eingebettete DLLs (Core, NTwain) laden – VOR dem ersten Zugriff auf deren Typen.
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var name = new AssemblyName(e.Name).Name + ".dll";
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("dep." + name);
            if (s == null) return null;
            var b = new byte[s.Length]; s.Read(b, 0, b.Length); return Assembly.Load(b);
        };
        return Run(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--post-update") return PostUpdate(args);
            if (args.Length >= 2 && args[0] == "--ui-test")   // Entwickler-/CI-Modus: Oberfläche rendern, als Bild speichern, beenden
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args.Skip(2).ToArray(), args[1])); return 0;
            }
            if (args.Length > 0 && args[0].StartsWith("--")) return Cli.Run(args);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (_, e) => ShowCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowCrash(e.ExceptionObject as Exception);
            Application.Run(new MainForm(args.Length > 0 ? args : null));
            return 0;
        }
        catch (Exception e) { ShowCrash(e); return 1; }
    }

    /// <summary>Start nach einem Update: alte Datei entfernen, Hinweis zeigen. Mit --update-report (nur CI-Selbsttest) stattdessen Ergebnis in eine Datei schreiben.</summary>
    static int PostUpdate(string[] args)
    {
        int ri = Array.IndexOf(args, "--update-report");
        if (ri >= 0 && ri + 1 < args.Length)
        {
            try { return PostUpdateReport(args[ri + 1]); }
            catch (Exception e) { File.WriteAllText(args[ri + 1], "FEHLER " + e.GetType().Name + ": " + e.Message); return 1; }
        }
        Updater.CleanupOld();
        bool moved = args.Contains("--moved");
        var msg = $"✓ Aktualisiert auf Version {Updater.CurrentText}." + (moved ? " (Der Ordner der alten Datei ist schreibgeschützt – die neue Version liegt in: " + Path.GetDirectoryName(Application.ExecutablePath) + ")" : "");
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(null, null, msg)); return 0;
    }

    static int PostUpdateReport(string reportFile)
    {
        bool oldGone = Updater.CleanupOld();
        {
            var bytes = File.ReadAllBytes(Application.ExecutablePath); var marker = System.Text.Encoding.ASCII.GetBytes("UPDATE-MARKER");
            bool hasMarker = bytes.Length > marker.Length && bytes.Skip(bytes.Length - marker.Length).SequenceEqual(marker);
            File.WriteAllText(reportFile, $"ok marker={hasMarker} old_deleted={oldGone} version={Updater.CurrentText}");
            return 0;
        }
    }

    static void ShowCrash(Exception? e)
    {
        // Automatisierung/CI: kein modaler Dialog (würde ewig warten), Fehler stattdessen in eine Datei neben der .exe schreiben
        if (Environment.GetEnvironmentVariable("AUSWEIS_NOUI") == "1")
        {
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath)!, "crash-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".txt"), e?.ToString() ?? "unbekannt"); } catch { }
            Environment.Exit(99);
        }
        // Bewusst ohne Personendaten: nur Fehlertyp + Meldung.
        MessageBox.Show("Unerwarteter Fehler:\n\n" + (e == null ? "unbekannt" : e.GetType().Name + ": " + e.Message) +
            "\n\nDas Programm kann weiterlaufen; bei Wiederholung bitte „Diagnose“ im Hauptfenster kopieren.", "Ausweis-Scanner", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
