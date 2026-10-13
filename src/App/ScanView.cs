using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>
/// Zeigt den gesamten Scan mit allen erkannten Rahmen. Klick auf einen Ausweis nimmt ihn heraus bzw. wieder hinein.
/// Nicht erkannter Ausweis: grob einen Rahmen darum ziehen – die genauen Ecken werden automatisch gesucht.
/// Der gewählte Ausweis hat Eck-Griffe (ziehen = Größe, Strg = drehen); ein Zug im Ausweis verschiebt ihn.
/// </summary>
sealed class ScanCanvas : Control
{
    enum Mode { None, Draw, Press, Move, Corner, Rotate }

    readonly ScanInfo _scan; readonly Bitmap _bmp; readonly double _sw, _sh; readonly double _insetMm;
    public List<ScanQuad> Quads = new List<ScanQuad>();
    int _sel = -1;
    Mode _mode; Point _down, _now; int _hitQuad, _hitCorner; PointD[]? _origin; PointD _downScan;
    public event Action? Changed;
    public event Action<string>? Hint;

    public ScanCanvas(ScanInfo scan, double insetMm)
    {
        _scan = scan; _insetMm = insetMm; _sw = scan.Image.Width; _sh = scan.Image.Height;
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
    public void SelectIndex(int i) { _sel = i; Invalidate(); }
    int HandleRadius => Theme.Px(9);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ImageRect(); g.DrawImage(_bmp, r);
        int n = 0;
        for (int qi = 0; qi < Quads.Count; qi++)
        {
            var q = Quads[qi];
            var pts = q.Corners.Select(ToView).ToArray();
            Color c = q.Include ? Color.FromArgb(34, 197, 94) : Color.FromArgb(160, 160, 160);
            if (q.Include) { n++; using var fill = new SolidBrush(Color.FromArgb(qi == _sel ? 70 : 45, c)); g.FillPolygon(fill, pts); }
            using (var pen = new Pen(c, q.Include ? (qi == _sel ? 4 : 3) : 2) { DashStyle = q.Include ? DashStyle.Solid : DashStyle.Dash }) g.DrawPolygon(pen, pts);
            // Nummer in die Ecke, damit sie den Namen nicht verdeckt
            var mid = q.Include ? new PointF(pts.Min(p => p.X) + 16, pts.Min(p => p.Y) + 14) : new PointF(pts.Average(p => p.X), pts.Average(p => p.Y));
            string label = q.Include ? n.ToString() : "✕";
            using var f = Theme.Font(q.Include ? 12f : 14f, true);
            var sz = g.MeasureString(label, f); var bx = new RectangleF(mid.X - sz.Width / 2 - 6, mid.Y - sz.Height / 2 - 2, sz.Width + 12, sz.Height + 4);
            using (var bb = new SolidBrush(Color.FromArgb(220, q.Include ? Color.FromArgb(22, 163, 74) : Color.FromArgb(110, 110, 110)))) g.FillRectangle(bb, bx);
            g.DrawString(label, f, Brushes.White, bx.X + 6, bx.Y + 2);
        }
        if (_sel >= 0 && _sel < Quads.Count && Quads[_sel].Include)        // Eck-Griffe am gewählten Ausweis
            foreach (var pt in Quads[_sel].Corners.Select(ToView))
            {
                float hr = HandleRadius * 0.6f;
                g.FillRectangle(Brushes.White, pt.X - hr, pt.Y - hr, hr * 2, hr * 2);
                using var hp = new Pen(Color.FromArgb(22, 163, 74), 2); g.DrawRectangle(hp, pt.X - hr, pt.Y - hr, hr * 2, hr * 2);
            }
        if (_mode == Mode.Draw)
        {
            using var yel = new Pen(Color.Gold, 2) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(yel, Rectangle.FromLTRB(Math.Min(_down.X, _now.X), Math.Min(_down.Y, _now.Y), Math.Max(_down.X, _now.X), Math.Max(_down.Y, _now.Y)));
        }
    }

    int HitQuad(Point p) { var sp = ToScan(p); for (int i = Quads.Count - 1; i >= 0; i--) if (QuadTools.Contains(Quads[i].Corners, sp)) return i; return -1; }

