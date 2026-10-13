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
    enum Mode { None, Draw, Press, Move, Corner, Edge, Rotate }

    readonly ScanInfo _scan; readonly Bitmap _bmp; readonly double _sw, _sh, _ox, _oy; readonly double _insetMm; readonly bool _single;
    PointD? _loupe;   // Mitte der Lupe (Scan-Koordinaten), solange eine Ecke gezogen wird
    public List<ScanQuad> Quads = new List<ScanQuad>();
    int _sel = -1;
    Mode _mode; Point _down, _now; int _hitQuad, _hitCorner, _hitEdge; PointD[]? _origin; PointD _downScan;
    public event Action? Changed;
    public event Action<string>? Hint;

    /// <param name="region">null: ganzer Scan. Sonst nur dieser Ausschnitt (Einzelansicht eines Ausweises: kein Hinzufügen/Herausnehmen, Bereich außerhalb des Rahmens abgedunkelt).</param>
    public ScanCanvas(ScanInfo scan, double insetMm, RectD? region = null)
    {
        _scan = scan; _insetMm = insetMm; _single = region != null;
        if (region is RectD rg)
        {
            int x0 = Math.Max(0, (int)Math.Floor(rg.X)), y0 = Math.Max(0, (int)Math.Floor(rg.Y));
            int x1 = Math.Min(scan.Image.Width, (int)Math.Ceiling(rg.Right)), y1 = Math.Min(scan.Image.Height, (int)Math.Ceiling(rg.Bottom));
            _ox = x0; _oy = y0; _sw = Math.Max(1, x1 - x0); _sh = Math.Max(1, y1 - y0);
            _bmp = BitmapCodec.Thumbnail(scan.Image.Crop(x0, y0, x1 - x0, y1 - y0), 2000, 2000);
        }
        else { _sw = scan.Image.Width; _sh = scan.Image.Height; _bmp = BitmapCodec.Thumbnail(scan.Image, 1800, 1800); }
        DoubleBuffered = true; BackColor = Color.FromArgb(55, 58, 64); Cursor = Cursors.Cross;
    }

    RectangleF ImageRect()
    {
        float k = Math.Min((float)(Width - 20) / _bmp.Width, (float)(Height - 20) / _bmp.Height);
        float w = _bmp.Width * k, h = _bmp.Height * k;
        return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
    }
    PointF ToView(PointD p) { var r = ImageRect(); return new PointF((float)(r.X + (p.X - _ox) / _sw * r.Width), (float)(r.Y + (p.Y - _oy) / _sh * r.Height)); }
    PointD ToScan(Point p) { var r = ImageRect(); return new PointD(_ox + (p.X - r.X) / r.Width * _sw, _oy + (p.Y - r.Y) / r.Height * _sh); }
    bool InImage(Point p) => ImageRect().Contains(p);
    public void SelectIndex(int i) { _sel = i; Invalidate(); Changed?.Invoke(); }
    /// <summary>Ein Rahmen ist gewählt (Griffe sichtbar, löschbar).</summary>
    public bool HasSelection => _sel >= 0 && _sel < Quads.Count && !Quads[_sel].Removed;
    public bool SelectionIncluded => HasSelection && Quads[_sel].Include;

    /// <summary>Gewählten Ausweis herausnehmen (grau, kommt nicht in die Liste) bzw. wieder hineinnehmen.</summary>
    public void ToggleSelected() { if (!HasSelection) return; Quads[_sel].Include = !Quads[_sel].Include; Changed?.Invoke(); Invalidate(); }

    /// <summary>Gewählten Rahmen ganz löschen. Gehört schon eine Kachel dazu, wird sie beim Übernehmen entfernt.</summary>
    public void DeleteSelected() => DeleteQuad(_sel);

    void DeleteQuad(int i)
    {
        if (i < 0 || i >= Quads.Count || Quads[i].Removed) return;
        if (Quads[i].Item != null) { Quads[i].Removed = true; Quads[i].Include = false; } else Quads.RemoveAt(i);
        _sel = -1; Changed?.Invoke(); Invalidate();
    }

    void ShowMenu(int qi, Point at)
    {
        var q = Quads[qi]; _sel = qi; Invalidate(); Changed?.Invoke();
        var m = new ContextMenuStrip { Font = Theme.Font(9.5f) };
        m.Items.Add(q.Include ? "Herausnehmen (nicht speichern)" : "Wieder hineinnehmen", null, (_, __) => { q.Include = !q.Include; Changed?.Invoke(); Invalidate(); });
        m.Items.Add("Rahmen löschen", null, (_, __) => DeleteQuad(qi));
        m.Show(this, at);
    }
    int HandleRadius => Theme.Px(9);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ImageRect(); g.DrawImage(_bmp, r);
        int n = 0;
        for (int qi = 0; qi < Quads.Count; qi++)
        {
            var q = Quads[qi]; if (q.Removed) continue;
            var pts = q.Corners.Select(ToView).ToArray();
            if (_single)   // Einzelansicht: alles außerhalb des Rahmens abdunkeln, damit sofort sichtbar ist, was im Ergebnis landet
            {
                using var path = new GraphicsPath(FillMode.Alternate); path.AddRectangle(r); path.AddPolygon(pts);
                using var dim = new SolidBrush(Color.FromArgb(150, 20, 22, 26)); g.FillPath(dim, path);
                using var pen1 = new Pen(Color.FromArgb(34, 197, 94), 2.5f); g.DrawPolygon(pen1, pts);
                continue;
            }
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
        if (HasSelection)        // Griffe am gewählten Ausweis: Ecken (Quadrate) und Kantenmitten (Balken)
        {
            var cv = Quads[_sel].Corners.Select(ToView).ToArray();
            using var green = new Pen(Color.FromArgb(22, 163, 74), 2);
            float hr = HandleRadius * 0.6f;
            for (int i = 0; i < 4; i++)
            {
                var a1 = cv[i]; var b1 = cv[(i + 1) % 4];
                float ang = (float)(Math.Atan2(b1.Y - a1.Y, b1.X - a1.X) * 180 / Math.PI), mx = (a1.X + b1.X) / 2, my = (a1.Y + b1.Y) / 2;
                var st = g.Save(); g.TranslateTransform(mx, my); g.RotateTransform(ang);
                float bl = hr * 2.4f, bt = hr * 0.9f;
                using (var bp = Theme.Round(new RectangleF(-bl, -bt, bl * 2, bt * 2), bt)) { g.FillPath(Brushes.White, bp); g.DrawPath(green, bp); }
                g.Restore(st);
            }
            foreach (var pt in cv) { g.FillRectangle(Brushes.White, pt.X - hr, pt.Y - hr, hr * 2, hr * 2); g.DrawRectangle(green, pt.X - hr, pt.Y - hr, hr * 2, hr * 2); }
        }
        if (_loupe is PointD lc) DrawLoupe(g, lc, r);
        if (_mode == Mode.Draw)
        {
            using var yel = new Pen(Color.Gold, 2) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(yel, Rectangle.FromLTRB(Math.Min(_down.X, _now.X), Math.Min(_down.Y, _now.Y), Math.Max(_down.X, _now.X), Math.Max(_down.Y, _now.Y)));
        }
    }

    /// <summary>Lupe: die Stelle unter dem gezogenen Eckpunkt in Original-Auflösung, 4-fach größer als die aktuelle Ansicht, mit dem aktuellen Rahmen darübergezeichnet.</summary>
    void DrawLoupe(Graphics g, PointD c, RectangleF imgRect)
    {
        int L = Theme.Px(190); const double Z = 4;
        double k = imgRect.Width / _sw;                         // Ansicht-Pixel je Scan-Pixel
        double srcW = L / (k * Z);                              // Kantenlänge des Ausschnitts in Scan-Pixeln
        double sx = c.X - srcW / 2, sy = c.Y - srcW / 2;
        int ix0 = Math.Max(0, (int)Math.Floor(sx)), iy0 = Math.Max(0, (int)Math.Floor(sy));
        int ix1 = Math.Min(_scan.Image.Width, (int)Math.Ceiling(sx + srcW)), iy1 = Math.Min(_scan.Image.Height, (int)Math.Ceiling(sy + srcW));
        if (ix1 - ix0 < 2 || iy1 - iy0 < 2) return;
        using var lb = new Bitmap(L, L); using (var g2 = Graphics.FromImage(lb))
        {
            g2.Clear(Color.FromArgb(30, 32, 36));
            g2.InterpolationMode = InterpolationMode.HighQualityBilinear; g2.PixelOffsetMode = PixelOffsetMode.Half;
            using (var part = BitmapCodec.ToBitmap(_scan.Image.Crop(ix0, iy0, ix1 - ix0, iy1 - iy0)))
                g2.DrawImage(part, new RectangleF((float)((ix0 - sx) / srcW * L), (float)((iy0 - sy) / srcW * L), (float)((ix1 - ix0) / srcW * L), (float)((iy1 - iy0) / srcW * L)));
            g2.SmoothingMode = SmoothingMode.AntiAlias;
            if (_hitQuad >= 0 && _hitQuad < Quads.Count)
            {
                var pts = Quads[_hitQuad].Corners.Select(p => new PointF((float)((p.X - sx) / srcW * L), (float)((p.Y - sy) / srcW * L))).ToArray();
                using var pen = new Pen(Color.FromArgb(34, 230, 94), 2); g2.DrawPolygon(pen, pts);
            }
            float m = L / 2f; using (var cp = new Pen(Color.FromArgb(200, 255, 255, 255), 1)) { g2.DrawLine(cp, m - 10, m, m + 10, m); g2.DrawLine(cp, m, m - 10, m, m + 10); }
        }
        var mp = PointToClient(Cursor.Position);
        int gap = Theme.Px(28); int lx = mp.X - L - gap, ly = mp.Y - L - gap;
        if (lx < 4) lx = mp.X + gap; if (ly < 4) ly = mp.Y + gap;
        if (lx + L > Width - 4) lx = Width - L - 4; if (ly + L > Height - 4) ly = Height - L - 4;
        g.SmoothingMode = SmoothingMode.None;
        using (var sh = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillRectangle(sh, lx + 4, ly + 4, L, L);
        g.DrawImage(lb, lx, ly, L, L);
        using (var bp = new Pen(Color.White, 3)) g.DrawRectangle(bp, lx, ly, L, L);
        using (var bp2 = new Pen(Color.FromArgb(60, 60, 60), 1)) g.DrawRectangle(bp2, lx - 2, ly - 2, L + 4, L + 4);
    }

    internal void DemoLoupe() { if (Quads.Count == 0) return; _hitQuad = 0; _loupe = Quads[0].Corners[2]; Invalidate(); }

    /// <summary>Rahmen komplett setzen (z. B. nach automatischem Neu-Suchen oder Zurücksetzen).</summary>
    public void SetCorners(int qi, PointD[] corners) { Quads[qi].Corners = corners; Quads[qi].Dirty = true; Changed?.Invoke(); Invalidate(); }

    int HitQuad(Point p) { var sp = ToScan(p); for (int i = Quads.Count - 1; i >= 0; i--) if (!Quads[i].Removed && QuadTools.Contains(Quads[i].Corners, sp)) return i; return -1; }

    int HitCorner(Point p)
    {
        if (!HasSelection) return -1;
        var c = Quads[_sel].Corners;
        for (int i = 0; i < 4; i++) { var v = ToView(c[i]); if (Math.Abs(v.X - p.X) <= HandleRadius && Math.Abs(v.Y - p.Y) <= HandleRadius) return i; }
        return -1;
    }

    int HitEdge(Point p)
    {
        if (!HasSelection) return -1;
        var c = Quads[_sel].Corners.Select(ToView).ToArray();
        for (int i = 0; i < 4; i++)
        {
            var a = c[i]; var b = c[(i + 1) % 4]; float mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
            if (Math.Abs(mx - p.X) <= HandleRadius * 1.3f && Math.Abs(my - p.Y) <= HandleRadius * 1.3f) return i;
        }
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right && !_single && InImage(e.Location)) { int rq = HitQuad(e.Location); if (rq >= 0) ShowMenu(rq, e.Location); return; }
        if (e.Button != MouseButtons.Left || !InImage(e.Location)) return;
        _down = _now = e.Location; _downScan = ToScan(e.Location);
        int ci = HitCorner(e.Location);
        if (ci >= 0) { _mode = (ModifierKeys & Keys.Control) != 0 ? Mode.Rotate : Mode.Corner; _hitCorner = ci; _hitQuad = _sel; _origin = Quads[_sel].Corners; return; }
        int ei = HitEdge(e.Location);
        if (ei >= 0) { _mode = Mode.Edge; _hitEdge = ei; _hitQuad = _sel; _origin = Quads[_sel].Corners; return; }
        int qi = HitQuad(e.Location);
        if (qi >= 0) { _mode = Mode.Press; _hitQuad = qi; _origin = Quads[qi].Corners; return; }
        if (_single) return;   // Einzelansicht: nichts Neues aufziehen
        _mode = Mode.Draw; Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _now = e.Location;
        switch (_mode)
        {
            case Mode.Draw: Invalidate(); return;
            case Mode.Press:
                if (Math.Abs(e.X - _down.X) > 5 || Math.Abs(e.Y - _down.Y) > 5) { _mode = Mode.Move; if (_sel != _hitQuad) { _sel = _hitQuad; Changed?.Invoke(); } }
                else return;
                goto case Mode.Move;
            case Mode.Move:
            { var sp = ToScan(e.Location); Edit(_hitQuad, QuadTools.Move(_origin!, sp.X - _downScan.X, sp.Y - _downScan.Y)); return; }
            case Mode.Corner: _loupe = ToScan(e.Location); Edit(_hitQuad, QuadTools.DragCorner(_origin!, _hitCorner, ToScan(e.Location), Math.Max(4, _scan.Dpi / 25.4 * 8))); return;
            case Mode.Edge: _loupe = ToScan(e.Location); Edit(_hitQuad, QuadTools.DragEdge(_origin!, _hitEdge, ToScan(e.Location), Math.Max(4, _scan.Dpi / 25.4 * 8))); return;
            case Mode.Rotate: _loupe = ToScan(e.Location); Edit(_hitQuad, QuadTools.RotateTo(_origin!, _hitCorner, ToScan(e.Location))); return;
        }
        int hc = HitCorner(e.Location), he = hc >= 0 ? -1 : HitEdge(e.Location);
        Cursor = hc >= 0 ? Cursors.SizeAll : he >= 0 ? (he % 2 == 0 ? Cursors.SizeNS : Cursors.SizeWE) : HitQuad(e.Location) >= 0 ? Cursors.Hand : Cursors.Cross;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (_single || e.Button != MouseButtons.Left) { base.OnMouseDoubleClick(e); return; }
        int qi = HitQuad(e.Location); if (qi >= 0) { _sel = qi; ToggleSelected(); }
    }

    void Edit(int qi, PointD[] corners) { Quads[qi].Corners = corners; Quads[qi].Dirty = true; Changed?.Invoke(); Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        var mode = _mode; _mode = Mode.None; Capture = false; _loupe = null;
        if (mode == Mode.Press && _single) { Invalidate(); return; }
        if (mode == Mode.Press)       // kurzer Klick: Ausweis auswählen (Griffe erscheinen); Herausnehmen ist eine eigene Aktion
        {
            _sel = _hitQuad; Changed?.Invoke(); Invalidate(); return;
        }
        if (mode != Mode.Draw) { Invalidate(); return; }
        var r = ImageRect();
        int x0 = (int)Math.Max(r.X, Math.Min(_down.X, e.X)), y0 = (int)Math.Max(r.Y, Math.Min(_down.Y, e.Y)), x1 = (int)Math.Min(r.Right, Math.Max(_down.X, e.X)), y1 = (int)Math.Min(r.Bottom, Math.Max(_down.Y, e.Y));
        Invalidate();
        if (x1 - x0 < 12 || y1 - y0 < 12) { if (_sel >= 0) { _sel = -1; Changed?.Invoke(); } return; }   // Klick ins Leere: Auswahl aufheben
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
    readonly List<ScanInfo> _scans; readonly Dictionary<ScanInfo, ScanCanvas> _canvases = new Dictionary<ScanInfo, ScanCanvas>();
    readonly double _inset; readonly CardItem? _select; int _cur; ScanCanvas _canvas = null!;
    readonly Label _info = new Label { AutoSize = true, Font = Theme.Font(11f, true), ForeColor = Theme.Text, BackColor = Color.Transparent, UseMnemonic = false, Anchor = AnchorStyles.Left };
    readonly Label _hint = new Label { AutoSize = true, Font = Theme.Font(9.5f), ForeColor = Theme.Muted, BackColor = Color.Transparent, UseMnemonic = false, Anchor = AnchorStyles.Left, Visible = false };
    readonly Button _delete = Theme.Ghost("🗑  Rahmen löschen", 170, 40), _toggle = Theme.Outline("Herausnehmen", 160, 40), _prev = Theme.Outline("◀", 48, 40), _next = Theme.Outline("▶", 48, 40);
    readonly Label _page = new Label { AutoSize = true, Font = Theme.Font(10f), ForeColor = Theme.Muted, BackColor = Color.Transparent, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(8, 10, 0, 0) };
    readonly string? _note;

    /// <summary>Quadrate je Scan, den man angesehen hat (nur diese können sich geändert haben).</summary>
    public IEnumerable<(ScanInfo scan, List<ScanQuad> quads)> Results => _scans.Where(s => _canvases.ContainsKey(s)).Select(s => (s, _canvases[s].Quads));
    internal void DemoRegion() => _canvas.DemoRegion();

    /// <param name="scans">Alle geladenen Scans/Dateien: bei mehreren blättert man mit ◀ ▶ durch (jeweils die ganze Datei).</param>
    public ScanViewForm(IList<ScanInfo> scans, int start, string? note, double edgeInsetMm = 0.6, CardItem? select = null)
    {
        _scans = scans.ToList(); _cur = Math.Max(0, Math.Min(start, _scans.Count - 1)); _inset = edgeInsetMm; _select = select; _note = note;
        Icon = Theme.AppIcon; Text = "Gesamter Scan"; BackColor = Theme.Surface; StartPosition = FormStartPosition.CenterParent; KeyPreview = true;
        Size = new Size(1100, 860); MinimumSize = new Size(640, 480);

        var top = Dialogs.TopBar(_info, _hint, Theme.Help("Gesamter Scan", "Hier siehst du die ganze Datei mit allen erkannten Rahmen. Bei mehreren geladenen Dateien (oder PDF-Seiten) blätterst du mit ◀ ▶.\n\nGrün = wird übernommen. Ein Klick wählt einen Ausweis aus – dann erscheinen die Griffe. „Herausnehmen“ (auch Leertaste, Doppelklick oder Rechtsklick) macht ihn grau: er kommt dann nicht in die Liste.\n\nNicht erkannt? Grob einen Rahmen darum ziehen – die Ecken werden automatisch gesucht.\n\nGewählten Ausweis anpassen: Eckpunkt ziehen ändert die Größe, die Mitte einer Kante verschiebt die ganze Kante, Strg + Eckpunkt dreht, im Ausweis ziehen verschiebt. Beim Ziehen zeigt eine Lupe die Stelle vergrößert.\n\nEntf oder Rechtsklick löscht einen Rahmen."));
        var bar = Dialogs.BottomBar(out var ok, out var cancel, this, "Übernehmen", out var left);
        _delete.ForeColor = Theme.Bad; _delete.Enabled = false; _delete.Margin = new Padding(0, 0, 14, 0);
        _delete.Click += (_, __) => _canvas.DeleteSelected();
        _toggle.Enabled = false; _toggle.Margin = new Padding(0, 0, 14, 0); _toggle.Click += (_, __) => _canvas.ToggleSelected();
        left.Controls.Add(_delete); left.Controls.Add(_toggle);
        if (_scans.Count > 1)
        {
            _prev.Margin = new Padding(0, 0, 4, 0); _next.Margin = new Padding(0); _prev.Click += (_, __) => Show(-1); _next.Click += (_, __) => Show(+1);
            left.Controls.Add(_prev); left.Controls.Add(_next); left.Controls.Add(_page);
        }
        Controls.Add(top); Controls.Add(bar);
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete && _canvas.HasSelection) { _canvas.DeleteSelected(); e.Handled = true; } else if (e.KeyCode == Keys.Space && _canvas.HasSelection) { _canvas.ToggleSelected(); e.Handled = true; } };
        ShowScan(_cur, _select);
        Theme.Fit(this);
        if (!string.IsNullOrEmpty(note)) { _hint.Text = note; _hint.Visible = true; }
    }

    public ScanViewForm(ScanInfo scan, string? note, double edgeInsetMm = 0.6, CardItem? select = null) : this(new[] { scan }, 0, note, edgeInsetMm, select) { }

    void Show(int delta) { ShowScan((_cur + delta + _scans.Count) % _scans.Count, null); }

    void ShowScan(int idx, CardItem? select)
    {
        if (_canvas != null) Controls.Remove(_canvas);
        _cur = idx; var scan = _scans[idx];
        if (!_canvases.TryGetValue(scan, out var c))
        {
            c = new ScanCanvas(scan, _inset) { Dock = DockStyle.Fill, Quads = scan.Quads.Select(q => new ScanQuad { Corners = q.Corners, Item = q.Item, Include = q.Item != null, Manual = q.Manual, Dirty = false }).ToList() };
            c.Changed += () => { if (c == _canvas) { SelectionUi(); Idle(); } };
            c.Hint += h => { _hint.Text = h; _hint.Visible = h.Length > 0; };
            c.Resize += (_, __) => c.Invalidate();
            if (select != null) c.SelectIndex(c.Quads.FindIndex(q => q.Item == select));
            _canvases[scan] = c;
        }
        _canvas = c; Controls.Add(c); c.BringToFront(); Controls.SetChildIndex(c, 0);
        SelectionUi();
        if (_scans.Count > 1) { _page.Text = $"{idx + 1} / {_scans.Count}   ·   {scan.Name}"; Text = "Gesamter Scan – " + scan.Name; }
        _hint.Visible = false; Idle();
    }

    void SelectionUi()
    {
        _delete.Enabled = _canvas.HasSelection; _toggle.Enabled = _canvas.HasSelection;
        _toggle.Text = _canvas.HasSelection && !_canvas.SelectionIncluded ? "Wieder hineinnehmen" : "Herausnehmen";
    }

    void Idle()
    {
        int n = _canvas.Quads.Count(q => q.Include && !q.Removed);
        _info.Text = n == 1 ? "1 Ausweis ausgewählt" : $"{n} Ausweise ausgewählt";
    }
}

