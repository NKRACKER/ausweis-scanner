using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AusweisScanner.App;

/// <summary>Einheitliches, ruhiges Erscheinungsbild: viel Weißraum, eine Akzentfarbe, gerundete Flächen, Hilfen hinter kleinen „?“.</summary>
static class Theme
{
    public static readonly Color Bg = Color.FromArgb(244, 246, 250), Surface = Color.White, SurfaceAlt = Color.FromArgb(240, 243, 248), Line = Color.FromArgb(226, 230, 237);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235), AccentHover = Color.FromArgb(29, 78, 216), AccentSoft = Color.FromArgb(232, 240, 254);
    public static readonly Color Ok = Color.FromArgb(22, 163, 74), OkSoft = Color.FromArgb(226, 246, 234);
    public static readonly Color Warn = Color.FromArgb(202, 113, 6), WarnSoft = Color.FromArgb(254, 243, 224);
    public static readonly Color Bad = Color.FromArgb(220, 38, 38), BadSoft = Color.FromArgb(253, 233, 233);
    public static readonly Color Text = Color.FromArgb(24, 32, 46), Muted = Color.FromArgb(100, 110, 126);

    /// <summary>Skalierung der Oberfläche (Windows-Anzeigeskalierung). AUSWEIS_UI_SCALE=1.5 simuliert sie – für Screenshot-Tests in der CI.</summary>
    public static readonly float Factor; public static readonly bool Simulated;

    static Theme()
    {
        var e = Environment.GetEnvironmentVariable("AUSWEIS_UI_SCALE");
        if (float.TryParse(e, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) && f >= 0.5f && f <= 4f) { Simulated = true; Factor = f; }
        else { try { using var g = Graphics.FromHwnd(IntPtr.Zero); Factor = Math.Max(1f, g.DpiX / 96f); } catch { Factor = 1f; } }
    }

    /// <summary>Für Controls, die erst nach dem Start entstehen (z. B. Kacheln) und deshalb nicht von der Fenster-Skalierung erfasst werden.</summary>
    public static int Px(int n) => (int)Math.Round(n * Factor);
    public static float Pt(float size) => Simulated ? size * Factor : size;   // echte Skalierung übernimmt Windows bei Punkt-Schriften

    /// <summary>Fenster skalieren (echte DPI: automatisch; simuliert: von Hand) und nie größer als der Bildschirm machen.</summary>
    public static void Fit(Form f)
    {
        if (Simulated) f.Scale(new SizeF(Factor, Factor));
        else { f.AutoScaleDimensions = new SizeF(96f, 96f); f.AutoScaleMode = AutoScaleMode.Dpi; }
        f.Shown += (_, __) =>
        {
            var wa = Screen.FromControl(f).WorkingArea;
            if (f.Width > wa.Width || f.Height > wa.Height) f.Size = new Size(Math.Min(f.Width, wa.Width), Math.Min(f.Height, wa.Height));
            if (f.Right > wa.Right || f.Bottom > wa.Bottom || f.Left < wa.Left || f.Top < wa.Top) f.Location = new Point(Math.Max(wa.Left, Math.Min(f.Left, wa.Right - f.Width)), Math.Max(wa.Top, Math.Min(f.Top, wa.Bottom - f.Height)));
        };
    }

    static Icon? _icon;
    /// <summary>Programm-Symbol (eingebettet) für Fenster/Dialoge.</summary>
    public static Icon? AppIcon
    {
        get
        {
            if (_icon != null) return _icon;
            try { using var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"); if (s != null) _icon = new Icon(s); } catch { }
            return _icon;
        }
    }

    public static Font Font(float size = 10f, bool bold = false) => new Font("Segoe UI", Pt(size), bold ? FontStyle.Bold : FontStyle.Regular);

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath(); float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
        return p;
    }

    public static Button Primary(string text, int w, int h, Color? color = null) => new ModernButton(ModernButton.K.Primary, text, w, h) { Fill = color ?? Accent };
    public static Button Outline(string text, int w, int h) => new ModernButton(ModernButton.K.Outline, text, w, h);
    /// <summary>Schlichter Text-Knopf ohne Rahmen.</summary>
    public static Button Ghost(string text, int w, int h) => new ModernButton(ModernButton.K.Ghost, text, w, h);

    public static Button Icon(string glyph, string tip)
    {
        var b = new ModernButton(ModernButton.K.Icon, glyph, 28, 28) { TabStop = false };
        new ToolTip { InitialDelay = 300 }.SetToolTip(b, tip); return b;
    }

    /// <summary>Kleiner „?“-Knopf: Klick zeigt eine kurze Erklärung. Hält die Oberfläche frei von langen Texten.</summary>
    public static Control Help(string title, string text) => new HelpButton(title, text);

    public static TextBox Field(int w) => new TextBox { Width = w, Font = Font(10.5f), BorderStyle = BorderStyle.FixedSingle };
}

