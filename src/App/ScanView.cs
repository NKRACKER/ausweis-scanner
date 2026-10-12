using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>
/// Zeigt den gesamten Scan mit allen erkannten Rahmen. Klick auf einen Ausweis nimmt ihn heraus bzw. wieder hinein;
/// einen nicht erkannten Ausweis markiert man mit einem aufgezogenen Rahmen (oder – bei schräger Lage – über seine vier Ecken).
/// </summary>
sealed class ScanCanvas : Control
{
    readonly Bitmap _bmp; readonly double _sw, _sh;
    public List<ScanQuad> Quads = new List<ScanQuad>();
    public bool FourCorners;
    readonly List<PointD> _pending = new List<PointD>();
    Point? _down, _now; Point _hover;
    public event Action? Changed;
    public event Action<string>? Hint;

    public ScanCanvas(ScanInfo scan)
    {
        _sw = scan.Image.Width; _sh = scan.Image.Height;
        _bmp = BitmapCodec.Thumbnail(scan.Image, 1800, 1800);
        DoubleBuffered = true; BackColor = Color.FromArgb(55, 58, 64); Cursor = Cursors.Cross;
    }

    RectangleF ImageRect()
    {
        float k = Math.Min((float)(Width - 20) / _bmp.Width, (float)(Height - 20) / _bmp.Height);
        float w = _bmp.Width * k, h = _bmp.Height * k;
        return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
    }
    PointF ToView(PointD p) { var r = ImageRect(); return new PointF((float)(r.X + p.X / _sw * r.Width), (float)(r.Y + p.Y / _sh * r.Height)); }
    PointD ToScan(Point p) { var r = ImageRect(); return new PointD((p.X - r.X) / r.Width * _sw, (p.Y - r.Y) / r.Height * _sh); }
    bool InImage(Point p) => ImageRect().Contains(p);

    public void CancelPending() { _pending.Clear(); _down = null; Invalidate(); Hint?.Invoke(""); }
    public void SetFourCorners(bool on) { FourCorners = on; CancelPending(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ImageRect(); g.DrawImage(_bmp, r);
        int n = 0;
        foreach (var q in Quads)
        {
            var pts = q.Corners.Select(ToView).ToArray();
            Color c = q.Include ? Color.FromArgb(34, 197, 94) : Color.FromArgb(160, 160, 160);
            if (q.Include) { n++; using var fill = new SolidBrush(Color.FromArgb(55, c)); g.FillPolygon(fill, pts); }
            using (var pen = new Pen(c, q.Include ? 3 : 2) { DashStyle = q.Include ? DashStyle.Solid : DashStyle.Dash }) g.DrawPolygon(pen, pts);
            var mid = new PointF(pts.Average(p => p.X), pts.Average(p => p.Y));
            string label = q.Include ? n.ToString() : "✕";
            using var f = Theme.Font(q.Include ? 16f : 14f, true);
            var sz = g.MeasureString(label, f); var bx = new RectangleF(mid.X - sz.Width / 2 - 6, mid.Y - sz.Height / 2 - 2, sz.Width + 12, sz.Height + 4);
            using (var bb = new SolidBrush(Color.FromArgb(220, q.Include ? Color.FromArgb(22, 163, 74) : Color.FromArgb(110, 110, 110)))) g.FillRectangle(bb, bx);
            g.DrawString(label, f, Brushes.White, bx.X + 6, bx.Y + 2);
        }
        using var yel = new Pen(Color.Gold, 2) { DashStyle = DashStyle.Dash };
        if (_down.HasValue && _now.HasValue && !FourCorners) g.DrawRectangle(yel, Rectangle.FromLTRB(Math.Min(_down.Value.X, _now.Value.X), Math.Min(_down.Value.Y, _now.Value.Y), Math.Max(_down.Value.X, _now.Value.X), Math.Max(_down.Value.Y, _now.Value.Y)));
        if (_pending.Count > 0)
        {
            var pv = _pending.Select(ToView).ToList(); if (pv.Count > 1) g.DrawLines(yel, pv.ToArray());
            if (_pending.Count > 0) g.DrawLine(yel, pv[pv.Count - 1], _hover);
            foreach (var p in pv) g.FillEllipse(Brushes.Gold, p.X - 5, p.Y - 5, 10, 10);
        }
    }

    int HitQuad(Point p) { var sp = ToScan(p); for (int i = Quads.Count - 1; i >= 0; i--) if (QuadTools.Contains(Quads[i].Corners, sp)) return i; return -1; }

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && InImage(e.Location)) { _down = e.Location; _now = e.Location; } }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        _hover = e.Location;
        if (_down.HasValue) { _now = e.Location; Invalidate(); }
        else { Cursor = HitQuad(e.Location) >= 0 && _pending.Count == 0 ? Cursors.Hand : Cursors.Cross; if (_pending.Count > 0) Invalidate(); }
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!_down.HasValue) return;
        var d = _down.Value; _down = null; var u = e.Location; Invalidate();
        bool click = Math.Abs(u.X - d.X) < 6 && Math.Abs(u.Y - d.Y) < 6;
        if (FourCorners)
        {
            if (!click || !InImage(u)) return;
            _pending.Add(ToScan(u));
            if (_pending.Count < 4) { Hint?.Invoke($"Ecke {_pending.Count} von 4 gesetzt – nächste Ecke des Ausweises anklicken (Esc bricht ab)."); return; }
            AddQuad(QuadTools.Normalize(_pending)); _pending.Clear(); Hint?.Invoke(""); return;
        }
        if (click)
        {
            int i = HitQuad(u);
            if (i >= 0) { Quads[i].Include = !Quads[i].Include; Changed?.Invoke(); Invalidate(); }
            return;
        }
        var r = ImageRect(); int x0 = (int)Math.Max(r.X, Math.Min(d.X, u.X)), y0 = (int)Math.Max(r.Y, Math.Min(d.Y, u.Y)), x1 = (int)Math.Min(r.Right, Math.Max(d.X, u.X)), y1 = (int)Math.Min(r.Bottom, Math.Max(d.Y, u.Y));
        if (x1 - x0 < 12 || y1 - y0 < 12) return;
        AddQuad(QuadTools.Normalize(new[] { ToScan(new Point(x0, y0)), ToScan(new Point(x1, y0)), ToScan(new Point(x1, y1)), ToScan(new Point(x0, y1)) }));
    }

    void AddQuad(PointD[] corners) { Quads.Add(new ScanQuad { Corners = corners, Include = true, Manual = true }); Changed?.Invoke(); Invalidate(); }
}

