using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>Kommandozeile für Automatisierung/Tests. Das Hauptprogramm ist die Oberfläche (ohne Argumente).</summary>
static class Cli
{
    const string Help = @"Ausweis-Scanner – Kommandozeile
  --process <Datei|Ordner> --out <Ordner> [--format jpg|pdf|png] [--profile <Name>] [--dpi N] [--report <Datei>]
        Bild/PDF(s) verarbeiten: Ausweise erkennen, Namen lesen, je Karte eine Datei speichern.
  --diag [<Datei>]            Technischen Zustandsbericht (Scanner, Texterkennung) ausgeben/speichern
  --selftest [--report <Datei>]   Schnelltest: Start ok, Texterkennung verfügbar?
  --make-testscan <Ordner> [--dark] [--umlauts] [--hard] [--seed N]   synthetischen Beispiel-Scan mit Fantasie-Ausweisen erzeugen
  --update-check [--force] [--report <Datei>]   Update-Prüfung (--force: so tun, als wäre eine alte Version installiert)
  --update-download-test [--report <Datei>]      neuestes Release laden und Prüfsumme kontrollieren (ohne zu installieren)
  --update-selftest <Ordner>                     Austausch der eigenen .exe + Neustart in einer Kopie durchspielen (CI)";

    public static int Run(string[] a)
    {
        string Opt(string name, string def = "") { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("--") ? a[i + 1] : def; }
        bool Has(string name) => Array.IndexOf(a, name) >= 0;
        try
        {
            switch (a[0])
            {
                case "--help": case "--hilfe": Out(Help); return 0;
                case "--make-testscan":
                    TestScan.Make(Opt("--make-testscan", "testscan"), Has("--dark"), Has("--umlauts"), int.TryParse(Opt("--seed", "1"), out var sd) ? sd : 1, Has("--hard"));
                    return 0;
                case "--diag":
                {
                    var rep = Diagnostics.Report(Settings.Load(Paths.SettingsFile), Has("--network"));
                    var f = Opt("--diag"); if (f.Length > 0) File.WriteAllText(f, rep, new UTF8Encoding(false)); else Out(rep);
                    return 0;
                }
                case "--selftest":
                {
                    var ocr = WinOcr.TryCreate(out var problem);
                    var rep = $"Start ok. Texterkennung: {(ocr != null ? ocr.Description : "nicht verfügbar – " + problem)}";
                    var f = Opt("--report"); if (f.Length > 0) File.WriteAllText(f, rep, new UTF8Encoding(false)); else Out(rep);
                    return 0;
                }
                case "--update-check":
                {
                    var info = Updater.CheckAsync(Has("--force") ? new Version(0, 0, 1) : null).GetAwaiter().GetResult();
                    Emit(info == null ? $"aktuell version={Updater.CurrentText}" : $"update version={Updater.CurrentText} latest={info.Tag} asset={info.AssetName} size={info.Size} sums={(info.SumsUrl.Length > 0)}", Opt("--report"));
                    return 0;
                }
                case "--update-download-test":
                {
                    var info = Updater.CheckAsync(new Version(0, 0, 1)).GetAwaiter().GetResult() ?? throw new InvalidOperationException("Kein Release gefunden.");
                    int lastPct = 0; var path = Updater.DownloadAsync(info, p => lastPct = p).GetAwaiter().GetResult();
                    var len = new FileInfo(path).Length; File.Delete(path);
                    Emit($"ok tag={info.Tag} bytes={len} progress={lastPct}", Opt("--report")); return 0;
                }
                case "--update-selftest": return UpdateSelftest(Opt("--update-selftest", "update-selftest"));
                case "--swap-test":   // (intern) vom Selbsttest gestartet: tauscht SICH SELBST gegen die angegebene Datei und startet neu
                    Updater.InstallAndRestart(Path.GetFullPath(a[1]), "--update-report \"" + Path.GetFullPath(a[2]) + "\""); return 0;
                case "--process": return Process(Opt("--process"), Opt("--out"), Opt("--format", "jpg"), Opt("--profile", "Automatisch"), Opt("--dpi"), Opt("--report"), Opt("--profiles"));
                default: Out("Unbekannte Option: " + a[0] + "\n" + Help); return 2;
            }
        }
        catch (Exception e)
        {
            var msg = "Fehler: " + e.GetType().Name + ": " + e.Message + (e.InnerException != null ? " / " + e.InnerException.GetType().Name + ": " + e.InnerException.Message : "");
            try { var rf = Opt("--report"); if (rf.Length > 0) File.WriteAllText(rf, msg, new UTF8Encoding(false)); else Out(msg); } catch { Out(msg); }
            return 1;
        }
    }

    static void Emit(string text, string reportFile) { if (reportFile.Length > 0) File.WriteAllText(reportFile, text, new UTF8Encoding(false)); else Out(text); }