/// <summary>Flacher, gerundeter Knopf mit Hover-/Gedrückt-Zustand; ersetzt den eckigen Standard-Knopf.</summary>
sealed class ModernButton : Button
{
    public enum K { Primary, Outline, Ghost, Icon }
    public K Kind; public Color Fill = Theme.Accent;
    bool _hover, _down;

    public ModernButton(K kind, string text, int w, int h)
    {
        Kind = kind; Text = text; Width = w; Height = h;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; BackColor = Color.Transparent; Cursor = Cursors.Hand; UseMnemonic = false;
        Font = kind == K.Icon ? new Font("Segoe UI Symbol", Theme.Pt(10f)) : Theme.Font(kind == K.Primary ? 10.5f : 10f, kind == K.Primary);
        if (kind != K.Icon) { MinimumSize = new Size(w, h); AutoSize = true; AutoSizeMode = AutoSizeMode.GrowOnly; Padding = new Padding(14, 0, 14, 0); }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (Kind == K.Icon) return Size;
        var t = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        return new Size(t.Width + Padding.Horizontal, Math.Max(t.Height + Theme.Px(12), MinimumSize.Height));
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        try { ButtonRenderer.DrawParentBackground(g, ClientRectangle, this); } catch { g.Clear(Theme.Bg); }
        var r = new RectangleF(1, 1, Width - 3, Height - 3); float rad = Theme.Px(Kind == K.Icon ? 7 : 9);
        bool explicitFore = ForeColor != SystemColors.ControlText && ForeColor != Color.Empty;   // z. B. rot für „Löschen“
        Color fill = Color.Transparent, fore = Theme.Text, border = Color.Transparent;
        switch (Kind)
        {
            case K.Primary:
                fill = !Enabled ? Color.FromArgb(222, 227, 234) : _down ? ControlPaint.Dark(Fill, 0.15f) : _hover ? ControlPaint.Dark(Fill, 0.08f) : Fill;
                fore = Enabled ? Color.White : Color.FromArgb(160, 168, 180); break;
            case K.Outline:
                fill = _down ? Color.FromArgb(214, 228, 252) : _hover ? Theme.AccentSoft : Theme.Surface;
                border = Enabled ? Theme.Accent : Color.FromArgb(200, 206, 216); fore = Enabled ? Theme.Accent : Color.FromArgb(160, 168, 180); break;
            default:   // Ghost, Icon
                fill = _down ? Color.FromArgb(222, 227, 235) : _hover ? Color.FromArgb(233, 237, 243) : Color.Transparent;
                fore = !Enabled ? Color.FromArgb(170, 176, 188) : explicitFore ? ForeColor : (_hover ? Theme.Text : Theme.Muted); break;
        }
        using (var path = Theme.Round(r, rad))
        {
            if (fill.A > 0) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            if (border.A > 0) using (var p = new Pen(border, 1.4f)) g.DrawPath(p, path);
        }
        if (Focused && ShowFocusCues) using (var fp = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), rad + 1)) using (var p = new Pen(Color.FromArgb(120, Theme.Accent), 2f)) g.DrawPath(p, fp);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
}

