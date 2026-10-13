using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>Gemeinsame Bausteine der Dialoge: Kopfzeile mit „?“, Fußleiste mit Hauptknöpfen – antwortfähig (wächst mit dem Inhalt, nichts überlappt).</summary>
static class Dialogs
{
    public static Control TopBar(Label info, Label? hint, Control help)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2, BackColor = Theme.Surface, Padding = new Padding(18, 12, 18, 10), MinimumSize = new Size(0, 52) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        help.Anchor = AnchorStyles.Left; help.Margin = new Padding(10, 0, 0, 0);
        t.Controls.Add(info, 0, 0); t.Controls.Add(help, 1, 0);
        if (hint != null) { hint.Margin = new Padding(0, 4, 0, 0); t.Controls.Add(hint, 0, 1); t.SetColumnSpan(hint, 2); }
        t.Paint += (_, e) => { using var p = new Pen(Theme.Line); e.Graphics.DrawLine(p, 0, t.Height - 1, t.Width, t.Height - 1); };
        return t;
    }

    /// <summary>Fußleiste: links frei für weitere Knöpfe, rechts „Abbrechen“ + Hauptknopf. Setzt AcceptButton/CancelButton des Formulars.</summary>
    public static Panel BottomBar(out Button ok, out Button cancel, Form form, string okText, out FlowLayoutPanel left)
    {
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Surface };
        bar.Paint += (_, e) => { using var p = new Pen(Theme.Line); e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
        ok = Theme.Primary(okText, 170, 42, Theme.Ok); cancel = Theme.Ghost("Abbrechen", 110, 42);
        ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel; form.AcceptButton = ok; form.CancelButton = cancel;
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 11, 16, 0), BackColor = Color.Transparent };
        ok.Margin = new Padding(6, 0, 0, 0); right.Controls.Add(ok); right.Controls.Add(cancel);
        left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(14, 11, 0, 0), BackColor = Color.Transparent };
        bar.Controls.Add(right); bar.Controls.Add(left);
        return bar;
    }
}

/// <summary>Bild mit Maus-Rahmen und anklickbaren Textzeilen: liefert gezogene Bereiche bzw. angeklickte Zeilen als Anteile (0..1) der Karte.</summary>
sealed class ImagePanel : Control
{
    Bitmap? _bmp; Point? _start; Rectangle _drag; int _hover = -1;
    public List<(string role, RectangleF frac)> Fields = new List<(string, RectangleF)>();
    /// <summary>Von der Texterkennung gefundene Zeilen (anklickbar).</summary>
    public List<(string text, RectangleF frac)> Boxes = new List<(string, RectangleF)>();
    public event Action<RectangleF>? RegionSelected;
    public event Action<int>? BoxClicked;

    public ImagePanel() { DoubleBuffered = true; BackColor = Color.FromArgb(52, 56, 64); Cursor = Cursors.Cross; }
    public void SetImage(Bitmap b) { _bmp = b; Boxes = new List<(string, RectangleF)>(); _hover = -1; Invalidate(); }

    RectangleF ImageRect()
    {
        if (_bmp == null) return RectangleF.Empty;
        float k = Math.Min((float)(Width - 20) / _bmp.Width, (float)(Height - 20) / _bmp.Height);
        float w = _bmp.Width * k, h = _bmp.Height * k;
        return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
    }

