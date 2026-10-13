using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>Bild mit Maus-Rahmen: liefert gezogene Bereiche als Anteile (0..1) der Karte.</summary>
sealed class ImagePanel : Control
{
    Bitmap? _bmp; Point? _start; Rectangle _drag;
    public List<(string role, RectangleF frac)> Fields = new List<(string, RectangleF)>();
    public event Action<RectangleF>? RegionSelected;

    public ImagePanel() { DoubleBuffered = true; BackColor = Color.FromArgb(60, 60, 60); Cursor = Cursors.Cross; }
    public void SetImage(Bitmap b) { _bmp = b; Invalidate(); }

    RectangleF ImageRect()
    {
        if (_bmp == null) return RectangleF.Empty;
        float k = Math.Min((float)(Width - 10) / _bmp.Width, (float)(Height - 10) / _bmp.Height);
        float w = _bmp.Width * k, h = _bmp.Height * k;
        return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (_bmp == null) return;
        var r = ImageRect(); e.Graphics.DrawImage(_bmp, r);
        foreach (var f in Fields)
        {
            var fr = new RectangleF(r.X + f.frac.X * r.Width, r.Y + f.frac.Y * r.Height, f.frac.Width * r.Width, f.frac.Height * r.Height);
            using var pen = new Pen(f.role == "first" ? Color.DeepSkyBlue : f.role == "last" ? Color.Orange : Color.LimeGreen, 3);
            e.Graphics.DrawRectangle(pen, fr.X, fr.Y, fr.Width, fr.Height);
            using var b = new SolidBrush(pen.Color); e.Graphics.DrawString(f.role == "first" ? "Vorname" : f.role == "last" ? "Nachname" : "Name", Font, b, fr.X + 3, Math.Max(0, fr.Y - 16));
        }
        if (_start.HasValue) using (var pen = new Pen(Color.Yellow, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) e.Graphics.DrawRectangle(pen, _drag);
    }

    protected override void OnMouseDown(MouseEventArgs e) { _start = e.Location; _drag = new Rectangle(e.Location, Size.Empty); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_start.HasValue) return;
        _drag = Rectangle.FromLTRB(Math.Min(_start.Value.X, e.X), Math.Min(_start.Value.Y, e.Y), Math.Max(_start.Value.X, e.X), Math.Max(_start.Value.Y, e.Y)); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_start.HasValue) return;
        var d = _drag; _start = null; Invalidate();
        var r = ImageRect(); if (r.IsEmpty || d.Width < 8 || d.Height < 6) return;
        float x0 = Math.Max(r.X, d.X), y0 = Math.Max(r.Y, d.Y), x1 = Math.Min(r.Right, d.Right), y1 = Math.Min(r.Bottom, d.Bottom);
        if (x1 - x0 < 6 || y1 - y0 < 4) return;
        RegionSelected?.Invoke(new RectangleF((x0 - r.X) / r.Width, (y0 - r.Y) / r.Height, (x1 - x0) / r.Width, (y1 - y0) / r.Height));
    }
}

/// <summary>Neues Ausweis-Design „anlernen“: Namensfeld auf einer Beispielkarte markieren → Profil als JSON speichern.</summary>
sealed class ProfileEditor : Form
{
    readonly Raster _card; readonly IOcrEngine? _ocr;
    readonly ImagePanel _panel = new ImagePanel();
    readonly TextBox _name = new TextBox();
    readonly ComboBox _format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, _orient = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckBox _size = new CheckBox { Text = "Kartengröße (85,6 × 54 mm) berücksichtigen", AutoSize = true };
    readonly RadioButton _rName = new RadioButton { Text = "Name komplett (Vor- + Nachname im selben Feld)", Checked = true, AutoSize = true };
    readonly RadioButton _rLast = new RadioButton { Text = "Nur Nachname", AutoSize = true }, _rFirst = new RadioButton { Text = "Nur Vorname", AutoSize = true };
    readonly Label _result = new Label { AutoSize = false, Height = 60 };
    readonly Profile _p;
    public Profile? Result { get; private set; }

    public ProfileEditor(Raster card, IOcrEngine? ocr, Profile? existing)
    {
        _card = card; _ocr = ocr; _p = existing ?? new Profile { Name = "Neues Design" };
        Icon = Theme.AppIcon; Text = "Ausweis-Design anlernen"; Size = new Size(1020, 640); StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(820, 520); Font = Theme.Font(9.5f);
        // Rechte Seite: scrollbarer Bereich + feste Fußleiste, damit „Speichern“ auch auf kleinen/hochskalierten Bildschirmen erreichbar bleibt
        var outer = new Panel { Dock = DockStyle.Right, Width = 340 };
        var right = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
        var foot = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Surface };
        outer.Controls.Add(right); outer.Controls.Add(foot);
        _panel.Dock = DockStyle.Fill; _panel.SetImage(BitmapCodec.ToBitmap(card));
        Controls.Add(_panel); Controls.Add(outer);