/// <summary>Rundes „?“: ein Klick öffnet ein kleines Fenster mit der Erklärung (schließt sich beim Wegklicken).</summary>
sealed class HelpButton : Control
{
    readonly string _title, _text; bool _hover;

    public HelpButton(string title, string text)
    {
        _title = title; _text = text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size(22, 22); Cursor = Cursors.Hand; TabStop = true; BackColor = Color.Transparent; Margin = new Padding(6, 0, 0, 0);   // Stil zuerst: sonst lehnt BackColor=Transparent ab
        new ToolTip { InitialDelay = 400 }.SetToolTip(this, "Erklärung anzeigen");
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Pop(); }
    protected override bool IsInputKey(Keys k) => k == Keys.Enter || k == Keys.Space || base.IsInputKey(k);
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; Pop(); } base.OnKeyDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        try { ButtonRenderer.DrawParentBackground(g, ClientRectangle, this); } catch { }
        var r = new RectangleF(1.5f, 1.5f, Width - 4, Height - 4);
        if (_hover) using (var b = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(b, r);
        using (var p = new Pen(_hover || Focused ? Theme.Accent : Color.FromArgb(160, 168, 182), 1.5f)) g.DrawEllipse(p, r);
        using var f = new Font("Segoe UI", Theme.Pt(8.5f), FontStyle.Bold);
        TextRenderer.DrawText(g, "?", f, ClientRectangle, _hover || Focused ? Theme.Accent : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    void Pop()
    {
        var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, BackColor = Theme.Surface, Padding = new Padding(Theme.Px(14), Theme.Px(12), Theme.Px(14), Theme.Px(14)) };
        int max = Theme.Px(330);
        if (_title.Length > 0) t.Controls.Add(new Label { Text = _title, Font = Theme.Font(10.5f, true), ForeColor = Theme.Text, AutoSize = true, MaximumSize = new Size(max, 0), Margin = new Padding(0, 0, 0, Theme.Px(6)), UseMnemonic = false });
        t.Controls.Add(new Label { Text = _text, Font = Theme.Font(9.5f), ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(max, 0), Margin = Padding.Empty, UseMnemonic = false });
        var dd = new ToolStripDropDown { Padding = Padding.Empty, DropShadowEnabled = true, BackColor = Theme.Surface, AutoClose = true };
        dd.Items.Add(new ToolStripControlHost(t) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = true });
        dd.Show(this, new Point(0, Height + Theme.Px(6)));
    }
}

/// <summary>Kleine Status-Pille („Erkannt“, „Bitte prüfen“ …).</summary>
sealed class Chip : Control
{
    Color _fore = Theme.Muted, _back = Theme.SurfaceAlt;

    public Chip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent; Font = Theme.Font(8.5f, true); Height = 22;
    }

    public void Set(string text, Color fore, Color back) { Text = text; _fore = fore; _back = back; Width = GetPreferredSize(Size.Empty).Width; Invalidate(); }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var t = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        return new Size(t.Width + Theme.Px(22), Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        try { ButtonRenderer.DrawParentBackground(g, ClientRectangle, this); } catch { }
        using (var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f)) using (var b = new SolidBrush(_back)) g.FillPath(b, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, _fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Runde Nummern-Marke für die drei Schritte.</summary>
sealed class StepBadge : Control
{
    public StepBadge(string n)
    {
        Text = n; Size = new Size(34, 34);
        // transparent zeichnen, sonst erscheint hinter dem Kreis ein Rechteck in der Standard-Hintergrundfarbe
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillEllipse(b, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, Text, Theme.Font(11f, true), new Rectangle(0, 0, Width, Height), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