    RectangleF ToView(RectangleF f, RectangleF r) => new RectangleF(r.X + f.X * r.Width, r.Y + f.Y * r.Height, f.Width * r.Width, f.Height * r.Height);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (_bmp == null) return;
        var g = e.Graphics; g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = ImageRect(); g.DrawImage(_bmp, r);
        for (int i = 0; i < Boxes.Count; i++)
        {
            var v = ToView(Boxes[i].frac, r); bool hov = i == _hover;
            using (var fill = new SolidBrush(Color.FromArgb(hov ? 70 : 22, Theme.Accent))) g.FillRectangle(fill, v);
            using (var pen = new Pen(Color.FromArgb(hov ? 255 : 150, Theme.Accent), hov ? 2.2f : 1.2f) { DashStyle = hov ? System.Drawing.Drawing2D.DashStyle.Solid : System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawRectangle(pen, v.X, v.Y, v.Width, v.Height);
        }
        foreach (var f in Fields)
        {
            var fr = ToView(f.frac, r); Color c = f.role == "first" ? Color.DeepSkyBlue : f.role == "last" ? Color.Orange : Color.LimeGreen;
            using var pen = new Pen(c, 3); g.DrawRectangle(pen, fr.X, fr.Y, fr.Width, fr.Height);
            using var b = new SolidBrush(c); using var ft = Theme.Font(9f, true);
            g.DrawString(f.role == "first" ? "Vorname" : f.role == "last" ? "Nachname" : "Name", ft, b, fr.X + 3, Math.Max(0, fr.Y - 18));
        }
        if (_start.HasValue) using (var pen = new Pen(Color.Gold, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawRectangle(pen, _drag);
    }

    int HitBox(Point p)
    {
        var r = ImageRect(); int best = -1; double bestArea = double.MaxValue;
        for (int i = 0; i < Boxes.Count; i++) { var v = ToView(Boxes[i].frac, r); if (v.Contains(p) && v.Width * v.Height < bestArea) { bestArea = v.Width * v.Height; best = i; } }
        return best;
    }

    protected override void OnMouseDown(MouseEventArgs e) { _start = e.Location; _drag = new Rectangle(e.Location, Size.Empty); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_start.HasValue) { int h = HitBox(e.Location); if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Cross; Invalidate(); } return; }
        _drag = Rectangle.FromLTRB(Math.Min(_start.Value.X, e.X), Math.Min(_start.Value.Y, e.Y), Math.Max(_start.Value.X, e.X), Math.Max(_start.Value.Y, e.Y)); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_start.HasValue) return;
        var d = _drag; var s0 = _start.Value; _start = null; Invalidate();
        if (Math.Abs(e.X - s0.X) < 6 && Math.Abs(e.Y - s0.Y) < 6) { int h = HitBox(e.Location); if (h >= 0) BoxClicked?.Invoke(h); return; }   // Klick auf eine erkannte Zeile
        var r = ImageRect(); if (r.IsEmpty || d.Width < 8 || d.Height < 6) return;
        float x0 = Math.Max(r.X, d.X), y0 = Math.Max(r.Y, d.Y), x1 = Math.Min(r.Right, d.Right), y1 = Math.Min(r.Bottom, d.Bottom);
        if (x1 - x0 < 6 || y1 - y0 < 4) return;
        RegionSelected?.Invoke(new RectangleF((x0 - r.X) / r.Width, (y0 - r.Y) / r.Height, (x1 - x0) / r.Width, (y1 - y0) / r.Height));
    }
}

/// <summary>
/// Neues Ausweis-Design „anlernen“: Beispielkarte(n) zeigen, Name per Klick auf die erkannte Zeile (oder Rahmen ziehen) markieren,
/// am besten gleich automatisch vorschlagen lassen und an allen Beispielkarten prüfen → Profil als JSON speichern.
/// </summary>
sealed class ProfileEditor : Form
{
    readonly List<Raster> _samples; readonly IOcrEngine? _ocr; int _idx;
    readonly ImagePanel _panel = new ImagePanel();
    readonly TextBox _name = new TextBox { Font = Theme.Font(10.5f) };
    readonly ComboBox _format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, _orient = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _sizeSel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly NumericUpDown _sw = new NumericUpDown { Minimum = 30, Maximum = 160, DecimalPlaces = 1, Increment = 0.1M, Width = 78, Value = 85.6M }, _sh = new NumericUpDown { Minimum = 30, Maximum = 160, DecimalPlaces = 1, Increment = 0.1M, Width = 78, Value = 54.0M };
    readonly CheckBox _copy = new CheckBox { Text = "Als neues Design speichern (Original bleibt)", AutoSize = true };
    readonly bool _editing;
    public bool SaveAsCopy => _editing && _copy.Checked;
    readonly RadioButton _rName = new RadioButton { Text = "Name komplett (Vor- + Nachname in einer Zeile)", Checked = true, AutoSize = true };
    readonly RadioButton _rLast = new RadioButton { Text = "Nur Nachname", AutoSize = true }, _rFirst = new RadioButton { Text = "Nur Vorname", AutoSize = true };
    readonly Label _result = new Label { AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Theme.Muted, UseMnemonic = false }, _nav = new Label { AutoSize = true, ForeColor = Theme.Muted, UseMnemonic = false };
    readonly Label _info = new Label { AutoSize = true, Font = Theme.Font(11f, true), ForeColor = Theme.Text, BackColor = Color.Transparent, UseMnemonic = false, Anchor = AnchorStyles.Left, Text = "Auf der Karte den Namen markieren" };
    // (Titel wird für das Bearbeiten im Konstruktor angepasst)
    readonly Button _prev = Theme.Outline("◀", 44, 34), _next = Theme.Outline("▶", 44, 34);
    List<LayoutLine> _layout = new List<LayoutLine>();
    readonly Profile _p;
    public Profile? Result { get; private set; }