    int HitCorner(Point p)
    {
        if (_sel < 0 || _sel >= Quads.Count || !Quads[_sel].Include) return -1;
        var c = Quads[_sel].Corners;
        for (int i = 0; i < 4; i++) { var v = ToView(c[i]); if (Math.Abs(v.X - p.X) <= HandleRadius && Math.Abs(v.Y - p.Y) <= HandleRadius) return i; }
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !InImage(e.Location)) return;
        _down = _now = e.Location; _downScan = ToScan(e.Location);
        int ci = HitCorner(e.Location);
        if (ci >= 0) { _mode = (ModifierKeys & Keys.Control) != 0 ? Mode.Rotate : Mode.Corner; _hitCorner = ci; _hitQuad = _sel; _origin = Quads[_sel].Corners; return; }
        int qi = HitQuad(e.Location);
        if (qi >= 0) { _mode = Mode.Press; _hitQuad = qi; _origin = Quads[qi].Corners; return; }
        _mode = Mode.Draw; Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _now = e.Location;
        switch (_mode)
        {
            case Mode.Draw: Invalidate(); return;
            case Mode.Press:
                if (Math.Abs(e.X - _down.X) > 5 || Math.Abs(e.Y - _down.Y) > 5) { _mode = Mode.Move; _sel = _hitQuad; if (!Quads[_hitQuad].Include) Quads[_hitQuad].Include = true; }
                else return;
                goto case Mode.Move;
            case Mode.Move:
            { var sp = ToScan(e.Location); Edit(_hitQuad, QuadTools.Move(_origin!, sp.X - _downScan.X, sp.Y - _downScan.Y)); return; }
            case Mode.Corner: Edit(_hitQuad, QuadTools.DragCorner(_origin!, _hitCorner, ToScan(e.Location), Math.Max(4, _scan.Dpi / 25.4 * 8))); return;
            case Mode.Rotate: Edit(_hitQuad, QuadTools.RotateTo(_origin!, _hitCorner, ToScan(e.Location))); return;
        }
        Cursor = HitCorner(e.Location) >= 0 ? Cursors.SizeAll : HitQuad(e.Location) >= 0 ? Cursors.Hand : Cursors.Cross;
    }

    void Edit(int qi, PointD[] corners) { Quads[qi].Corners = corners; Quads[qi].Dirty = true; Changed?.Invoke(); Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var mode = _mode; _mode = Mode.None; Capture = false;
        if (mode == Mode.Press)       // kurzer Klick: herausnehmen / hineinnehmen, und auswählen
        {
            var q = Quads[_hitQuad]; q.Include = !q.Include; _sel = q.Include ? _hitQuad : -1; Changed?.Invoke(); Invalidate(); return;
        }
        if (mode != Mode.Draw) { Invalidate(); return; }
        var r = ImageRect();
        int x0 = (int)Math.Max(r.X, Math.Min(_down.X, e.X)), y0 = (int)Math.Max(r.Y, Math.Min(_down.Y, e.Y)), x1 = (int)Math.Min(r.Right, Math.Max(_down.X, e.X)), y1 = (int)Math.Min(r.Bottom, Math.Max(_down.Y, e.Y));
        Invalidate();
        if (x1 - x0 < 12 || y1 - y0 < 12) return;
        var a = ToScan(new Point(x0, y0)); var b = ToScan(new Point(x1, y1));
        AddFromRegion(new RectD(a.X, a.Y, b.X - a.X, b.Y - a.Y));
    }

    /// <summary>Nur für den UI-Test: ersten Ausweis entfernen und stattdessen einen groben Bereich darum „ziehen“.</summary>
    internal void DemoRegion()
    {
        if (Quads.Count == 0) return;
        var c = Quads[0].Corners; Quads.RemoveAt(0);
        double x0 = c.Min(p => p.X), x1 = c.Max(p => p.X), y0 = c.Min(p => p.Y), y1 = c.Max(p => p.Y), mx = (x1 - x0) * 0.12, my = (y1 - y0) * 0.12;
        AddFromRegion(new RectD(x0 - mx, y0 - my, x1 - x0 + 2 * mx, y1 - y0 + 2 * my));
    }

    /// <summary>Ecken im gezogenen Bereich automatisch suchen; gelingt das nicht, bleibt der Rahmen selbst als Startpunkt zum Anpassen.</summary>
    void AddFromRegion(RectD roi)
    {
        PointD[] corners; bool found = false;
        var old = Cursor; Cursor = Cursors.WaitCursor;
        try
        {
            var fit = CardDetector.FitCard(_scan.Image, roi, new DetectorOptions { Dpi = _scan.Dpi, EdgeInsetMm = _insetMm });
            if (fit != null) { corners = fit.Corners; found = true; }
            else corners = QuadTools.Normalize(new[] { new PointD(roi.X, roi.Y), new PointD(roi.Right, roi.Y), new PointD(roi.Right, roi.Bottom), new PointD(roi.X, roi.Bottom) });
        }
        catch { corners = QuadTools.Normalize(new[] { new PointD(roi.X, roi.Y), new PointD(roi.Right, roi.Y), new PointD(roi.Right, roi.Bottom), new PointD(roi.X, roi.Bottom) }); }
        finally { Cursor = old; }
        Quads.Add(new ScanQuad { Corners = corners, Include = true, Manual = true, Dirty = true }); _sel = Quads.Count - 1;
        Changed?.Invoke(); Invalidate();
        Hint?.Invoke(found ? "Ecken automatisch gefunden. Zum Anpassen die Eckpunkte ziehen (Strg + Ecke dreht)." : "Ecken konnten nicht automatisch gefunden werden – bitte die Eckpunkte an den Ausweis ziehen (Strg + Ecke dreht).");
    }
}

