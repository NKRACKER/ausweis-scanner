using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>Alles Konfigurierbare an einem Ort, in Tabs: das Hauptfenster bleibt dadurch schlicht.</summary>
sealed class SettingsForm : Form
{
    readonly MainForm _m; readonly Settings _s; readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.Font(10f), Padding = new Point(18, 6) };
    // Allgemein
    readonly TextBox _dir = Theme.Field(380), _tpl = Theme.Field(260);
    readonly RadioButton _jpg = new RadioButton { Text = "JPG (klein, überall lesbar – empfohlen)", AutoSize = true }, _pdf = new RadioButton { Text = "PDF (eine Seite pro Ausweis, in Kartengröße)", AutoSize = true }, _png = new RadioButton { Text = "PNG (verlustfrei, große Dateien)", AutoSize = true };
    readonly CheckBox _umlaut = new CheckBox { Text = "Umlaute ersetzen (ä → ae, ö → oe, ü → ue, ß → ss)", AutoSize = true };
    readonly Label _example = new Label { AutoSize = true, ForeColor = Theme.Muted };
    readonly CheckBox _updChk = new CheckBox { Text = "Automatisch nach neuen Versionen suchen (verbindet sich beim Start mit github.com)", AutoSize = true };
    readonly Label _updMsg = new Label { AutoSize = false, Size = new Size(470, 40), ForeColor = Theme.Muted };
    // Scanner
    readonly ComboBox _dev = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400 };
    readonly NumericUpDown _dpi = new NumericUpDown { Minimum = 100, Maximum = 600, Increment = 50, Width = 90 };
    readonly CheckBox _driver = new CheckBox { Text = "Scan-Dialog des Herstellers verwenden (Vorschau, Bereich, Helligkeit …)", AutoSize = true };
    readonly CheckBox _escl = new CheckBox { Text = "Netzwerkscanner im lokalen Netz suchen (AirScan)", AutoSize = true };
    readonly TextBox _hosts = new TextBox { Multiline = true, Width = 400, Height = 56, ScrollBars = ScrollBars.Vertical };
    // Design
    readonly ComboBox _profile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    readonly ListBox _designs = new ListBox { Width = 400, Height = 100, IntegralHeight = false };
    // Erweitert
    readonly NumericUpDown _q = new NumericUpDown { Minimum = 50, Maximum = 100, Width = 80 }, _inset = new NumericUpDown { Minimum = 0, Maximum = 3, DecimalPlaces = 1, Increment = 0.1M, Width = 80 };
    readonly TextBox _watch = Theme.Field(380);
    // Diagnose
    readonly TextBox _diag = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9.5f), BackColor = Color.White };
    bool _diagLoaded;
    public int TabCount => _tabs.TabCount;
    public void SelectTab(int i) => _tabs.SelectedIndex = i;

    public SettingsForm(MainForm m, int tab)
    {
        _m = m; _s = m.S; if (Theme.AppIcon != null) Icon = Theme.AppIcon; Text = "Einstellungen"; Size = new Size(720, 590); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; Font = Theme.Font(10f); BackColor = Theme.Bg;
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Theme.Surface };
        var ok = Theme.Primary("Fertig", 120, 36); var cancel = Theme.Ghost("Abbrechen", 110, 36);
        // Position über Resize statt Anchor: Anchor würde gegen die noch ungültige Anfangsbreite des Panels gerechnet (Knöpfe landeten außerhalb)
        bottom.Resize += (_, __) => { ok.Location = new Point(bottom.Width - 140, 11); cancel.Location = new Point(bottom.Width - 260, 11); };
        ok.DialogResult = DialogResult.None; cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, __) => { Apply(); DialogResult = DialogResult.OK; Close(); };
        bottom.Controls.Add(ok); bottom.Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
        Controls.Add(_tabs); Controls.Add(bottom);

        _tabs.TabPages.Add(PageGeneral()); _tabs.TabPages.Add(PageScanner()); _tabs.TabPages.Add(PageDesign()); _tabs.TabPages.Add(PageAdvanced()); _tabs.TabPages.Add(PageDiag());
        foreach (TabPage p in _tabs.TabPages)
        {
            // Inhalt in ein eigenes Panel mit festem Rand: TabPage.Padding verschob Beschriftungen gegenüber Eingabefeldern
            p.BackColor = Theme.Surface; p.Padding = Padding.Empty;
            var inner = new Panel { Location = new Point(20, 16), Size = new Size(640, 470), BackColor = Color.Transparent, Anchor = AnchorStyles.Top | AnchorStyles.Left };
            var kids = p.Controls.Cast<Control>().ToList(); p.Controls.Clear();
            foreach (var k in kids) inner.Controls.Add(k);
            p.Controls.Add(inner);
        }
        _tabs.SelectedIndex = Math.Max(0, Math.Min(tab, _tabs.TabCount - 1));
        _tabs.SelectedIndexChanged += async (_, __) => { if (_tabs.SelectedIndex == 4 && !_diagLoaded) await LoadDiag(); };
        Shown += async (_, __) => { if (_tabs.SelectedIndex == 4 && !_diagLoaded) await LoadDiag(); };
    }

    // ---- Bausteine
    static Label H(string t, int y) => new Label { Text = t, Font = Theme.Font(10f, true), ForeColor = Theme.Text, AutoSize = true, Location = new Point(0, y) };
    static Label Hint(string t, int y, int w = 640) => new Label { Text = t, Font = Theme.Font(9f), ForeColor = Theme.Muted, AutoSize = false, Size = new Size(w, 36), Location = new Point(0, y) };
    static T At<T>(T c, int x, int y) where T : Control { c.Location = new Point(x, y); return c; }

    TabPage PageGeneral()
    {
        var p = new TabPage("Allgemein");
        p.Controls.Add(H("Speicherordner", 0));
        _dir.Text = _s.OutputDir; p.Controls.Add(At(_dir, 0, 26));
        var b = Theme.Outline("Wählen…", 100, 28); b.Location = new Point(392, 25); b.Click += (_, __) => { using var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(_dir.Text) ? _dir.Text : "" }; if (d.ShowDialog(this) == DialogResult.OK) _dir.Text = d.SelectedPath; };
        p.Controls.Add(b);
        p.Controls.Add(H("Dateiformat", 76));
        _jpg.Checked = _s.Format == "jpg"; _pdf.Checked = _s.Format == "pdf"; _png.Checked = _s.Format == "png";
        p.Controls.Add(At(_jpg, 0, 102)); p.Controls.Add(At(_pdf, 0, 128)); p.Controls.Add(At(_png, 0, 154));
        p.Controls.Add(H("Dateiname", 200));
        _tpl.Text = _s.Template; p.Controls.Add(At(_tpl, 0, 226)); p.Controls.Add(At(_example, 280, 229));
        p.Controls.Add(Hint("Platzhalter: {Nachname}  {Vorname}  {Klasse}  {Datum}.  Doppelte Namen werden nie überschrieben, sondern mit _2, _3 … ergänzt.", 258));
        _umlaut.Checked = _s.TransliterateUmlauts; p.Controls.Add(At(_umlaut, 0, 302));
        p.Controls.Add(H("Updates", 346));
        _updChk.Checked = _s.CheckUpdates; p.Controls.Add(At(_updChk, 0, 372));
        var chk = Theme.Outline("Einmal suchen", 130, 30); chk.Location = new Point(0, 402);
        chk.Click += async (_, __) => { chk.Enabled = false; _updMsg.Text = "Suche …"; try { _updMsg.Text = await _m.CheckForUpdate(true); } finally { chk.Enabled = true; } };
        p.Controls.Add(chk); _updMsg.Location = new Point(142, 399); _updMsg.Text = "Installiert: Version " + Updater.CurrentText + (_s.CheckUpdates ? "" : "  ·  automatische Suche ist aus"); p.Controls.Add(_updMsg);
        EventHandler upd = (_, __) => UpdateExample(); _tpl.TextChanged += upd; _umlaut.CheckedChanged += upd; _jpg.CheckedChanged += upd; _pdf.CheckedChanged += upd; _png.CheckedChanged += upd; UpdateExample();
        return p;
    }

    void UpdateExample()
    {
        var n = FileNamer.BaseName(new ParsedName("Müller", "Anna"), new NamingOptions { Template = _tpl.Text.Trim().Length > 0 ? _tpl.Text.Trim() : "{Nachname}_{Vorname}", TransliterateUmlauts = _umlaut.Checked }, new Dictionary<string, string> { ["Klasse"] = "7b", ["Datum"] = DateTime.Now.ToString("yyyy-MM-dd") });
        _example.Text = "Beispiel:  " + n + (_pdf.Checked ? ".pdf" : _png.Checked ? ".png" : ".jpg");
    }

    TabPage PageScanner()
    {
        var p = new TabPage("Scanner");
        p.Controls.Add(H("Scanner", 0));
        FillDevices(); p.Controls.Add(At(_dev, 0, 26));
        var re = Theme.Outline("Neu suchen", 110, 28); re.Location = new Point(412, 25);
        re.Click += async (_, __) => { re.Enabled = false; re.Text = "Suche …"; try { _s.EsclEnabled = _escl.Checked; _s.EsclHosts = Hosts(); await _m.RefreshDevices(_escl.Checked); FillDevices(); } finally { re.Enabled = true; re.Text = "Neu suchen"; } };
        p.Controls.Add(re);
        p.Controls.Add(Hint("Gefunden werden Scanner über WIA, TWAIN und – wenn unten aktiviert – im Netzwerk (AirScan). Geht keiner davon: mit der Scanner-Software scannen und die Datei ins Hauptfenster ziehen.", 62));
        p.Controls.Add(H("Auflösung", 112)); _dpi.Value = _s.Dpi; p.Controls.Add(At(_dpi, 0, 138)); p.Controls.Add(At(new Label { Text = "dpi   (300 reicht für Ausweise)", AutoSize = true, ForeColor = Theme.Muted }, 100, 142));
        _driver.Checked = _s.UseDriverDialog; p.Controls.Add(At(_driver, 0, 182));
        p.Controls.Add(Hint("Nützlich, wenn der Schnellscan nicht klappt oder die Glasplatte nur teilweise erfasst wird.", 206));
        p.Controls.Add(H("Netzwerkscanner (AirScan / eSCL)", 252));
        _escl.Checked = _s.EsclEnabled; p.Controls.Add(At(_escl, 0, 278));
        p.Controls.Add(At(new Label { Text = "Feste Adressen, falls die Suche nichts findet (eine pro Zeile, z. B. 192.168.1.20):", AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Font(9f) }, 0, 308));
        _hosts.Text = string.Join("\r\n", _s.EsclHosts); p.Controls.Add(At(_hosts, 0, 330));
        return p;
    }

    List<string> Hosts() => _hosts.Text.Split(new[] { '\r', '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

    void FillDevices()
    {
        _dev.Items.Clear(); foreach (var d in _m.Devices) _dev.Items.Add(d);
        if (_m.Devices.Count == 0) { _dev.Items.Add("(kein Scanner gefunden)"); _dev.SelectedIndex = 0; _dev.Enabled = false; }
        else { _dev.Enabled = true; _dev.SelectedItem = _m.CurrentDevice ?? _m.Devices[0]; }
    }

    TabPage PageDesign()
    {
        var p = new TabPage("Ausweis-Design");
        p.Controls.Add(H("Welches Design wird gelesen?", 0));
        foreach (var pr in _m.Profiles) _profile.Items.Add(pr.Name == "Automatisch" ? "Automatisch (empfohlen)" : pr.Name);
        _profile.SelectedIndex = Math.Max(0, _m.Profiles.FindIndex(x => x.Name == _s.ProfileName)); p.Controls.Add(At(_profile, 0, 26));
        p.Controls.Add(Hint("„Automatisch“ probiert alle angelernten Designs durch und sucht sonst nach „Name:“/„Vorname“ auf der Karte. Das ist fast immer richtig – ein festes Design nur wählen, wenn mehrere ähnliche Designs verwechselt werden.", 62, 640));
        p.Controls.Add(H("Angelernte Designs", 116));
        p.Controls.Add(At(_designs, 0, 142)); FillDesigns();
        var add = Theme.Outline("Neues Design anlernen…", 210, 32); add.Location = new Point(0, 256); add.Click += (_, __) => { _m.TeachDesign(this); RefreshProfileLists(); };
        var del = Theme.Ghost("Ausgewähltes löschen", 180, 32); del.Location = new Point(220, 256);
        del.Click += (_, __) =>
        {
            if (!(_designs.SelectedItem is string name) || name == "Automatisch") return;
            if (MessageBox.Show($"Design „{name}“ wirklich löschen?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { var f = Directory.GetFiles(Paths.ProfilesDir, "*.json").FirstOrDefault(x => { try { return Profile.FromJson(File.ReadAllText(x)).Name == name; } catch { return false; } }); if (f != null) File.Delete(f); } catch (Exception e) { MessageBox.Show(e.Message); }
            _m.ReloadProfiles(_s.ProfileName); RefreshProfileLists();
        };
        var open = Theme.Ghost("Ordner öffnen", 130, 32); open.Location = new Point(410, 256); open.Click += (_, __) => { try { Directory.CreateDirectory(Paths.ProfilesDir); System.Diagnostics.Process.Start("explorer.exe", "\"" + Paths.ProfilesDir + "\""); } catch { } };
        p.Controls.Add(add); p.Controls.Add(del); p.Controls.Add(open);
        p.Controls.Add(Hint("Ändert die Schule das Ausweis-Design, genügt ein Beispiel-Ausweis: einmal das Namensfeld mit der Maus markieren. Designs sind einfache Dateien und lassen sich auf andere Rechner kopieren.", 304, 640));
        return p;
    }

    void FillDesigns() { _designs.Items.Clear(); foreach (var pr in _m.Profiles) _designs.Items.Add(pr.Name); }

    void RefreshProfileLists()
    {
        _profile.Items.Clear(); foreach (var pr in _m.Profiles) _profile.Items.Add(pr.Name == "Automatisch" ? "Automatisch (empfohlen)" : pr.Name);
        _profile.SelectedIndex = Math.Max(0, _m.Profiles.FindIndex(x => x.Name == _s.ProfileName)); FillDesigns();
    }

    TabPage PageAdvanced()
    {
        var p = new TabPage("Erweitert");
        p.Controls.Add(H("JPEG-Qualität", 0)); _q.Value = _s.JpegQuality; p.Controls.Add(At(_q, 0, 26)); p.Controls.Add(At(new Label { Text = "50–100   (92 ist ein guter Kompromiss)", AutoSize = true, ForeColor = Theme.Muted }, 92, 30));
        p.Controls.Add(H("Randabzug beim Ausschneiden", 76)); _inset.Value = (decimal)Math.Max(0, Math.Min(3, _s.EdgeInsetMm)); p.Controls.Add(At(_inset, 0, 102)); p.Controls.Add(At(new Label { Text = "mm   (schneidet Schattenrand und Hintergrund weg; zu groß schneidet Inhalt an)", AutoSize = true, ForeColor = Theme.Muted }, 92, 106));
        p.Controls.Add(H("Ordner überwachen (Automatik)", 152));
        p.Controls.Add(Hint("Neue Scan-Dateien (Bild/PDF) in diesem Ordner werden automatisch eingelesen – z. B. wenn die Scanner-Software dort ablegt. Leer = aus.", 178, 640));
        _watch.Text = _s.WatchFolder; p.Controls.Add(At(_watch, 0, 222));
        var b = Theme.Outline("Wählen…", 100, 28); b.Location = new Point(392, 221); b.Click += (_, __) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) _watch.Text = d.SelectedPath; };
        var clear = Theme.Ghost("Aus", 60, 28); clear.Location = new Point(500, 221); clear.Click += (_, __) => _watch.Text = "";
        p.Controls.Add(b); p.Controls.Add(clear);
        p.Controls.Add(H("Datenschutz", 280));
        p.Controls.Add(Hint("Das Programm verbindet sich nur mit github.com, um nach neuen Versionen zu fragen (abschaltbar unter „Allgemein“) – dabei werden keine Daten über Sie, Scans oder Ausweise gesendet. Der Roh-Scan wird nicht gespeichert, nur die einzelnen Ausweise im Speicherordner. Einstellungen und Designs enthalten keine Namen.", 306, 640));
        return p;
    }

    TabPage PageDiag()
    {
        var p = new TabPage("Diagnose");
        var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.Transparent };
        var again = Theme.Outline("Neu erstellen", 130, 30); again.Location = new Point(0, 4); again.Click += async (_, __) => await LoadDiag();
        var copy = Theme.Outline("Kopieren", 110, 30); copy.Location = new Point(140, 4); copy.Click += (_, __) => { try { Clipboard.SetText(_diag.Text); copy.Text = "Kopiert ✓"; } catch { } };
        top.Controls.Add(again); top.Controls.Add(copy);
        p.Controls.Add(_diag); p.Controls.Add(top); _diag.BringToFront();
        return p;
    }

    async System.Threading.Tasks.Task LoadDiag()
    {
        _diagLoaded = true; _diag.Text = "Erstelle Bericht … (Scanner werden gesucht)";
        try { var t = await System.Threading.Tasks.Task.Run(() => _m.DiagText()); _diag.Text = t.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n(Enthält keine Personendaten – zum Weitergeben bei Problemen geeignet.)"; }
        catch (Exception e) { _diag.Text = "Fehler: " + e.Message; }
    }

    void Apply()
    {
        _s.OutputDir = _dir.Text.Trim(); _s.Format = _pdf.Checked ? "pdf" : _png.Checked ? "png" : "jpg";
        _s.Template = _tpl.Text.Trim().Length > 0 ? _tpl.Text.Trim() : "{Nachname}_{Vorname}"; _s.TransliterateUmlauts = _umlaut.Checked;
        _s.Dpi = (int)_dpi.Value; _s.UseDriverDialog = _driver.Checked; _s.EsclEnabled = _escl.Checked; _s.EsclHosts = Hosts();
        if (_dev.SelectedItem is ScanDevice d) _m.SelectDevice(d.Id);
        var idx = _profile.SelectedIndex; if (idx >= 0 && idx < _m.Profiles.Count) _s.ProfileName = _m.Profiles[idx].Name;
        _s.CheckUpdates = _updChk.Checked; _s.JpegQuality = (int)_q.Value; _s.EdgeInsetMm = (double)_inset.Value; _s.WatchFolder = _watch.Text.Trim();
    }
}
