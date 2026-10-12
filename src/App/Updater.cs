using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>
/// Update-Prüfung und Selbst-Aktualisierung über GitHub Releases. Gesendet wird nur eine anonyme GET-Anfrage (Programmname + Version im User-Agent,
/// keine IDs, keine Daten über Scans/Ausweise). Installiert wird NUR, wenn die SHA-256-Prüfsumme aus dem Release zur heruntergeladenen Datei passt.
/// </summary>
static class Updater
{
    const string Repo = "NKRACKER/ausweis-scanner";
    public static Version Current => UpdateLogic.Normalize(typeof(Updater).Assembly.GetName().Version!);
    public static string CurrentText => Current.ToString(3);

    static HttpClient NewClient(int timeoutSeconds, bool apiAuth = false)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;   // GitHub verlangt TLS 1.2+
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Ausweis-Scanner/" + CurrentText);
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        // Nur für die CI: geteilte Runner-IPs reißen das anonyme API-Limit (60/h). Beim Endnutzer ist die Variable nie gesetzt; Downloads bekommen sie nie.
        var tok = Environment.GetEnvironmentVariable("AUSWEIS_GH_TOKEN");
        if (apiAuth && !string.IsNullOrEmpty(tok)) c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tok);
        return c;
    }

    /// <summary>Gibt das neuere Release zurück oder null (aktuell). Wirft bei Netzwerkfehlern.</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version? pretendCurrent = null)
    {
        using var c = NewClient(10, apiAuth: true);
        var json = await c.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest").ConfigureAwait(false);
        return UpdateLogic.FromReleaseJson(json, pretendCurrent ?? Current, Environment.Is64BitProcess);
    }

    /// <summary>Lädt das Programm, prüft die SHA-256 gegen SHA256SUMS.txt des Releases und liefert den Pfad der geprüften Datei.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, Action<int>? progress)
    {
        var dir = Path.Combine(Paths.DataDir, "update"); Directory.CreateDirectory(dir);
        var part = Path.Combine(dir, info.AssetName + ".part"); var final = Path.Combine(dir, info.AssetName);
        using var c = NewClient(600);
        var sums = await c.GetStringAsync(info.SumsUrl).ConfigureAwait(false);
        var expected = UpdateLogic.ExpectedSha256(sums, info.AssetName) ?? throw new InvalidOperationException("Die Prüfsumme für dieses Update fehlt im Release.");
        using (var resp = await c.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? info.Size, done = 0; int last = -1, n;
            using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var dst = File.Create(part);
            var buf = new byte[81920];
            while ((n = await src.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
            {
                dst.Write(buf, 0, n); done += n;
                if (total > 0) { int pct = (int)(done * 100 / total); if (pct != last) { last = pct; progress?.Invoke(pct); } }
            }
        }
        string actual;
        using (var fs = File.OpenRead(part)) using (var sha = SHA256.Create()) actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        if (actual != expected) { TryDelete(part); throw new InvalidOperationException("Die heruntergeladene Datei ist beschädigt oder verändert (Prüfsumme stimmt nicht). Das Update wurde NICHT installiert."); }
        if (new FileInfo(part).Length < 100000 || !StartsWithMz(part)) { TryDelete(part); throw new InvalidOperationException("Die heruntergeladene Datei ist kein gültiges Programm. Das Update wurde NICHT installiert."); }
        TryDelete(final); File.Move(part, final);
        return final;
    }

    /// <summary>
    /// Ersetzt die laufende .exe durch <paramref name="newExe"/> und startet sie neu (der Aufruf kehrt bei Erfolg nicht zurück).
    /// Windows erlaubt, eine laufende .exe umzubenennen: alt → „.old“, neu → Originalname. Der neue Prozess löscht „.old“.
    /// Ist der Ordner nicht beschreibbar (schreibgeschützter Stick/Programmordner), wird die neue Version aus dem Datenordner gestartet.
    /// </summary>
    public static void InstallAndRestart(string newExe, string extraArgs = "")
    {
        var cur = Application.ExecutablePath; var dir = Path.GetDirectoryName(cur)!;
        string target; string args = "--post-update";
        if (CanWrite(dir))
        {
            var old = cur + ".old"; TryDelete(old);
            File.Move(cur, old);
            try { File.Move(newExe, cur); }
            catch { try { File.Move(old, cur); } catch { /* Rückweg best effort */ } throw; }
            target = cur;
        }
        else
        {
            target = Path.Combine(Paths.DataDir, Path.GetFileNameWithoutExtension(cur) + "-neu" + Path.GetExtension(cur));
            File.Copy(newExe, target, true); args += " --moved";
        }
        Process.Start(new ProcessStartInfo(target, (args + " " + extraArgs).Trim()) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! });
        Environment.Exit(0);
    }

    /// <summary>Wird vom neu gestarteten Programm aufgerufen: räumt die alte Datei weg (der Vorgänger beendet sich gerade → kurz wiederholen).</summary>
    public static bool CleanupOld()
    {
        var old = Application.ExecutablePath + ".old";
        for (int i = 0; i < 30; i++)
        {
            if (!File.Exists(old)) return true;
            try { File.Delete(old); return true; } catch { System.Threading.Thread.Sleep(200); }
        }
        return !File.Exists(old);
    }

    static bool CanWrite(string dir)
    {
        try { var t = Path.Combine(dir, ".schreibtest" + Guid.NewGuid().ToString("N")); File.WriteAllText(t, "x"); File.Delete(t); return true; } catch { return false; }
    }

    static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

    static bool StartsWithMz(string p) { using var f = File.OpenRead(p); return f.ReadByte() == 'M' && f.ReadByte() == 'Z'; }
}