sealed class ScanViewForm : Form
{
    readonly ScanCanvas _canvas; readonly Label _info = new Label { AutoSize = false, Font = Theme.Font(10f), ForeColor = Theme.Text, BackColor = Color.Transparent };
    readonly Label _hint = new Label { AutoSize = false, Font = Theme.Font(9.5f), ForeColor = Theme.Muted, BackColor = Color.Transparent };
    public List<ScanQuad> Result => _canvas.Quads;
    internal void DemoRegion() => _canvas.DemoRegion();

    public ScanViewForm(ScanInfo scan, string? note, double edgeInsetMm = 0.6, CardItem? select = null)
    {
        Icon = Theme.AppIcon; Text = "Gesamter Scan"; BackColor = Theme.Surface; StartPosition = FormStartPosition.CenterParent; KeyPreview = true;
        Size = new Size(1100, 860); MinimumSize = new Size(700, 520);
        _canvas = new ScanCanvas(scan, edgeInsetMm) { Dock = DockStyle.Fill, Quads = scan.Quads.Select(q => new ScanQuad { Corners = q.Corners, Item = q.Item, Include = q.Item != null, Manual = q.Manual, Dirty = false }).ToList() };

        var top = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Theme.Surface, Padding = new Padding(16, 8, 16, 4) };
        _hint.Dock = DockStyle.Top; _hint.Height = 44; _info.Dock = DockStyle.Top; _info.Height = 26; _info.AutoEllipsis = true;
        top.Controls.Add(_hint); top.Controls.Add(_info);   // später hinzugefügt = zuerst angedockt → _info oben
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Theme.Surface };
        bar.Paint += (_, e) => { using var p = new Pen(Theme.Line); e.Graphics.DrawLine(p, 0, 0, bar.Width, 0); };
        var ok = Theme.Primary("Übernehmen", 170, 40, Theme.Ok); var cancel = Theme.Ghost("Abbrechen", 110, 40);
        ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel; AcceptButton = ok; CancelButton = cancel;
        var btns = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 10, 16, 0), BackColor = Color.Transparent };
        btns.Controls.Add(ok); btns.Controls.Add(cancel);
        bar.Controls.Add(btns);
        Controls.Add(_canvas); Controls.Add(top); Controls.Add(bar);

        _canvas.Changed += Idle; _canvas.Hint += h => { if (h.Length > 0) _hint.Text = h; else Idle(); };
        _canvas.Resize += (_, __) => _canvas.Invalidate();
        if (select != null) _canvas.SelectIndex(_canvas.Quads.FindIndex(q => q.Item == select));
        Theme.Fit(this);
        Idle(note);
    }

    string? _note;
    void Idle(string? note) { _note = note ?? _note; Idle(); }
    void Idle()
    {
        int n = _canvas.Quads.Count(q => q.Include);
        _info.Text = (n == 1 ? "1 Ausweis ausgewählt" : $"{n} Ausweise ausgewählt") + (string.IsNullOrEmpty(_note) ? "" : "   ·   " + _note);
        _hint.Text = "Grün = wird übernommen; Klick auf einen Ausweis nimmt ihn heraus bzw. wieder hinein.  Nicht erkannt? Grob einen Rahmen darum ziehen – die Ecken werden automatisch gesucht.  Eckpunkte ziehen = anpassen (Strg = drehen), im Ausweis ziehen = verschieben.";
    }
}