/// <summary>
/// Große Ansicht eines einzelnen Ausweises (mit etwas Rand drumherum): den Rahmen direkt hier anpassen – Eckpunkte ziehen, mit Lupe.
/// </summary>
sealed class CropEditorForm : Form
{
    readonly ScanCanvas _canvas; readonly ScanInfo _scan; readonly PointD[] _start; readonly double _inset;
    public PointD[] Result => _canvas.Quads[0].Corners;

    public CropEditorForm(ScanInfo scan, PointD[] corners, double edgeInsetMm)
    {
        _scan = scan; _start = corners.ToArray(); _inset = edgeInsetMm;
        Icon = Theme.AppIcon; Text = "Zuschnitt anpassen"; BackColor = Theme.Surface; StartPosition = FormStartPosition.CenterParent; KeyPreview = true;
        Size = new Size(1000, 780); MinimumSize = new Size(600, 480);
        double pad = 10 * scan.Dpi / 25.4;
        double x0 = corners.Min(p => p.X) - pad, y0 = corners.Min(p => p.Y) - pad, x1 = corners.Max(p => p.X) + pad, y1 = corners.Max(p => p.Y) + pad;
        _canvas = new ScanCanvas(scan, edgeInsetMm, new RectD(x0, y0, x1 - x0, y1 - y0)) { Dock = DockStyle.Fill };
        _canvas.Quads.Add(new ScanQuad { Corners = _start.ToArray(), Include = true });
        _canvas.SelectIndex(0);

        var info = new Label { AutoSize = true, Font = Theme.Font(11f, true), ForeColor = Theme.Text, BackColor = Color.Transparent, UseMnemonic = false, Anchor = AnchorStyles.Left, Text = "Alles außerhalb des grünen Rahmens wird abgeschnitten" };
        var top = Dialogs.TopBar(info, null, Theme.Help("Zuschnitt anpassen", "Eckpunkt ziehen: Größe ändern – die gegenüberliegende Ecke bleibt stehen.\n\nMitte einer Kante ziehen: die ganze Kante parallel verschieben.\n\nStrg + Eckpunkt: drehen.  Im Ausweis ziehen: verschieben.\n\nBeim Ziehen zeigt eine Lupe die Stelle vergrößert in voller Scan-Auflösung. „Automatisch neu suchen“ findet die Ecken erneut, „Zurücksetzen“ stellt den Anfangszustand wieder her."));
        var bar = Dialogs.BottomBar(out var ok, out var cancel, this, "Übernehmen", out var left);
        var auto = Theme.Outline("Automatisch neu suchen", 200, 40); var reset = Theme.Ghost("Zurücksetzen", 130, 40);
        auto.Margin = new Padding(0, 0, 6, 0); auto.Click += (_, __) => AutoFit(); reset.Click += (_, __) => _canvas.SetCorners(0, _start.ToArray());
        left.Controls.Add(auto); left.Controls.Add(reset);
        Controls.Add(_canvas); Controls.Add(top); Controls.Add(bar);
        Theme.Fit(this);
    }

    void AutoFit()
    {
        var c = _canvas.Quads[0].Corners;
        var roi = new RectD(c.Min(p => p.X), c.Min(p => p.Y), c.Max(p => p.X) - c.Min(p => p.X), c.Max(p => p.Y) - c.Min(p => p.Y));
        var old = Cursor; Cursor = Cursors.WaitCursor;
        try
        {
            var fit = CardDetector.FitCard(_scan.Image, roi, new DetectorOptions { Dpi = _scan.Dpi, EdgeInsetMm = _inset });
            if (fit != null) _canvas.SetCorners(0, fit.Corners);
            else MessageBox.Show(this, "Hier konnte die Karte nicht automatisch gefunden werden. Bitte die Eckpunkte von Hand ziehen.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { Cursor = old; }
    }

    internal void DemoLoupe() => _canvas.DemoLoupe();   // nur UI-Test
}
