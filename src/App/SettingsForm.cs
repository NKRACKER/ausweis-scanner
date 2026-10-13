using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>
/// Alles Konfigurierbare an einem Ort, in Tabs: das Hauptfenster bleibt dadurch schlicht.
/// Antwortfähig: Felder füllen die Breite, Inhalte scrollen statt abgeschnitten zu werden; Erklärungen stehen hinter kleinen „?“.
/// </summary>
sealed class SettingsForm : Form
{
    readonly MainForm _m; readonly Settings _s; readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill, Font = Theme.Font(10f), Padding = new Point(18, 6) };
    // Allgemein
    readonly TextBox _dir = Theme.Field(380), _tpl = Theme.Field(260);
    readonly RadioButton _jpg = new RadioButton { Text = "JPG  (empfohlen)", AutoSize = true }, _pdf = new RadioButton { Text = "PDF", AutoSize = true }, _png = new RadioButton { Text = "PNG", AutoSize = true };
    readonly CheckBox _umlaut = new CheckBox { Text = "Umlaute ersetzen (ä → ae, ß → ss)", AutoSize = true };
    readonly Label _example = new Label { AutoSize = true, ForeColor = Theme.Muted, UseMnemonic = false };
    readonly CheckBox _updChk = new CheckBox { Text = "Automatisch nach neuen Versionen suchen", AutoSize = true };
    readonly Label _updMsg = new Label { AutoSize = true, ForeColor = Theme.Muted, UseMnemonic = false };
    // Scanner
    readonly ComboBox _dev = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400 };
    readonly NumericUpDown _dpi = new NumericUpDown { Minimum = 100, Maximum = 600, Increment = 50, Width = 90 };
    readonly CheckBox _driver = new CheckBox { Text = "Scan-Dialog des Herstellers verwenden", AutoSize = true };
    readonly CheckBox _escl = new CheckBox { Text = "Netzwerkscanner im lokalen Netz suchen", AutoSize = true };
    readonly TextBox _hosts = new TextBox { Multiline = true, Height = 64, ScrollBars = ScrollBars.Vertical };
    // Design
    readonly ComboBox _profile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    readonly ListBox _designs = new ListBox { Height = 110, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    // Erweitert
    readonly NumericUpDown _q = new NumericUpDown { Minimum = 50, Maximum = 100, Width = 80 }, _inset = new NumericUpDown { Minimum = 0, Maximum = 3, DecimalPlaces = 1, Increment = 0.1M, Width = 80 };
    readonly TextBox _watch = Theme.Field(380);
    // Diagnose
    readonly TextBox _diag = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", Theme.Pt(9.5f)), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    bool _diagLoaded;
    public int TabCount => _tabs.TabCount;
    public void SelectTab(int i) => _tabs.SelectedIndex = i;

    public SettingsForm(MainForm m, int tab)
    {
        _m = m; _s = m.S; if (Theme.AppIcon != null) Icon = Theme.AppIcon; Text = "Einstellungen"; Size = new Size(760, 640); MinimumSize = new Size(600, 480);
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true; MinimizeBox = false; Font = Theme.Font(10f); BackColor = Theme.Bg;
        var bar = Dialogs.BottomBar(out var ok, out var cancel, this, "Fertig", out _);
        ok.DialogResult = DialogResult.None; ok.Click += (_, __) => { Apply(); DialogResult = DialogResult.OK; Close(); };
        Controls.Add(_tabs); Controls.Add(bar);

        _tabs.TabPages.Add(PageGeneral()); _tabs.TabPages.Add(PageScanner()); _tabs.TabPages.Add(PageDesign()); _tabs.TabPages.Add(PageAdvanced()); _tabs.TabPages.Add(PageDiag());
        Theme.Fit(this);
        _tabs.SelectedIndex = Math.Max(0, Math.Min(tab, _tabs.TabCount - 1));
        _tabs.SelectedIndexChanged += async (_, __) => { if (_tabs.SelectedIndex == 4 && !_diagLoaded) await LoadDiag(); };
        Shown += async (_, __) => { if (_tabs.SelectedIndex == 4 && !_diagLoaded) await LoadDiag(); };
    }

    // ---- Bausteine: Seite = scrollbare Spalte; Zeile = Überschrift (+ ?) bzw. Eingabe (Feld füllt die Breite)
    static TabPage Page(string title, params Control[] rows)
    {
        var p = new TabPage(title) { BackColor = Theme.Surface, AutoScroll = true, Padding = Padding.Empty };
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = Color.Transparent, Padding = new Padding(22, 18, 22, 18) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var r in rows) { r.Dock = DockStyle.Top; t.Controls.Add(r); }
        p.Controls.Add(t); return p;
    }

    /// <summary>Überschrift mit optionalem „?“.</summary>
    static Control Head(string text, string? helpTitle = null, string? help = null)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 14, 0, 6) };
        f.Controls.Add(new Label { Text = text, Font = Theme.Font(10.5f, true), ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 2, 0, 0), UseMnemonic = false });
        if (help != null) { var q = Theme.Help(helpTitle ?? text, help); q.Margin = new Padding(8, 0, 0, 0); f.Controls.Add(q); }
        return f;
    }

    /// <summary>Zeile mit einem Feld, das die Breite füllt, und optionalen Knöpfen rechts.</summary>
    static Control Row(Control fill, params Control[] right)
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 + right.Length, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fill.Dock = DockStyle.Fill; fill.Margin = new Padding(0, 0, right.Length > 0 ? 8 : 0, 0); fill.MinimumSize = new Size(Theme.Px(120), 0);
        t.Controls.Add(fill, 0, 0);
        for (int i = 0; i < right.Length; i++) { t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); right[i].Anchor = AnchorStyles.Right; right[i].Margin = new Padding(i == 0 ? 0 : 6, 0, 0, 0); t.Controls.Add(right[i], i + 1, 0); }
        return t;
    }

    static Control Flow(params Control[] items)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4) };
        foreach (var i in items) { i.Margin = new Padding(0, 3, 14, 3); f.Controls.Add(i); }
        return f;
    }

    /// <summary>Kontrollkästchen mit „?“ daneben.</summary>
    static Control WithHelp(Control c, string title, string help)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4) };
        c.Margin = new Padding(0, 3, 0, 0); f.Controls.Add(c); var q = Theme.Help(title, help); q.Margin = new Padding(8, 0, 0, 0); f.Controls.Add(q); return f;
    }

    TabPage PageGeneral()
    {
        _dir.Text = _s.OutputDir;
        var pick = Theme.Outline("Wählen…", 100, 32); pick.Click += (_, __) => { using var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(_dir.Text) ? _dir.Text : "" }; if (d.ShowDialog(this) == DialogResult.OK) _dir.Text = d.SelectedPath; };
        _jpg.Checked = _s.Format == "jpg"; _pdf.Checked = _s.Format == "pdf"; _png.Checked = _s.Format == "png";
        _tpl.Text = _s.Template; _umlaut.Checked = _s.TransliterateUmlauts; _tpl.Dock = DockStyle.None; _tpl.Width = Theme.Px(260);
        _updChk.Checked = _s.CheckUpdates;
        var chk = Theme.Outline("Einmal suchen", 130, 32);
        chk.Click += async (_, __) => { chk.Enabled = false; _updMsg.Text = "Suche …"; try { _updMsg.Text = await _m.CheckForUpdate(true); } finally { chk.Enabled = true; } };
        _updMsg.Text = "Installiert: Version " + Updater.CurrentText + (_s.CheckUpdates ? "" : "  ·  automatische Suche ist aus");
        EventHandler upd = (_, __) => UpdateExample(); _tpl.TextChanged += upd; _umlaut.CheckedChanged += upd; _jpg.CheckedChanged += upd; _pdf.CheckedChanged += upd; _png.CheckedChanged += upd; UpdateExample();
        return Page("Allgemein",
            Head("Speicherordner"), Row(_dir, pick),
            Head("Dateiformat", "Dateiformat", "JPG ist klein und überall lesbar (empfohlen). PDF legt eine Seite pro Ausweis in Kartengröße an. PNG ist verlustfrei, die Dateien werden aber groß."), Flow(_jpg, _pdf, _png),
            Head("Dateiname", "Dateiname", "Platzhalter: {Nachname}  {Vorname}  {Klasse}  {Datum}.\n\nDoppelte Namen werden nie überschrieben, sondern mit _2, _3 … ergänzt."), Flow(_tpl, _example), WithHelp(_umlaut, "Umlaute", "Ersetzt ä → ae, ö → oe, ü → ue, ß → ss – nützlich, wenn Dateinamen später auf Systemen ohne Umlaut-Unterstützung landen."),
            Head("Updates", "Update-Suche", "Beim Start fragt das Programm anonym bei github.com, ob es eine neue Version gibt. Es werden keine Daten über Scans oder Ausweise gesendet. Ausgeschaltet verbindet sich das Programm von selbst nie mit dem Internet; „Einmal suchen“ geht trotzdem."),
            _updChk, Flow(chk, _updMsg));
    }

    void UpdateExample()
    {
        var n = FileNamer.BaseName(new ParsedName("Müller", "Anna"), new NamingOptions { Template = _tpl.Text.Trim().Length > 0 ? _tpl.Text.Trim() : "{Nachname}_{Vorname}", TransliterateUmlauts = _umlaut.Checked }, new Dictionary<string, string> { ["Klasse"] = "7b", ["Datum"] = DateTime.Now.ToString("yyyy-MM-dd") });
        _example.Text = "→  " + n + (_pdf.Checked ? ".pdf" : _png.Checked ? ".png" : ".jpg");
    }

    TabPage PageScanner()
    {
        FillDevices();
        var re = Theme.Outline("Neu suchen", 110, 32);
        re.Click += async (_, __) => { re.Enabled = false; re.Text = "Suche …"; try { _s.EsclEnabled = _escl.Checked; _s.EsclHosts = Hosts(); await _m.RefreshDevices(_escl.Checked); FillDevices(); } finally { re.Enabled = true; re.Text = "Neu suchen"; } };
        _dpi.Value = _s.Dpi; _driver.Checked = _s.UseDriverDialog; _escl.Checked = _s.EsclEnabled; _hosts.Text = string.Join("\r\n", _s.EsclHosts);
        return Page("Scanner",
            Head("Scanner", "Scanner finden", "Gefunden werden Scanner über WIA, TWAIN und – wenn unten aktiviert – im Netzwerk (AirScan).\n\nGeht keiner davon: mit der Scanner-Software scannen und die Datei ins Hauptfenster ziehen."), Row(_dev, re),
            Head("Auflösung", "Auflösung", "300 dpi reicht für Ausweise. Höhere Werte machen den Scan langsamer, aber nicht besser lesbar."), Flow(_dpi, new Label { Text = "dpi", AutoSize = true, ForeColor = Theme.Muted, UseMnemonic = false }),
            Head("Scan-Dialog"), WithHelp(_driver, "Scan-Dialog des Herstellers", "Öffnet beim Scannen die Oberfläche des Scanner-Treibers (Vorschau, Bereich, Helligkeit …). Nützlich, wenn der Schnellscan nicht klappt oder die Glasplatte nur teilweise erfasst wird."),
            Head("Netzwerkscanner", "AirScan / eSCL", "Sucht per AirScan nach Scannern im lokalen Netz und spricht nur mit dem gewählten Scanner.\n\nFeste Adressen (eine pro Zeile, z. B. 192.168.1.20) helfen, wenn die Suche nichts findet."), _escl,
            Head("Feste Adressen"), Row(_hosts));
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
        foreach (var pr in _m.Profiles) _profile.Items.Add(pr.Name == "Automatisch" ? "Automatisch (empfohlen)" : pr.Name);
        _profile.SelectedIndex = Math.Max(0, _m.Profiles.FindIndex(x => x.Name == _s.ProfileName)); FillDesigns();
        var add = Theme.Outline("Neues Design anlernen…", 210, 34); add.Click += (_, __) => { _m.TeachDesign(this); RefreshProfileLists(); };
        var del = Theme.Ghost("Ausgewähltes löschen", 180, 34);
        del.Click += (_, __) =>
        {
            if (!(_designs.SelectedItem is string name) || name == "Automatisch") return;
            if (MessageBox.Show($"Design „{name}“ wirklich löschen?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { var f = Directory.GetFiles(Paths.ProfilesDir, "*.json").FirstOrDefault(x => { try { return Profile.FromJson(File.ReadAllText(x)).Name == name; } catch { return false; } }); if (f != null) File.Delete(f); } catch (Exception e) { MessageBox.Show(e.Message); }
            _m.ReloadProfiles(_s.ProfileName); RefreshProfileLists();
        };
        var open = Theme.Ghost("Ordner öffnen", 130, 34); open.Click += (_, __) => { try { Directory.CreateDirectory(Paths.ProfilesDir); System.Diagnostics.Process.Start("explorer.exe", "\"" + Paths.ProfilesDir + "\""); } catch { } };
        return Page("Ausweis-Design",
            Head("Welches Design wird gelesen?", "Design", "„Automatisch“ probiert alle angelernten Designs durch und sucht sonst nach „Name:“ / „Vorname“ auf der Karte. Das ist fast immer richtig – ein festes Design nur wählen, wenn mehrere ähnliche Designs verwechselt werden."), Row(_profile),
            Head("Angelernte Designs", "Neues Design anlernen", "Ändert die Schule das Ausweis-Design, genügt ein Beispiel-Ausweis: einmal das Namensfeld mit der Maus markieren.\n\nDesigns sind einfache Dateien (JSON) und lassen sich auf andere Rechner kopieren."), Row(_designs),
            Flow(add, del, open));
    }

    void FillDesigns() { _designs.Items.Clear(); foreach (var pr in _m.Profiles) _designs.Items.Add(pr.Name); }

    void RefreshProfileLists()
    {
        _profile.Items.Clear(); foreach (var pr in _m.Profiles) _profile.Items.Add(pr.Name == "Automatisch" ? "Automatisch (empfohlen)" : pr.Name);
        _profile.SelectedIndex = Math.Max(0, _m.Profiles.FindIndex(x => x.Name == _s.ProfileName)); FillDesigns();
    }

    TabPage PageAdvanced()
    {
        _q.Value = _s.JpegQuality; _inset.Value = (decimal)Math.Max(0, Math.Min(3, _s.EdgeInsetMm)); _watch.Text = _s.WatchFolder;
        var pick = Theme.Outline("Wählen…", 100, 32); pick.Click += (_, __) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog(this) == DialogResult.OK) _watch.Text = d.SelectedPath; };
        var off = Theme.Ghost("Aus", 60, 32); off.Click += (_, __) => _watch.Text = "";
        string Mut(string t) => t;
        Label L(string t) => new Label { Text = t, AutoSize = true, ForeColor = Theme.Muted, UseMnemonic = false };
        return Page("Erweitert",
            Head("JPEG-Qualität", "JPEG-Qualität", "50–100. 92 ist ein guter Kompromiss aus Dateigröße und Schärfe."), Flow(_q, L("%")),
            Head("Randabzug beim Ausschneiden", "Randabzug", "Schneidet Schattenrand und Hintergrund weg. Zu groß gewählt, wird Inhalt des Ausweises angeschnitten."), Flow(_inset, L("mm")),
            Head("Ordner überwachen", "Ordner überwachen", "Neue Scan-Dateien (Bild/PDF) in diesem Ordner werden automatisch eingelesen – z. B. wenn die Scanner-Software dort ablegt. Leer = aus."), Row(_watch, pick, off),
            Head("Datenschutz", "Datenschutz", "Alles bleibt lokal auf diesem Rechner. Die einzige Verbindung ist die anonyme Update-Abfrage bei github.com (abschaltbar unter „Allgemein“) – dabei werden keine Daten über Sie, Scans oder Ausweise gesendet. Der Roh-Scan wird nicht gespeichert, nur die einzelnen Ausweise im Speicherordner. Einstellungen und Designs enthalten keine Namen."),
            L("Alles bleibt lokal auf diesem Rechner."));
    }

    TabPage PageDiag()
    {
        var p = new TabPage("Diagnose") { BackColor = Theme.Surface, Padding = new Padding(16) };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Padding = new Padding(0, 0, 0, 8) };
        var again = Theme.Outline("Neu erstellen", 130, 34); again.Click += async (_, __) => await LoadDiag();
        var copy = Theme.Outline("Kopieren", 110, 34); copy.Click += (_, __) => { try { Clipboard.SetText(_diag.Text); copy.Text = "Kopiert ✓"; } catch { } };
        var q = Theme.Help("Diagnose", "Zeigt, welche Scanner und welche Texterkennung gefunden wurden – hilfreich bei Problemen. Der Bericht enthält keine Personendaten."); q.Margin = new Padding(8, 6, 0, 0);
        top.Controls.Add(again); copy.Margin = new Padding(6, 3, 0, 3); top.Controls.Add(copy); top.Controls.Add(q);
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