        int y = 6;
        Label L(string t, bool bold = false) { var l = new Label { Text = t, Left = 10, Top = y, Width = 316, AutoSize = false, Height = 20 }; if (bold) l.Font = new Font(Font, FontStyle.Bold); right.Controls.Add(l); y += 22; return l; }
        void Add(Control c, int h = 24) { c.Left = 10; c.Top = y; c.Width = 316; if (!(c is CheckBox) && !(c is RadioButton)) c.Height = h; right.Controls.Add(c); y += h + 6; }
        var hint = L("1. Was möchten Sie markieren?", true);
        Add(_rName, 20); Add(_rLast, 20); Add(_rFirst, 20);
        var h2 = new Label { Text = "2. Ziehen Sie mit der Maus einen Rahmen um genau dieses Feld auf der Karte (links). Ein neuer Rahmen ersetzt den alten derselben Art.", Left = 10, Top = y, Width = 316, Height = 54 }; right.Controls.Add(h2); y += 60;
        L("Name des Designs:"); Add(_name); _name.Text = _p.Name;
        L("Schreibweise auf der Karte:");
        _format.Items.AddRange(new object[] { "automatisch erkennen", "Nachname, Vorname", "Vorname Nachname", "NACHNAME Vorname (ohne Komma)" }); _format.SelectedIndex = _p.Format == NameFormat.LastCommaFirst ? 1 : _p.Format == NameFormat.FirstLast ? 2 : _p.Format == NameFormat.LastFirst ? 3 : 0; Add(_format);
        L("Textrichtung auf der Karte:");
        _orient.Items.AddRange(new object[] { "Querformat (normal)", "Hochformat (Text steht aufrecht)" }); _orient.SelectedIndex = _p.Orientation == "portrait" ? 1 : 0; Add(_orient);
        _size.Checked = _p.CardWidthMm > 0; Add(_size, 20);
        var test = new Button { Text = "Testen: Name aus markierten Feldern lesen" }; test.Click += (_, __) => Test(); Add(test, 30);
        _result.Left = 10; _result.Top = y; _result.Width = 316; right.Controls.Add(_result); y += 66;
        var ok = Theme.Primary("Speichern", 150, 34); ok.Location = new Point(10, 9); ok.Click += (_, __) => Save();
        var cancel = Theme.Ghost("Abbrechen", 120, 34); cancel.Location = new Point(170, 9); cancel.DialogResult = DialogResult.Cancel;
        foot.Controls.Add(ok); foot.Controls.Add(cancel); CancelButton = cancel;

        foreach (var f in _p.Fields) _panel.Fields.Add((f.Role, new RectangleF((float)f.X, (float)f.Y, (float)f.W, (float)f.H)));
        _panel.RegionSelected += r =>
        {
            string role = _rLast.Checked ? "last" : _rFirst.Checked ? "first" : "name";
            _panel.Fields.RemoveAll(f => f.role == role || (role == "name" && f.role != "name") || (role != "name" && f.role == "name"));
            _panel.Fields.Add((role, r)); _panel.Invalidate(); Test();
        };
        Theme.Fit(this);
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
        if (_size.Checked) { p.CardWidthMm = 85.6; p.CardHeightMm = 54; }
        foreach (var f in _panel.Fields) p.Fields.Add(new FieldRegion { Role = f.role, X = Math.Round(f.frac.X, 4), Y = Math.Round(f.frac.Y, 4), W = Math.Round(f.frac.Width, 4), H = Math.Round(f.frac.Height, 4) });
        return p;
    }

    void Test()
    {
        if (_ocr == null) { _result.Text = "Keine Texterkennung verfügbar – Test nicht möglich. Das Design kann trotzdem gespeichert werden."; return; }
        try
        {
            var p = Build(); p.UseAnchors = false; p.UseHeuristic = false;
            var c = new NameLocator(_ocr).Locate(_card, p);
            _result.Text = c.Count == 0 ? "Nichts erkannt. Rahmen etwas größer/genauer ziehen." : "Gelesen: " + c[0].Name + "   (Sicherheit " + c[0].Score.ToString("0.00") + ")";
        }
        catch (Exception e) { _result.Text = "Test fehlgeschlagen: " + e.Message; }
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
    public bool AdjustRequested { get; private set; }

    public PreviewForm(Raster r, string title, bool offerAdjust = false)
    {
        Icon = Theme.AppIcon; Text = title; Size = new Size(Math.Min(1100, r.Width + 40), Math.Min(760, r.Height + 80)); StartPosition = FormStartPosition.CenterParent;
        Controls.Add(new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = BitmapCodec.ToBitmap(r), BackColor = Color.FromArgb(40, 40, 40) });
        if (offerAdjust)   // Zuschnitt nachträglich korrigieren (z. B. Schattenrand, abgeschnittene Ecke)
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Surface };
            var b = Theme.Outline("✂  Zuschnitt anpassen …", 220, 34); b.Location = new Point(12, 9);
            b.Click += (_, __) => { AdjustRequested = true; Close(); };
            bar.Controls.Add(b); Controls.Add(bar);
        }
        KeyPreview = true; KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }
}