    /// <summary>Echter Ersetzen-und-Neustarten-Ablauf mit der eigenen .exe in einem Arbeitsordner (kein Netz): Kopie startet, tauscht sich gegen „neue Version“, neue startet und räumt auf.</summary>
    static int UpdateSelftest(string work)
    {
        work = Path.GetFullPath(work);   // absolut: der neu gestartete Prozess läuft in einem anderen Arbeitsverzeichnis
        Directory.CreateDirectory(work);
        var self = System.Windows.Forms.Application.ExecutablePath; var app = Path.Combine(work, "app.exe"); var neu = Path.Combine(work, "new.exe"); var res = Path.Combine(work, "result.txt");
        foreach (var f in new[] { app, neu, res, app + ".old" }) if (File.Exists(f)) File.Delete(f);
        File.Copy(self, app);
        var bytes = File.ReadAllBytes(self); using (var o = File.Create(neu)) { o.Write(bytes, 0, bytes.Length); var m = Encoding.ASCII.GetBytes("UPDATE-MARKER"); o.Write(m, 0, m.Length); }
        File.WriteAllText(Path.Combine(work, "trace.txt"), "start " + DateTime.Now.ToString("HH:mm:ss") + "\n");
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(app, "--swap-test \"" + neu + "\" \"" + res + "\"") { UseShellExecute = false });
        p!.WaitForExit(20000); File.AppendAllText(Path.Combine(work, "trace.txt"), "swap-prozess beendet exit=" + (p.HasExited ? p.ExitCode.ToString() : "läuft noch") + "\n");
        for (int i = 0; i < 80 && !File.Exists(res); i++) System.Threading.Thread.Sleep(250);
        var text = File.Exists(res) ? File.ReadAllText(res) : "FEHLT";
        bool ok = text.StartsWith("ok") && text.Contains("marker=True") && text.Contains("old_deleted=True") && !File.Exists(app + ".old");
        Out((ok ? "UPDATE-SELFTEST OK: " : "UPDATE-SELFTEST FEHLER: ") + text);
        File.WriteAllText(Path.Combine(work, "selftest.txt"), (ok ? "OK " : "FEHLER ") + text);
        return ok ? 0 : 1;
    }

    static int Process(string input, string outDir, string format, string profileName, string dpiOpt, string reportFile, string profilesDir)
    {
        if (input.Length == 0 || outDir.Length == 0) { Out("--process braucht <Datei|Ordner> und --out <Ordner>"); return 2; }
        var s = Settings.Load(Paths.SettingsFile); s.Format = format;
        if (int.TryParse(dpiOpt, out var d) && d > 0) s.Dpi = d;
        var problems = new List<string>();
        var profiles = ProfileStore.Load(profilesDir.Length > 0 ? profilesDir : Paths.ProfilesDir, problems);
        var selected = profiles.FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase)) ?? Profile.Auto();
        var ocr = WinOcr.TryCreate(out var ocrProblem);
        if (ocr == null) problems.Add(ocrProblem);

        var files = Directory.Exists(input)
            ? Directory.GetFiles(input).Where(f => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".pdf" }.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToList()
            : new List<string> { input };
        var report = new List<object?>(); var allWritten = 0;
        foreach (var f in files)
        {
            foreach (var loaded in BitmapCodec.LoadFile(f, problems))
            {
                var res = Pipeline.Process(loaded.Image, loaded.Dpi, s, profiles, selected, ocr);
                var sv = Saver.Save(res.Cards, s, outDir);
                allWritten += sv.Written.Count;
                for (int i = 0; i < res.Cards.Count; i++)
                {
                    var c = res.Cards[i];
                    report.Add(new Dictionary<string, object?>
                    {
                        ["last"] = c.Last, ["first"] = c.First, ["score"] = Math.Round(c.Score, 2), ["source"] = c.Source, ["profile"] = c.ProfileUsed,
                        ["warnings"] = c.Warnings.Cast<object?>().ToList(), ["file"] = i < sv.Written.Count ? Path.GetFileName(sv.Written[i]) : null,
                    });
                }
                problems.AddRange(res.Warnings); problems.AddRange(sv.Errors);
            }
        }
        var json = MiniJson.Write(new Dictionary<string, object?>
        { ["ocr"] = ocr?.Description, ["cards"] = report, ["problems"] = problems.Cast<object?>().ToList(), ["written"] = allWritten });
        if (reportFile.Length > 0) File.WriteAllText(reportFile, json, new UTF8Encoding(false)); else Out(json);
        return allWritten > 0 ? 0 : 3;
    }

    // WinExe hat keine Konsole: an die des Aufrufers anhängen, sonst ginge die Ausgabe verloren.
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    static void Out(string s) { try { AttachConsole(-1); Console.WriteLine(s); } catch { } }
}