sealed class ScanViewForm : Form
{
    readonly ScanCanvas _canvas; readonly Label _info = new Label { AutoSize = false, Font = Theme.Font(10f), ForeColor = Theme.Text, BackColor = Color.Transparent };
    readonly Label _hint = new Label { AutoSize = false, Font = Theme.Font(9.5f), ForeColor = Theme.Muted, BackColor = Color.Transparent };
    readonly CheckBox _four = new CheckBox { Text = "Ausweis liegt schräg: vier Ecken anklicken", AutoSize = true, Font = Theme.Font(9.5f), BackColor = Color.Transparent };
    public List<ScanQuad> Result => _canvas.Quads;

    public ScanViewForm(ScanInfo scan, string? note)
    {
        Icon = Theme.AppIcon; Text = "Gesamter Scan"; BackColor = Theme.Surface; StartPosition = FormStartPosition.CenterParent; KeyPreview = true;
        var wa = Screen.PrimaryScreen.WorkingArea; Size = new Size(Math.Min(1100, wa.Width), Math.Min(860, wa.Height)); MinimumSize = new Size(700, 520);
        _canvas = new ScanCanvas(scan) { Dock = DockStyle.Fill, Quads = scan.Quads.Select(q => new ScanQuad { Corners = q.Corners, Item = q.Item, Include = q.Item != null, Manual = q.Manual }).ToList() };

        var top = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Surface };
        _info.SetBounds(16, 8, 1000, 24); _hint.SetBounds(16, 34, 1000, 22);
        top.Controls.AddRange(new Control[] { _info, _hint });
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Theme.Surface };
        bar.Paint += (_, e) => { using var p = new Pen(Theme.Line); e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
        var ok = Theme.Primary("Übernehmen", 170, 40, Theme.Ok); var cancel = Theme.Ghost("Abbrechen", 110, 40);
        ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel; AcceptButton = ok; CancelButton = cancel;
        _four.Location = new Point(16, 20); bar.Controls.Add(_four);
        bar.Controls.AddRange(new Control[] { ok, cancel });
        bar.Resize += (_, __) => { ok.Location = new Point(bar.Width - 186, 11); cancel.Location = new Point(bar.Width - 306, 11); };
        Controls.Add(_canvas); Controls.Add(top); Controls.Add(bar);

        _four.CheckedChanged += (_, __) => { _canvas.SetFourCorners(_four.Checked); Idle(); };
        _canvas.Changed += Idle; _canvas.Hint += h => { if (h.Length > 0) _hint.Text = h; else Idle(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && _four.Checked) { _canvas.CancelPending(); e.Handled = true; e.SuppressKeyPress = true; } };
        _canvas.Resize += (_, __) => _canvas.Invalidate();
        Idle(note);
    }

    string? _note;
    void Idle(string? note) { _note = note ?? _note; Idle(); }
    void Idle()
    {
        int n = _canvas.Quads.Count(q => q.Include);
        _info.Text = (n == 1 ? "1 Ausweis ausgewählt" : $"{n} Ausweise ausgewählt") + (string.IsNullOrEmpty(_note) ? "" : "   ·   " + _note);
        _hint.Text = _four.Checked ? "Die vier Ecken des Ausweises nacheinander anklicken." : "Grün = wird übernommen. Klick auf einen Ausweis: herausnehmen / wieder hineinnehmen.  Rahmen ziehen: nicht erkannten Ausweis hinzufügen.";
    }
}