    public ProfileEditor(IList<Raster> samples, IOcrEngine? ocr, Profile? existing)
    {
        _samples = samples.ToList(); _ocr = ocr; _p = existing ?? new Profile { Name = "Neues Design" }; _editing = existing != null;
        Icon = Theme.AppIcon; Text = _editing ? "Ausweis-Design bearbeiten" : "Ausweis-Design anlernen"; Size = new Size(1100, 720); StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(820, 560); Font = Theme.Font(9.5f); BackColor = Theme.Surface;

        var top = Dialogs.TopBar(_info, null, Theme.Help("Design anlernen", "1. Auf der Karte links die Zeile anklicken, in der der Name steht (gestrichelte Kästen = erkannter Text). Oder mit der Maus einen Rahmen um das Feld ziehen.\n\n2. „Automatisch vorschlagen“ erledigt das meist schon für dich.\n\n3. „An allen Beispielen testen“ zeigt, ob der Name auch auf den anderen Karten gelesen wird. Gib dafür mehrere Beispielkarten des gleichen Designs an (z. B. aus der Liste)."));
        var bar = Dialogs.BottomBar(out var ok, out var cancel, this, "Speichern", out _);
        ok.DialogResult = DialogResult.None; ok.Click += (_, __) => Save();

        var outer = new Panel { Dock = DockStyle.Right, Width = 380, BackColor = Theme.Surface };
        var right = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16, 8, 12, 8), BackColor = Theme.Surface };
        outer.Controls.Add(right);
        _panel.Dock = DockStyle.Fill; ShowSample(0);
        Controls.Add(_panel); Controls.Add(outer); Controls.Add(top); Controls.Add(bar);

        Control Head(string t, string? ht = null, string? h = null)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 4), BackColor = Color.Transparent };
            f.Controls.Add(new Label { Text = t, Font = Theme.Font(10.5f, true), ForeColor = Theme.Text, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, 2, 0, 0) });
            if (h != null) { var q = Theme.Help(ht ?? t, h); q.Margin = new Padding(8, 0, 0, 0); f.Controls.Add(q); }
            return f;
        }
        void Add(Control c, int w = 340) { c.Margin = new Padding(0, 0, 0, 6); if (!(c is CheckBox) && !(c is RadioButton) && !(c is Label)) c.Width = w; right.Controls.Add(c); }

        if (_samples.Count > 1)
        {
            var nav = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 0) };
            _prev.Margin = new Padding(0, 0, 4, 0); _next.Margin = new Padding(0, 0, 10, 0); _nav.Margin = new Padding(0, 8, 0, 0);
            _prev.Click += (_, __) => ShowSample(_idx - 1); _next.Click += (_, __) => ShowSample(_idx + 1);
            nav.Controls.Add(_prev); nav.Controls.Add(_next); nav.Controls.Add(_nav); right.Controls.Add(nav);
        }
        var sug = Theme.Primary("✨  Automatisch vorschlagen", 300, 38);
        sug.Click += async (_, __) => await DoSuggest(); sug.Margin = new Padding(0, 10, 0, 4); right.Controls.Add(sug);
        right.Controls.Add(Head("Was markieren?", "Was markieren?", "Klicke eine erkannte Zeile an oder ziehe einen Rahmen. Der neue Rahmen ersetzt den alten derselben Art. Steht „Name: Mustermann, Max“ in einer Zeile, nimm „Name komplett“; stehen Nachname und Vorname in getrennten Zeilen, markiere beide nacheinander."));
        Add(_rName); Add(_rLast); Add(_rFirst);
        right.Controls.Add(Head("Name des Designs"));
        _name.Text = _p.Name; Add(_name);
        if (_editing) { _info.Text = "Design bearbeiten: „" + _p.Name + "“"; Add(_copy); }
        right.Controls.Add(Head("Schreibweise", "Schreibweise auf der Karte", "Wie der Name auf der Karte steht. „Automatisch“ erkennt meist von selbst, ob zuerst der Nach- oder der Vorname kommt."));
        _format.Items.AddRange(new object[] { "automatisch erkennen", "Nachname, Vorname", "Vorname Nachname", "NACHNAME Vorname (ohne Komma)" }); _format.SelectedIndex = _p.Format == NameFormat.LastCommaFirst ? 1 : _p.Format == NameFormat.FirstLast ? 2 : _p.Format == NameFormat.LastFirst ? 3 : 0; Add(_format);
        right.Controls.Add(Head("Textrichtung"));
        _orient.Items.AddRange(new object[] { "Querformat (normal)", "Hochformat (Text steht aufrecht)" }); _orient.SelectedIndex = _p.Orientation == "portrait" ? 1 : 0; Add(_orient);
        right.Controls.Add(Head("Kartengröße", "Kartengröße", "„Wie in den Einstellungen“ nutzt die allgemeine Kartengröße (Einstellungen → Ausweis-Design) und ist für die meisten Designs richtig.\n\nMit eigener Größe für dieses Design wird der Rahmen exakt darauf eingepasst; Karten, die mehr als 20 % abweichen, werden mit diesem Design ignoriert."));
        _sizeSel.Items.AddRange(new object[] { "Wie in den Einstellungen (empfohlen)", "Scheckkarte 85,6 × 54 mm", "Eigene Maße" });
        bool id1 = Math.Abs(_p.CardWidthMm - CardSizes.Id1Width) < 0.05 && Math.Abs(_p.CardHeightMm - CardSizes.Id1Height) < 0.05;
        _sizeSel.SelectedIndex = _p.CardWidthMm <= 0 ? 0 : id1 ? 1 : 2; if (_p.CardWidthMm > 0 && !id1) { _sw.Value = (decimal)Math.Max(30, Math.Min(160, _p.CardWidthMm)); _sh.Value = (decimal)Math.Max(30, Math.Min(160, _p.CardHeightMm)); }
        Add(_sizeSel);
        var dims = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 6) };
        _sw.Margin = new Padding(0, 0, 4, 0); _sh.Margin = new Padding(4, 0, 4, 0); dims.Controls.Add(_sw); dims.Controls.Add(new Label { Text = "×", AutoSize = true, Margin = new Padding(0, 4, 0, 0) }); dims.Controls.Add(_sh); dims.Controls.Add(new Label { Text = "mm", AutoSize = true, Margin = new Padding(2, 4, 0, 0) });
        right.Controls.Add(dims); _sizeSel.SelectedIndexChanged += (_, __) => dims.Visible = _sizeSel.SelectedIndex == 2; dims.Visible = _sizeSel.SelectedIndex == 2;
        var test = Theme.Outline("An allen Beispielen testen", 300, 36); test.Click += async (_, __) => await TestAll(); test.Margin = new Padding(0, 10, 0, 4); right.Controls.Add(test);
        right.Controls.Add(_result);

        foreach (var f in _p.Fields) _panel.Fields.Add((f.Role, new RectangleF((float)f.X, (float)f.Y, (float)f.W, (float)f.H)));
        _panel.RegionSelected += r => Assign(r);
        _panel.BoxClicked += i => { if (i < _layout.Count) { var rg = DesignLearner.RegionFor(_layout[i], ""); Assign(new RectangleF((float)rg.X, (float)rg.Y, (float)rg.W, (float)rg.H)); } };
        Theme.Fit(this);
        Shown += async (_, __) => await LoadLayout();
    }

    internal async System.Threading.Tasks.Task DemoSuggest() { await LoadLayout(); await DoSuggest(); }   // nur UI-Test

    string CurrentRole => _rLast.Checked ? "last" : _rFirst.Checked ? "first" : "name";

    void Assign(RectangleF r)
    {
        string role = CurrentRole;
        _panel.Fields.RemoveAll(f => f.role == role || (role == "name" && f.role != "name") || (role != "name" && f.role == "name"));
        _panel.Fields.Add((role, r)); _panel.Invalidate();
        if (role == "last" && !_panel.Fields.Any(f => f.role == "first")) _rFirst.Checked = true;      // nach dem Nachnamen liegt der Vorname nahe
        _ = TestAll();
    }

    void ShowSample(int idx)
    {
        _idx = (idx + _samples.Count) % _samples.Count;
        _panel.SetImage(BitmapCodec.ToBitmap(_samples[_idx]));
        _nav.Text = $"Beispiel {_idx + 1} von {_samples.Count}";
        _layout = new List<LayoutLine>();
        if (IsHandleCreated) _ = LoadLayout();
    }

    async System.Threading.Tasks.Task LoadLayout()
    {
        if (_ocr == null) { _result.Text = "Keine Texterkennung verfügbar – bitte Rahmen von Hand ziehen. Das Design kann trotzdem gespeichert werden."; return; }
        int mine = _idx; var sample = _samples[mine]; var ocr = _ocr;
        try
        {
            var lines = await System.Threading.Tasks.Task.Run(() => DesignLearner.ReadLayout(sample, ocr));
            if (mine != _idx) return;
            _layout = lines; _panel.Boxes = lines.Select(l => (l.Text, new RectangleF((float)l.Frac.X, (float)l.Frac.Y, (float)l.Frac.W, (float)l.Frac.H))).ToList(); _panel.Invalidate();
        }
        catch (Exception e) { _result.Text = "Texterkennung fehlgeschlagen: " + e.Message; }
    }

    async System.Threading.Tasks.Task DoSuggest()
    {
        if (_ocr == null) { _result.Text = "Ohne Texterkennung nicht möglich – bitte Rahmen von Hand ziehen."; return; }
        _result.Text = "Suche …"; var sample = _samples[_idx]; var ocr = _ocr;
        string why = ""; Profile? sug = null;
        try { (sug, why) = await System.Threading.Tasks.Task.Run(() => { var pr = DesignLearner.Suggest(sample, ocr, out var w); return (pr, w); }); }
        catch (Exception e) { _result.Text = "Vorschlag fehlgeschlagen: " + e.Message; return; }
        if (sug == null) { _result.Text = why; return; }
        _panel.Fields.Clear();
        foreach (var f in sug.Fields) _panel.Fields.Add((f.Role, new RectangleF((float)f.X, (float)f.Y, (float)f.W, (float)f.H)));
        _format.SelectedIndex = sug.Format == NameFormat.LastCommaFirst ? 1 : sug.Format == NameFormat.FirstLast ? 2 : sug.Format == NameFormat.LastFirst ? 3 : 0;
        _rName.Checked = sug.Fields.Count == 1; _panel.Invalidate();
        _result.Text = why + " Bitte prüfen und bei Bedarf Rahmen nachziehen."; await TestAll(append: true);
    }

    Profile Build()
    {
        var p = new Profile
        {
            Name = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : "Neues Design", Description = _p.Description, ValidFrom = DateTime.Now.ToString("yyyy-MM-dd"),
            Stopwords = _p.Stopwords, LastLabels = _p.LastLabels, FirstLabels = _p.FirstLabels,
            Format = new[] { NameFormat.Auto, NameFormat.LastCommaFirst, NameFormat.FirstLast, NameFormat.LastFirst }[Math.Max(0, _format.SelectedIndex)],
            Orientation = _orient.SelectedIndex == 1 ? "portrait" : "landscape",
        };
        if (_sizeSel.SelectedIndex == 1) { p.CardWidthMm = CardSizes.Id1Width; p.CardHeightMm = CardSizes.Id1Height; }
        else if (_sizeSel.SelectedIndex == 2) { p.CardWidthMm = (double)_sw.Value; p.CardHeightMm = (double)_sh.Value; }
        foreach (var f in _panel.Fields) p.Fields.Add(new FieldRegion { Role = f.role, X = Math.Round(f.frac.X, 4), Y = Math.Round(f.frac.Y, 4), W = Math.Round(f.frac.Width, 4), H = Math.Round(f.frac.Height, 4) });
        return p;
    }

    /// <summary>Liest den Namen mit den markierten Feldern auf allen Beispielkarten und zeigt das Ergebnis.</summary>
    async System.Threading.Tasks.Task TestAll(bool append = false)
    {
        if (_ocr == null) { _result.Text = "Keine Texterkennung verfügbar – Test nicht möglich. Das Design kann trotzdem gespeichert werden."; return; }
        if (_panel.Fields.Count == 0) { _result.Text = "Noch nichts markiert."; return; }
        var prev = append ? _result.Text + "\n\n" : ""; _result.Text = prev + "Teste …";
        var p = Build(); p.UseAnchors = false; p.UseHeuristic = false; var samples = _samples.ToList(); var ocr = _ocr;
        try
        {
            var lines = await System.Threading.Tasks.Task.Run(() => samples.Select((s, i) =>
            {
                var c = new NameLocator(ocr).Locate(s, p);
                return c.Count == 0 ? $"{i + 1}: nichts erkannt" : $"{i + 1}: {c[0].Name}   ({c[0].Score:0.00})";
            }).ToList());
            int good = lines.Count(l => !l.Contains("nichts erkannt"));
            _result.Text = prev + (samples.Count > 1 ? $"Gelesen auf {good} von {samples.Count} Karten:\n" : "Gelesen: ") + string.Join("\n", lines);
        }
        catch (Exception e) { _result.Text = prev + "Test fehlgeschlagen: " + e.Message; }
    }

    void Save()
    {
        if (_panel.Fields.Count == 0 && MessageBox.Show("Es wurde kein Feld markiert. Ohne Feld sucht das Programm den Namen über Beschriftungen/Schätzung. Trotzdem speichern?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Result = Build(); DialogResult = DialogResult.OK; Close();
    }
}

sealed class TextDialog : Form
{
    public TextDialog(string title, string text)
    {
        Icon = Theme.AppIcon; Text = title; Size = new Size(720, 520); StartPosition = FormStartPosition.CenterParent;
        var tb = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9.5f), Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n") };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var close = new Button { Text = "Schließen", Width = 100, DialogResult = DialogResult.OK }; var copy = new Button { Text = "In Zwischenablage kopieren", Width = 190 };
        copy.Click += (_, __) => { try { Clipboard.SetText(tb.Text); copy.Text = "Kopiert ✓"; } catch { } };
        bar.Controls.Add(close); bar.Controls.Add(copy); Controls.Add(tb); Controls.Add(bar); AcceptButton = close; Theme.Fit(this);
    }
}

sealed class PreviewForm : Form
{
    public PreviewForm(Raster r, string title)
    {
        Icon = Theme.AppIcon; Text = title; Size = new Size(Math.Min(1100, r.Width + 40), Math.Min(760, r.Height + 80)); StartPosition = FormStartPosition.CenterParent;
        Controls.Add(new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = BitmapCodec.ToBitmap(r), BackColor = Color.FromArgb(40, 40, 40) });
        KeyPreview = true; KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }
}
