using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>
/// Hauptfenster bewusst schlicht: Leerzustand mit den drei Schritten (Auflegen → Scannen → Prüfen &amp; speichern),
/// danach die erkannten Ausweise und unten „Alle speichern“. Alles Konfigurierbare liegt im Einstellungs-Dialog.
/// </summary>
sealed class MainForm : Form
{
    internal Settings S = Settings.Load(Paths.SettingsFile);
    internal List<Profile> Profiles = new List<Profile>();
    List<string> _profileProblems = new List<string>();
    IOcrEngine? _ocr; string _ocrProblem = "";
    internal List<ScanDevice> Devices = new List<ScanDevice>(); ScanDevice? _device;

    readonly Label _title = new Label { Text = "Ausweis-Scanner", Font = Theme.Font(14f, true), ForeColor = Theme.Text, AutoSize = true, Anchor = AnchorStyles.Left, BackColor = Color.Transparent, UseMnemonic = false };
    readonly Button _open = Theme.Outline("Datei öffnen…", 130, 36), _settingsBtn = Theme.Ghost("⚙  Einstellungen", 150, 36);
    readonly Button _updateBtn = Theme.Primary("⬆  Update verfügbar", 230, 36, Theme.Ok);
    UpdateInfo? _update; readonly string? _postUpdate;
    // Hinweisleiste: eine kompakte, schließbare Zeile unter der Kopfzeile (schiebt den Inhalt nach unten, verdeckt nie etwas)
    readonly TableLayoutPanel _notice = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, BackColor = Theme.WarnSoft, Visible = false, Padding = new Padding(24, 8, 12, 8), Margin = Padding.Empty };
    readonly Label _noticeText = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(120, 72, 0), Font = Theme.Font(9.5f), BackColor = Color.Transparent, UseMnemonic = false };
    readonly Button _noticeAction = Theme.Outline("Im Scan prüfen", 130, 32), _noticeClose = Theme.Icon("✕", "Hinweis schließen");
    ScanInfo? _noticeScan;
    // Leerzustand
    readonly TableLayoutPanel _empty = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = Padding.Empty, ColumnCount = 1, RowCount = 1, AutoScroll = true }, _inner = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, BackColor = Color.Transparent, Anchor = AnchorStyles.None, Margin = Padding.Empty };
    readonly Button _scan = Theme.Primary("Scannen", 260, 58);
    readonly Label _emptyStatus = new Label { AutoSize = true, MaximumSize = new Size(700, 0), TextAlign = ContentAlignment.MiddleCenter, Font = Theme.Font(10.5f), ForeColor = Theme.Text, BackColor = Color.Transparent, UseMnemonic = false };
    readonly Label _scannerInfo = new Label { AutoSize = true, MaximumSize = new Size(700, 0), TextAlign = ContentAlignment.MiddleCenter, Font = Theme.Font(9.5f), ForeColor = Theme.Muted, BackColor = Color.Transparent, UseMnemonic = false };
    // Liste
    readonly Panel _list = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false, Margin = Padding.Empty };
    readonly Label _summary = new Label { Font = Theme.Font(12f, true), ForeColor = Theme.Text, AutoSize = true, Anchor = AnchorStyles.Left, BackColor = Color.Transparent, UseMnemonic = false };
    readonly Button _addScan = Theme.Outline("＋  Weiterer Scan", 160, 36), _clear = Theme.Ghost("Liste leeren", 110, 36), _viewScan = Theme.Outline("Gesamten Scan", 170, 36);
    readonly FlowLayoutPanel _flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(16, 4, 16, 16) };
    // Fußleiste
    readonly TableLayoutPanel _bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, BackColor = Theme.Surface, Visible = false, Padding = new Padding(24, 10, 24, 10), MinimumSize = new Size(0, 72), Margin = Padding.Empty };
    readonly Label _barFolderCaption = new Label { Text = "Speichern in", Font = Theme.Font(8.5f), ForeColor = Theme.Muted, AutoSize = true, BackColor = Color.Transparent, UseMnemonic = false };
    readonly Label _barFolder = new Label { Font = Theme.Font(10f), ForeColor = Theme.Text, AutoSize = true, Anchor = AnchorStyles.Left, BackColor = Color.Transparent, UseMnemonic = false };
    readonly Button _changeFolder = Theme.Ghost("Ändern", 70, 26);
    readonly Label _barStatus = new Label { Font = Theme.Font(9f), ForeColor = Theme.Muted, AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent, Margin = new Padding(16, 0, 16, 0), UseMnemonic = false };
    readonly Button _save = Theme.Primary("Alle speichern", 220, 48, Theme.Ok);

    readonly List<CardControl> _cards = new List<CardControl>();
    readonly ConcurrentQueue<string> _watchQueue = new ConcurrentQueue<string>();
    FileSystemWatcher? _watcher; readonly System.Windows.Forms.Timer _watchTimer = new System.Windows.Forms.Timer { Interval = 2000 };
    bool _busy; internal List<string> DeviceNotes = new List<string>(); readonly string[]? _startFiles; readonly string? _uiTestOut;

    public MainForm(string[]? startFiles, string? uiTestOut = null, string? postUpdate = null)
    {
        _startFiles = startFiles; _uiTestOut = uiTestOut; _postUpdate = postUpdate;
        if (Theme.AppIcon != null) Icon = Theme.AppIcon;
        _updateBtn.Visible = false;
        Text = "Ausweis-Scanner"; BackColor = Theme.Bg; Font = Theme.Font(10f); AllowDrop = true; KeyPreview = true;
        Size = new Size(1180, 800); MinimumSize = new Size(900, 580); StartPosition = FormStartPosition.CenterScreen;
        BuildLayout(); Wire(); Theme.Fit(this);
        if (S.OutputDir.Length == 0) S.OutputDir = Paths.DefaultOutputDir();
        ReloadProfiles(S.ProfileName);
        _ocr = WinOcr.TryCreate(out _ocrProblem);
        UpdateView();
        Shown += async (_, __) =>
        {
            if (_ocr == null) Notice("Ohne Texterkennung müssen die Namen von Hand eingetippt werden. " + _ocrProblem, true);
            if (_postUpdate != null) Notice(_postUpdate, true);
            RestartWatcher();
            if (S.CheckUpdates && _uiTestOut == null) _ = CheckForUpdate(false);   // im Hintergrund, blockiert nichts
            await RefreshDevices(false);
            if (_startFiles != null) await LoadFiles(_startFiles.Where(File.Exists));
            if (_uiTestOut != null) await UiTestCapture();
        };
    }

    // ------------------------------------------------------------------ Layout
    void BuildLayout()
    {
        // Gerüst: eine Tabelle mit Kopfzeile / Hinweisleiste / Inhalt / Fußleiste. Zeilen, die leer sind, haben Höhe 0 – nichts liegt übereinander.
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Bg, Margin = Padding.Empty, Padding = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, BackColor = Theme.Surface, Padding = new Padding(22, 10, 16, 10), MinimumSize = new Size(0, 64), Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Paint += (_, e) => { using var p = new Pen(Theme.Line); e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1); };
        var logo = new PictureBox { Size = new Size(30, 30), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0) };
        try { if (Theme.AppIcon != null) logo.Image = new Icon(Theme.AppIcon, 64, 64).ToBitmap(); } catch { }
        var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
        foreach (var b in new[] { _updateBtn, _open, _settingsBtn }) b.Margin = new Padding(6, 0, 0, 0);
        right.Controls.Add(_updateBtn); right.Controls.Add(_open); right.Controls.Add(_settingsBtn);
        header.Controls.Add(logo, 0, 0); header.Controls.Add(_title, 1, 0); header.Controls.Add(right, 2, 0);

        // Hinweisleiste
        _notice.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); _notice.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); _notice.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); _notice.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _noticeAction.Visible = false; _noticeAction.Margin = new Padding(12, 0, 6, 0); _noticeAction.Anchor = AnchorStyles.None; _noticeClose.Margin = new Padding(0); _noticeClose.Anchor = AnchorStyles.None;
        _notice.Controls.Add(_noticeText, 0, 0); _notice.Controls.Add(_noticeAction, 1, 0); _notice.Controls.Add(_noticeClose, 2, 0);
        Resize += (_, __) => { if (_notice.Visible) FitNoticeText(); };
        _noticeClose.Click += (_, __) => Notice("", false);
        _noticeAction.Click += async (_, __) => { var sc = _noticeScan; if (sc != null) await OpenScanView(sc, null); };

        // Leerzustand: drei Schritte (kurz, Details hinter „?“), ein großer Knopf – bricht bei schmalem Fenster um und bleibt zentriert
        var steps = new FlowLayoutPanel { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 26), Anchor = AnchorStyles.None, MaximumSize = new Size(900, 0) };
        steps.Controls.Add(Step("1", "Auflegen", "Bildseite nach unten, mit Abstand", "Ausweise auflegen", "Bildseite nach unten, etwa 5 mm Abstand zueinander und zum Rand. Die Drehung ist egal. Deckel offen lassen oder dunkles Papier darüberlegen – das erkennt helle Karten am besten."));
        steps.Controls.Add(Step("2", "Scannen", "Ein Klick – Ausweise werden gefunden", "Scannen", "Die Ausweise werden automatisch erkannt, ausgeschnitten und gedreht; der Name wird gelesen. Alles bleibt lokal auf diesem Rechner."));
        steps.Controls.Add(Step("3", "Speichern", "Namen prüfen, alle speichern", "Prüfen & speichern", "Jeder Ausweis wird eine Datei „Nachname_Vorname“. Doppelte Namen bekommen _2, _3 … – es wird nie etwas überschrieben."));
        _scan.Anchor = AnchorStyles.None; _scan.Margin = new Padding(0, 0, 0, 14);
        _emptyStatus.Anchor = AnchorStyles.None; _scannerInfo.Anchor = AnchorStyles.None; _scannerInfo.Margin = new Padding(0, 0, 0, 6);
        var drag = new Label { Text = "oder eine Scan-Datei (Bild/PDF) hierher ziehen", Font = Theme.Font(9.5f), ForeColor = Theme.Muted, AutoSize = true, Anchor = AnchorStyles.None, BackColor = Color.Transparent, UseMnemonic = false };
        _inner.Controls.Add(steps, 0, 0); _inner.Controls.Add(_scan, 0, 1); _inner.Controls.Add(_emptyStatus, 0, 2); _inner.Controls.Add(_scannerInfo, 0, 3); _inner.Controls.Add(drag, 0, 4);
        _empty.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); _empty.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _empty.Controls.Add(_inner, 0, 0);

        // Liste: oben Zusammenfassung + Aktionen, darunter die Kacheln
        // Fließend mit Umbruch: bei schmalem Fenster rutschen die Knöpfe in eine zweite Zeile, statt den Text zu quetschen
        var strip = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, BackColor = Theme.Bg, Padding = new Padding(24, 12, 20, 6), Margin = Padding.Empty };
        _summary.Margin = new Padding(0, 8, 4, 0); strip.Controls.Add(_summary);
        var help = Theme.Help("Und jetzt?", "Namen kontrollieren und bei Bedarf korrigieren (Enter springt zum nächsten Feld). Klick aufs Bild oder ✂ passt den Zuschnitt an, ⟳ dreht, ✕ entfernt den Ausweis. Danach „Alle speichern“ (Strg+S)."); help.Margin = new Padding(0, 8, 28, 0); strip.Controls.Add(help);
        foreach (var b in new[] { _viewScan, _addScan, _clear }) { b.Margin = new Padding(0, 0, 8, 0); strip.Controls.Add(b); }
        _list.Controls.Add(_flow); _list.Controls.Add(strip);

        // Fußleiste: Speicherort links, Status in der Mitte, großer Speichern-Knopf rechts
        _bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); _bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _bar.Paint += (_, e) => { using var p = new Pen(Theme.Line); e.Graphics.DrawLine(p, 0, 0, _bar.Width, 0); };
        var folder = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
        var capRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
        _barFolderCaption.Margin = new Padding(0, 6, 4, 0); _changeFolder.Margin = new Padding(0); _changeFolder.MinimumSize = new Size(0, 24); _changeFolder.Height = 24; _changeFolder.Font = Theme.Font(9f); _changeFolder.ForeColor = Theme.Accent;
        capRow.Controls.Add(_barFolderCaption); capRow.Controls.Add(_changeFolder);
        _barFolder.Margin = new Padding(0, 0, 0, 0);
        folder.Controls.Add(capRow, 0, 0); folder.Controls.Add(_barFolder, 0, 1);
        _save.Anchor = AnchorStyles.Right; _save.Margin = Padding.Empty;
        _bar.Controls.Add(folder, 0, 0); _bar.Controls.Add(_barStatus, 1, 0); _bar.Controls.Add(_save, 2, 0);

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = Padding.Empty };
        content.Controls.Add(_list); content.Controls.Add(_empty);
        root.Controls.Add(header, 0, 0); root.Controls.Add(_notice, 0, 1); root.Controls.Add(content, 0, 2); root.Controls.Add(_bar, 0, 3);
        Controls.Add(root);
    }

    /// <summary>Ein Schritt im Leerzustand: Nummer, Titel, eine kurze Zeile – Details hinter dem „?“.</summary>
    Control Step(string n, string title, string line, string helpTitle, string help)
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(14, 0, 14, 12) };
        var badge = new StepBadge(n) { Margin = new Padding(0, 2, 12, 0) };
        var head = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
        head.Controls.Add(new Label { Text = title, Font = Theme.Font(12f, true), ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 2, 0, 0), UseMnemonic = false });
        var q = Theme.Help(helpTitle, help); q.Margin = new Padding(8, 4, 0, 0); head.Controls.Add(q);
        t.Controls.Add(badge, 0, 0); t.SetRowSpan(badge, 2);
        t.Controls.Add(head, 1, 0);
        t.Controls.Add(new Label { Text = line, Font = Theme.Font(9.5f), ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(190, 0), Margin = new Padding(0, 2, 0, 0), UseMnemonic = false }, 1, 1);
        return t;
    }

    void FitNoticeText()
    {
        // Basis ist die Fensterbreite (die Leiste selbst hat im unsichtbaren Zustand keine verlässliche Breite → Text wäre extrem hoch)
        int reserved = (_noticeAction.Visible ? _noticeAction.Width + 18 : 0) + _noticeClose.Width + _notice.Padding.Horizontal + 24;
        _noticeText.MaximumSize = new Size(Math.Max(200, ClientSize.Width - reserved), 0);
    }

    void Wire()
    {
        _scan.Click += async (_, __) => await DoScan();
        _addScan.Click += async (_, __) => await DoScan();
        _viewScan.Click += async (_, __) => await ShowScanView();
        _open.Click += async (_, __) =>
        {
            using var d = new OpenFileDialog { Multiselect = true, Title = "Scan(s) öffnen", Filter = "Bilder und PDF|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.pdf|Alle Dateien|*.*" };
            if (d.ShowDialog(this) == DialogResult.OK) await LoadFiles(d.FileNames);
        };
        _changeFolder.Click += (_, __) => PickFolder();
        _updateBtn.Click += async (_, __) => await InstallUpdate();
        _settingsBtn.Click += (_, __) => OpenSettings(0);
        _save.Click += async (_, __) => await DoSave();
        _clear.Click += (_, __) => { if (MessageBox.Show("Alle erkannten Ausweise verwerfen, ohne zu speichern?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) ClearCards(); };
        DragEnter += (_, e) => { if (e.Data!.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
        DragDrop += async (_, e) => { if (e.Data!.GetData(DataFormats.FileDrop) is string[] files) await LoadFiles(files); };
        foreach (var c in new Control[] { _empty, _inner, _flow, _list }) { c.AllowDrop = true; c.DragEnter += (_, e) => { if (e.Data!.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }; c.DragDrop += async (_, e) => { if (e.Data!.GetData(DataFormats.FileDrop) is string[] files) await LoadFiles(files); }; }
        KeyDown += async (_, e) => { if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; await DoSave(); } };
        _watchTimer.Tick += async (_, __) => await DrainWatchQueue();
        FormClosing += (_, e) =>
        {
            if (_uiTestOut == null && _cards.Count > 0 && MessageBox.Show("Es gibt noch nicht gespeicherte Ausweise. Trotzdem beenden?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { e.Cancel = true; return; }
            SaveSettings();
        };
    }

    // ------------------------------------------------------------------ Hilfen
    void Status(string t) { _emptyStatus.Text = t; _barStatus.Text = t; }

    /// <summary>Kompakte Hinweiszeile. Mit <paramref name="scan"/> erscheint zusätzlich „Im Scan prüfen“.</summary>
    void Notice(string text, bool show, ScanInfo? scan = null)
    {
        bool on = show && text.Length > 0;
        _noticeScan = on ? scan : null; _noticeAction.Visible = on && scan != null;
        FitNoticeText(); _noticeText.Text = text; _notice.Visible = on; _notice.PerformLayout();
    }

    /// <summary>Viele einzelne „Objekt bei x/y …“-Meldungen des Detektors zu einer Zeile zusammenfassen.</summary>
    static string Summarize(IEnumerable<string> warnings, out bool hasObjects)
    {
        var all = warnings.Distinct().ToList();
        var objs = all.Where(w => w.StartsWith("Objekt bei")).ToList();
        var rest = all.Except(objs).ToList(); hasObjects = objs.Count > 0;
        if (objs.Count > 0) rest.Insert(0, objs.Count == 1 ? "1 Objekt im Scan wurde nicht als Ausweis erkannt." : $"{objs.Count} Objekte im Scan wurden nicht als Ausweis erkannt (zu groß, zu schief oder berühren sich). Tipp: mit Abstand auflegen – oder fehlende Ausweise im Scan selbst markieren.");
        return string.Join("  ", rest);
    }

    void SetBusy(bool busy, string? msg = null)
    {
        _busy = busy;
        foreach (var c in new Control[] { _scan, _addScan, _viewScan, _open, _settingsBtn, _save, _clear, _changeFolder }) c.Enabled = !busy;
        UseWaitCursor = busy; if (msg != null) Status(msg);
    }

    internal void SaveSettings() { try { S.Save(Paths.SettingsFile); } catch (Exception e) { Status("Einstellungen konnten nicht gespeichert werden: " + e.Message); } }

    internal void ReloadProfiles(string? select)
    {
        _profileProblems = new List<string>(); Profiles = ProfileStore.Load(Paths.ProfilesDir, _profileProblems);
        var sel = Profiles.FirstOrDefault(p => string.Equals(p.Name, select, StringComparison.OrdinalIgnoreCase)) ?? Profiles[0];
        S.ProfileName = sel.Name;
        if (_profileProblems.Count > 0) Notice("Design-Dateien mit Fehlern übersprungen: " + string.Join("; ", _profileProblems), true);
    }

    Profile SelectedProfile => Profiles.FirstOrDefault(p => p.Name == S.ProfileName) ?? Profile.Auto();

    static string ShortPath(string p) => p.Length <= 40 ? p : "…" + p.Substring(p.Length - 37);

    void UpdateView()
    {
        bool has = _cards.Count > 0;
        _empty.Visible = !has; _list.Visible = has; _bar.Visible = has;
        _summary.Text = has ? (_cards.Count == 1 ? "1 Ausweis" : $"{_cards.Count} Ausweise") : "";
        _barFolder.Text = ShortPath(S.OutputDir) + "   (" + S.Format.ToUpperInvariant() + ")";
        _save.Text = _cards.Count == 0 ? "Alle speichern" : $"Alle speichern ({_cards.Count})";
        UpdateScannerInfo();
    }

    void UpdateScannerInfo()
    {
        if (_device != null) { _scannerInfo.Text = "Scanner: " + _device.Name; _scannerInfo.ForeColor = Theme.Muted; }
        else { _scannerInfo.Text = "Kein Scanner gefunden – Scan-Datei hierher ziehen, oder in den Einstellungen unter „Scanner“ suchen."; _scannerInfo.ForeColor = Theme.Warn; }
    }

    void PickFolder()
    {
        using var d = new FolderBrowserDialog { Description = "Ordner für die Ausweis-Dateien", SelectedPath = Directory.Exists(S.OutputDir) ? S.OutputDir : "" };
        if (d.ShowDialog(this) == DialogResult.OK) { S.OutputDir = d.SelectedPath; SaveSettings(); UpdateView(); }
    }

    // ------------------------------------------------------------------ Einstellungen
    void OpenSettings(int tab)
    {
        bool hadUpdates = S.CheckUpdates;
        using var f = new SettingsForm(this, tab);
        if (f.ShowDialog(this) != DialogResult.OK) { UpdateView(); return; }
        SaveSettings(); RestartWatcher(); UpdateView();
        if (!S.CheckUpdates) { _update = null; _updateBtn.Visible = false; }         // Schalter aus: kein Hinweis, keine Verbindung
        else if (!hadUpdates) _ = CheckForUpdate(false);                             // gerade eingeschaltet: einmal sofort prüfen
    }

    internal void SelectDevice(string id) { _device = Devices.FirstOrDefault(d => d.Id == id) ?? _device; if (_device != null) S.LastDevice = _device.Id; UpdateScannerInfo(); }
    internal ScanDevice? CurrentDevice => _device;

    internal async Task RefreshDevices(bool network)
    {
        bool was = _busy; if (!was) SetBusy(true, "Suche Scanner …");
        try
        {
            var notes = new List<string>(); var s = S;
            Devices = await Task.Run(() => ScanBackends.EnumerateAll(s, notes, network));
            DeviceNotes = notes;
            _device = Devices.FirstOrDefault(d => d.Id == S.LastDevice) ?? Devices.FirstOrDefault();
            Status(Devices.Count > 0 ? "" : "");
            UpdateScannerInfo();
        }
        finally { if (!was) SetBusy(false); }
    }

    internal string DiagText() => Diagnostics.Report(S, true);

    // ------------------------------------------------------------------ Updates
    /// <returns>Meldung für die Einstellungen; bei stiller Prüfung (Start) bleibt sie bei Fehlern leer.</returns>
    internal async Task<string> CheckForUpdate(bool manual)
    {
        try
        {
            var info = await Updater.CheckAsync();
            _update = info; _updateBtn.Visible = info != null;
            if (info == null) return $"Sie nutzen die neueste Version ({Updater.CurrentText}).";
            _updateBtn.Text = $"⬆  Update {info.Tag} verfügbar";
            return $"Version {info.Tag} ist verfügbar (installiert: {Updater.CurrentText}) – grüner Knopf oben rechts.";
        }
        catch (Exception e) { return manual ? "Update-Suche nicht möglich (keine Internetverbindung?): " + e.Message : ""; }
    }

    async Task InstallUpdate()
    {
        if (_update == null || _busy) return;
        if (_cards.Count > 0) { MessageBox.Show("Bitte zuerst die erkannten Ausweise speichern (oder die Liste leeren) – beim Update startet das Programm neu.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var notes = string.Join("\n", (_update.Notes ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim('#', '*', '`', ' ')).Where(l => l.Length > 0).Take(6));
        if (MessageBox.Show($"Neue Version {_update.Tag} ist verfügbar (installiert: {Updater.CurrentText}).\n\n{notes}\n\nJetzt herunterladen und installieren? Das Programm startet danach neu.\nDas Update wird per Prüfsumme auf Echtheit kontrolliert.", "Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SaveSettings(); SetBusy(true, "Lade Update …");
        try
        {
            var path = await Updater.DownloadAsync(_update, pct => BeginInvoke(new Action(() => Status($"Lade Update … {pct} %"))));
            Status("Installiere …");
            Updater.InstallAndRestart(path);   // startet die neue Version und beendet dieses Programm
        }
        catch (Exception e)
        {
            SetBusy(false); Status("");
            MessageBox.Show("Das Update konnte nicht installiert werden:\n\n" + e.Message + "\n\nSie können die neue Version auch von Hand herunterladen:\n" + _update.PageUrl, "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ------------------------------------------------------------------ Scannen / Laden
    async Task DoScan()
    {
        if (_busy) return;
        if (_device == null)
        {
            if (MessageBox.Show("Es wurde kein Scanner gefunden.\n\nTipps:\n• Scanner einschalten/anschließen und in den Einstellungen unter „Scanner“ neu suchen\n• oder mit der Scanner-Software scannen und die Datei hier ins Fenster ziehen\n\nEinstellungen jetzt öffnen?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) OpenSettings(1);
            return;
        }
        var dev = _device; bool dialog = S.UseDriverDialog; int dpi = S.Dpi;
        SetBusy(true, dialog ? "Scanner-Dialog geöffnet …" : "Scanne … bitte den Scanner nicht bewegen.");
        BitmapCodec.Loaded? loaded = null;
        try { loaded = await Task.Run(() => dev.Scan(new ScanRequest { Dpi = dpi, DriverDialog = dialog })); S.LastDevice = dev.Id; SaveSettings(); }
        catch (OperationCanceledException) { Status("Scan abgebrochen."); }
        catch (Exception e) { MessageBox.Show(ScanBackends.Friendly(e) + "\n\nTipp: In den Einstellungen unter „Scanner“ den Hersteller-Dialog aktivieren – oder mit der Scanner-Software scannen und die Datei hier ins Fenster ziehen.", "Scan fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Warning); Status("Scan fehlgeschlagen."); }
        finally { SetBusy(false); }
        if (loaded != null) await ProcessRaster(loaded.Image, DeviceDpi(loaded), "Scan " + DateTime.Now.ToString("HH:mm:ss"));
    }

    // Die angeforderte Auflösung ist verlässlicher als Metadaten; passt die Bildbreite nicht zu A4, wird die echte Auflösung berechnet.
    double DeviceDpi(BitmapCodec.Loaded l)
    {
        double fromWidth = l.Image.Width / (210 / 25.4);
        return Math.Abs(fromWidth - S.Dpi) / S.Dpi < 0.2 ? S.Dpi : Math.Round(fromWidth / 25) * 25;
    }

    async Task LoadFiles(IEnumerable<string> files)
    {
        if (_busy) return;
        foreach (var f in files.ToList())
        {
            var problems = new List<string>(); List<BitmapCodec.Loaded> loaded;
            try { loaded = await Task.Run(() => BitmapCodec.LoadFile(f, problems)); }
            catch (Exception e) { Notice($"„{Path.GetFileName(f)}“ konnte nicht gelesen werden: {e.Message}", true); continue; }
            if (problems.Count > 0) Notice(string.Join(" ", problems), true);
            for (int pi = 0; pi < loaded.Count; pi++)
            { var l = loaded[pi]; await ProcessRaster(l.Image, l.Dpi >= 100 && l.Dpi != 96 && l.Dpi != 72 ? l.Dpi : 0, loaded.Count > 1 ? $"{Path.GetFileName(f)} (Seite {pi + 1})" : Path.GetFileName(f)); }
        }
    }

    async Task ProcessRaster(Raster scan, double dpi, string name = "Scan")
    {
        if (_busy) return; SetBusy(true, "Suche Ausweise …");
        PipelineResult? res = null;
        try
        {
            var profile = SelectedProfile; var ocr = _ocr; var s = S; var profs = Profiles;
            res = await Task.Run(() => Pipeline.Process(scan, dpi, s, profs, profile, ocr, m => BeginInvoke(new Action(() => Status(m)))));
        }
        catch (Exception e) { MessageBox.Show("Auswertung fehlgeschlagen:\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
        if (res == null) return;
        if (res.Scan != null) res.Scan.Name = name;
        AddCards(res.Cards);
        var msg = Summarize(res.Warnings, out var hasObjects);
        Notice(msg, msg.Length > 0, hasObjects ? res.Scan : null);
        Status(res.Cards.Count == 0 ? "Keine Ausweise erkannt." : "");
        var first = _cards.Where(c => c.Item.Score < 0.7).FirstOrDefault() ?? _cards.FirstOrDefault();
        first?.FocusLast();
        // Nichts erkannt: gleich den ganzen Scan zeigen, damit die Ausweise per Rahmen markiert werden können
        if (res.Cards.Count == 0 && res.Scan != null && _uiTestOut == null) await OpenScanView(res.Scan, "Es wurde kein Ausweis automatisch erkannt – bitte per Rahmen markieren.");
    }

    // ------------------------------------------------------------------ Gesamter Scan
    async Task ShowScanView()
    {
        var scans = _cards.Select(c => c.Item.Scan).Where(x => x != null).Distinct().Cast<ScanInfo>().ToList();
        if (scans.Count == 0) return;
        await OpenScanView(scans, 0, null);   // bei mehreren Dateien: Blättern mit ◀ ▶ im Dialog
    }

    /// <summary>Große Ansicht eines Ausweises: Rahmen direkt dort anpassen (mit Lupe). Die Kachel wird danach an derselben Stelle neu ausgeschnitten.</summary>
    async Task AdjustCrop(CardControl c)
    {
        var scan = c.Item.Scan; if (scan == null || _busy) return;
        var quad = scan.Quads.FirstOrDefault(q => q.Item == c.Item); if (quad == null) return;
        using var f = new CropEditorForm(scan, quad.Corners, S.EdgeInsetMm);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        int idx = _cards.IndexOf(c); var item = c.Item;
        foreach (var q in scan.Quads) q.Include = q.Item != null;
        quad.Corners = f.Result; quad.Dirty = true;
        await ApplyScanChanges(scan);
        var nc = quad.Item == null ? null : _cards.FirstOrDefault(x => x.Item == quad.Item);
        if (nc != null && idx >= 0 && idx < _cards.Count - 1)   // an die alte Stelle der Liste zurück
        { _cards.Remove(nc); _cards.Insert(idx, nc); _flow.Controls.SetChildIndex(nc, idx); Renumber(); }
        nc?.FocusLast();
    }

    Task OpenScanView(ScanInfo scan, string? note, CardItem? select = null)
    {
        var all = _cards.Select(c => c.Item.Scan).Where(x => x != null).Distinct().Cast<ScanInfo>().ToList();
        if (!all.Contains(scan)) all.Insert(0, scan);   // z. B. frischer Scan ohne erkannte Ausweise
        return OpenScanView(all, all.IndexOf(scan), note, select);
    }

    async Task OpenScanView(List<ScanInfo> scans, int start, string? note, CardItem? select = null)
    {
        if (_busy) return;
        using var f = new ScanViewForm(scans, start, note, S.EdgeInsetMm, select);
        if (f.ShowDialog(this) != DialogResult.OK) { UpdateView(); return; }
        foreach (var (scan, quads) in f.Results.ToList()) { scan.Quads = quads; await ApplyScanChanges(scan); }
    }

    /// <summary>Stand der Rahmen (Include/Dirty/Removed) in die Kachelliste übernehmen.</summary>
    async Task ApplyScanChanges(ScanInfo scan)
    {
        foreach (var q in scan.Quads.Where(q => !q.Include && q.Item != null).ToList())
        { var c = _cards.FirstOrDefault(x => x.Item == q.Item); if (c != null) RemoveCard(c); q.Item = null; }
        // Ecken verändert → alte Kachel verwerfen, neu ausschneiden und lesen; von Hand eingetippte Namen bleiben erhalten
        var typed = new Dictionary<ScanQuad, (string last, string first)>();
        foreach (var q in scan.Quads.Where(q => q.Include && q.Item != null && q.Dirty).ToList())
        {
            if (q.Item!.Source == "von Hand") typed[q] = (q.Item.Last, q.Item.First);
            var c = _cards.FirstOrDefault(x => x.Item == q.Item); if (c != null) RemoveCard(c); q.Item = null;
        }
        foreach (var q in scan.Quads) q.Dirty = false;
        scan.Quads.RemoveAll(q => q.Removed);   // gelöschte Rahmen gibt es nicht mehr (ihre Kacheln sind oben schon entfernt)
        var add = scan.Quads.Where(q => q.Include && q.Item == null).ToList();
        if (add.Count > 0)
        {
            SetBusy(true, "Lese Ausweise …");
            try
            {
                var profile = SelectedProfile; var ocr = _ocr; var profs = Profiles;
                var items = await Task.Run(() => add.Select(q => Pipeline.FromCorners(scan, q.Corners, profs, profile, ocr)).ToList());
                for (int i = 0; i < add.Count; i++)
                {
                    add[i].Item = items[i];
                    if (typed.TryGetValue(add[i], out var t)) { items[i].Last = t.last; items[i].First = t.first; items[i].Source = "von Hand"; items[i].Score = string.IsNullOrWhiteSpace(t.last) ? 0 : 0.99; }
                }
                AddCards(items);
                (_cards.FirstOrDefault(c => items.Contains(c.Item) && c.Item.Score < 0.7) ?? _cards.FirstOrDefault(c => items.Contains(c.Item)))?.FocusLast();
            }
            catch (Exception e) { MessageBox.Show("Auswertung fehlgeschlagen:\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { SetBusy(false); Status(""); }
        }
        UpdateView();
    }

    // ------------------------------------------------------------------ Karten
    void AddCards(IEnumerable<CardItem> items)
    {
        _flow.SuspendLayout();
        foreach (var it in items)
        {
            var c = new CardControl(it, _cards.Count + 1);
            c.RemoveRequested += RemoveCard; c.CropRequested += async x => await AdjustCrop(x);
            c.PreviewRequested += async x =>
            {
                if (x.Item.Scan != null) { await AdjustCrop(x); return; }   // große Ansicht = Zuschnitt direkt dort anpassen
                using var f = new PreviewForm(x.Item.Image, "Ausweis – Großansicht (Esc schließt)"); f.ShowDialog(this);
            };
            c.RotateRequested += async (x, t) => await RotateCard(x, t);
            c.EnterOnLast += x => { var i = _cards.IndexOf(x); if (i + 1 < _cards.Count) _cards[i + 1].FocusLast(); else _save.Focus(); };
            c.NameEdited += MarkDuplicates;
            _cards.Add(c); _flow.Controls.Add(c);
        }
        _flow.ResumeLayout(); Renumber(); MarkDuplicates(); UpdateView();
    }

    void RemoveCard(CardControl c)
    {
        if (c.Item.Scan != null) foreach (var q in c.Item.Scan.Quads) if (q.Item == c.Item) q.Item = null;   // im Gesamtscan wieder „nicht ausgewählt“
        _cards.Remove(c); _flow.Controls.Remove(c); c.Dispose(); Renumber(); MarkDuplicates(); UpdateView();
    }

    void ClearCards() { foreach (var c in _cards) { _flow.Controls.Remove(c); c.Dispose(); } _cards.Clear(); Notice("", false); Status(""); UpdateView(); }

    void Renumber() { for (int i = 0; i < _cards.Count; i++) _cards[i].SetNumber(i + 1); }

    void MarkDuplicates()
    {
        var groups = _cards.Where(c => !string.IsNullOrWhiteSpace(c.Item.Last)).GroupBy(c => (c.Item.Last.Trim().ToLowerInvariant(), c.Item.First.Trim().ToLowerInvariant()));
        foreach (var c in _cards) c.MarkDuplicate(false);
        foreach (var g in groups.Where(g => g.Count() > 1)) foreach (var c in g) c.MarkDuplicate(true);
    }

    async Task RotateCard(CardControl c, int turns)
    {
        if (_busy) return;
        c.Item.Image = c.Item.Image.Rotate(turns);
        if (_ocr != null)
        {
            SetBusy(true, "Lese Namen neu …");
            try { var item = c.Item; var ocr = _ocr; var profile = SelectedProfile; var profs = Profiles; await Task.Run(() => Relocator.Run(item, profs, profile, ocr)); }
            catch (Exception e) { Notice("Texterkennung fehlgeschlagen: " + e.Message, true); }
            finally { SetBusy(false); }
        }
        c.RefreshFromItem(); MarkDuplicates(); Status("");
    }

    // ------------------------------------------------------------------ Speichern
    async Task DoSave()
    {
        if (_busy || _cards.Count == 0) return;
        var dir = S.OutputDir.Trim();
        if (dir.Length == 0) { PickFolder(); return; }
        int unnamed = _cards.Count(c => string.IsNullOrWhiteSpace(c.Item.Last));
        if (unnamed > 0)
        {
            var a = MessageBox.Show($"{unnamed} Ausweis(e) haben keinen Namen und würden als „Unbekannt“ gespeichert.\n\nJa = trotzdem speichern\nNein = zurück und Namen eintippen", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (a != DialogResult.Yes) { _cards.First(c => string.IsNullOrWhiteSpace(c.Item.Last)).FocusLast(); return; }
        }
        SaveSettings(); SetBusy(true, "Speichere …");
        SaveResult? r = null;
        try { var items = _cards.Select(c => c.Item).ToList(); var s = S; r = await Task.Run(() => Saver.Save(items, s, dir)); }
        catch (Exception e) { MessageBox.Show("Speichern fehlgeschlagen:\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
        if (r == null) return;
        if (r.Errors.Count > 0) MessageBox.Show($"{r.Written.Count} gespeichert, {r.Errors.Count} Fehler:\n\n" + string.Join("\n", r.Errors.Distinct()) + "\n\nDie Ausweise bleiben in der Liste.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        else
        {
            ClearCards();
            Status($"{r.Written.Count} Dateien gespeichert.");
            if (MessageBox.Show($"{r.Written.Count} Dateien gespeichert in\n{dir}\n\nOrdner jetzt öffnen?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            { try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { } }
        }
    }

    // ------------------------------------------------------------------ Design anlernen (aus den Einstellungen aufgerufen)
    internal void TeachDesign(IWin32Window owner)
    {
        // Beispielkarten: die schon erkannten Ausweise der Liste (bis 8 – je mehr, desto besser lässt sich das Design prüfen); sonst eine Datei wählen
        var samples = _cards.Take(8).Select(c => c.Item.Image).ToList();
        if (samples.Count == 0)
        {
            using var d = new OpenFileDialog { Title = "Foto oder Scan von Beispielkarten wählen", Multiselect = true, Filter = "Bilder|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|Alle Dateien|*.*" };
            if (d.ShowDialog(owner) != DialogResult.OK) return;
            try
            {
                foreach (var fn in d.FileNames.Take(8))
                {
                    var l = BitmapCodec.LoadFile(fn)[0];
                    var det = CardDetector.Detect(l.Image, new DetectorOptions { Dpi = l.Dpi >= 100 && l.Dpi != 96 ? l.Dpi : Math.Max(100, Pipeline.EstimateDpiFromPage(l.Image)) });
                    if (det.Cards.Count > 0) foreach (var dc in det.Cards.Take(8 - samples.Count)) samples.Add(CardExtractor.Extract(l.Image, dc));   // mehrere Karten in einem Scan → alle als Beispiele
                    else samples.Add(l.Image);                                                                                                         // kein Rahmen erkannt → Bild gilt als Karte
                    if (samples.Count >= 8) break;
                }
            }
            catch (Exception e) { MessageBox.Show("Bild konnte nicht gelesen werden: " + e.Message, Text); return; }
        }
        using var ed = new ProfileEditor(samples, _ocr, null);
        if (ed.ShowDialog(owner) != DialogResult.OK || ed.Result == null) return;
        try { ProfileStore.Save(Paths.ProfilesDir, ed.Result); ReloadProfiles(ed.Result.Name); }
        catch (Exception e) { MessageBox.Show("Design konnte nicht gespeichert werden: " + e.Message, Text); }
    }

    // ------------------------------------------------------------------ Ordner überwachen
    void RestartWatcher()
    {
        _watcher?.Dispose(); _watcher = null; _watchTimer.Stop();
        if (string.IsNullOrWhiteSpace(S.WatchFolder)) return;
        try
        {
            if (!Directory.Exists(S.WatchFolder)) { Notice("Überwachter Ordner existiert nicht: " + S.WatchFolder, true); return; }
            _watcher = new FileSystemWatcher(S.WatchFolder) { EnableRaisingEvents = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            FileSystemEventHandler h = (_, e) => { if (IsScanFile(e.FullPath)) _watchQueue.Enqueue(e.FullPath); };
            _watcher.Created += h; _watcher.Renamed += (_, e) => { if (IsScanFile(e.FullPath)) _watchQueue.Enqueue(e.FullPath); };
            _watchTimer.Start();
        }
        catch (Exception e) { Notice("Ordnerüberwachung nicht möglich: " + e.Message, true); }
    }

    static bool IsScanFile(string p) => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".pdf" }.Contains(Path.GetExtension(p).ToLowerInvariant());

    async Task DrainWatchQueue()
    {
        if (_busy || _watchQueue.IsEmpty) return;
        var ready = new List<string>();
        while (_watchQueue.TryDequeue(out var p)) if (File.Exists(p) && !ready.Contains(p)) ready.Add(p);
        var stable = new List<string>(); var later = new List<string>();
        foreach (var p in ready) { try { using (File.Open(p, FileMode.Open, FileAccess.Read, FileShare.None)) { } stable.Add(p); } catch (IOException) { later.Add(p); } }
        foreach (var p in later) _watchQueue.Enqueue(p);   // Scanner schreibt noch → nächster Takt
        if (stable.Count > 0) await LoadFiles(stable);
    }

    // ------------------------------------------------------------------ UI-Test (CI): Fenster, Design-Editor und Einstellungen als PNG
    async Task UiTestCapture()
    {
        await Task.Delay(800); Application.DoEvents();
        // Schmales Fenster testen (Antwortfähigkeit): AUSWEIS_UI_SIZE=760x640
        var sz = Environment.GetEnvironmentVariable("AUSWEIS_UI_SIZE")?.Split('x');
        if (sz != null && sz.Length == 2 && int.TryParse(sz[0], out var sw) && int.TryParse(sz[1], out var sh)) { WindowState = FormWindowState.Normal; Size = new Size(sw, sh); await Task.Delay(500); Application.DoEvents(); }
        void Snap(Control c, string path) { using var b = new Bitmap(c.Width, c.Height); c.DrawToBitmap(b, new Rectangle(Point.Empty, c.Size)); b.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
        var baseName = Path.ChangeExtension(_uiTestOut!, null);
        if (_cards.Count > 0)   // Worst Case der Hinweiszeile: viele Detektor-Meldungen
        {
            var fake = Enumerable.Range(1, 8).Select(i => $"Objekt bei x≈{i * 211}, y≈{i * 317} nicht rechteckig genug (50 %) – Karten mit etwas Abstand auflegen.");
            Notice(Summarize(fake, out _) + "  Auflösung des Bildes unklar – mit 300 dpi ausgewertet.", true, _cards[0].Item.Scan);
            await Task.Delay(300); Application.DoEvents();
        }
        Snap(this, _uiTestOut!);
        if (_cards.Count > 0)
        {
            var prof = new Profile { Name = "Test-Design" };
            prof.Fields.Add(new FieldRegion { Role = "name", X = 0.32, Y = 0.36, W = 0.62, H = 0.14 });
            using var ed = new ProfileEditor(_cards.Take(3).Select(c => c.Item.Image).ToList(), _ocr, prof) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
            ed.Show(); await Task.Delay(500); Application.DoEvents();
            await ed.DemoSuggest(); await Task.Delay(1500); Application.DoEvents();   // OCR-Kästen + automatischer Vorschlag + Test
            Snap(ed, baseName + "-editor.png");
        }
        if (_cards.Count > 0 && _cards[0].Item.Scan != null)
        {
            using var sv = new ScanViewForm(_cards[0].Item.Scan!, null, S.EdgeInsetMm) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
            sv.Show(); await Task.Delay(500); Application.DoEvents();
            Snap(sv, baseName + "-scanview.png");
            sv.DemoRegion(); await Task.Delay(300); Application.DoEvents();
            Snap(sv, baseName + "-scanview-fit.png");
            var second = new ScanInfo { Image = _cards[0].Item.Scan!.Image, Dpi = _cards[0].Item.Scan!.Dpi, Name = "zweite-datei.png" };   // Blättern zwischen mehreren Dateien
            _cards[0].Item.Scan!.Name = "scan-seite-1.png";
            using (var mv = new ScanViewForm(new[] { _cards[0].Item.Scan!, second }, 0, null, S.EdgeInsetMm) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) })
            { mv.Show(); await Task.Delay(400); Application.DoEvents(); Snap(mv, baseName + "-scanview-multi.png"); }
            var q0 = _cards[0].Item.Scan!.Quads.First(q => q.Item == _cards[0].Item);
            using var ce = new CropEditorForm(_cards[0].Item.Scan!, q0.Corners, S.EdgeInsetMm) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
            ce.Show(); await Task.Delay(400); Application.DoEvents(); ce.DemoLoupe(); await Task.Delay(300); Application.DoEvents();
            Snap(ce, baseName + "-crop.png");
        }
        using var st = new SettingsForm(this, 0) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
        st.Show();
        for (int i = 0; i < st.TabCount; i++) { st.SelectTab(i); await Task.Delay(i == st.TabCount - 1 ? 4000 : 400); Application.DoEvents(); Snap(st, baseName + "-settings-" + i + ".png"); }
        _cards.Clear(); Close();
    }
}
