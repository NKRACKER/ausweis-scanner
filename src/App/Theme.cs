using System;
using System.Drawing;
using System.Windows.Forms;

namespace AusweisScanner.App;

/// <summary>Einheitliches, ruhiges Erscheinungsbild: viel Weißraum, eine Akzentfarbe, flache Knöpfe.</summary>
static class Theme
{
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
    static float Pt(float size) => Simulated ? size * Factor : size;   // echte Skalierung übernimmt Windows bei Punkt-Schriften

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

    public static readonly Color Bg = Color.FromArgb(247, 248, 250), Surface = Color.White, Line = Color.FromArgb(229, 231, 235);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235), AccentHover = Color.FromArgb(29, 78, 216);
    public static readonly Color Ok = Color.FromArgb(22, 163, 74), Warn = Color.FromArgb(217, 119, 6), Bad = Color.FromArgb(220, 38, 38);
    public static readonly Color Text = Color.FromArgb(31, 41, 55), Muted = Color.FromArgb(107, 114, 128);

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

    static Button Base(string text, int w, int h)
    {
        // AutoSize (nur wachsen): Knöpfe passen sich Schriftgröße/Skalierung an, Text wird nie abgeschnitten
        var b = new Button { Text = text, Width = w, Height = h, MinimumSize = new Size(w, h), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly, Padding = new Padding(10, 0, 10, 0), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand, Font = Font(10f), TabStop = true, UseMnemonic = false };
        b.FlatAppearance.BorderSize = 0; return b;
    }

    public static Button Primary(string text, int w, int h, Color? color = null)
    {
        var c = color ?? Accent; var b = Base(text, w, h); b.BackColor = c; b.ForeColor = Color.White; b.Font = Font(10.5f, true);
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Dark(c, 0.08f); b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(c, 0.15f); return b;
    }

    public static Button Outline(string text, int w, int h)
    {
        var b = Base(text, w, h); b.BackColor = Surface; b.ForeColor = Accent; b.FlatAppearance.BorderSize = 1; b.FlatAppearance.BorderColor = Accent;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 246, 255); return b;
    }

    /// <summary>Schlichter Text-Knopf ohne Rahmen.</summary>
    public static Button Ghost(string text, int w, int h)
    {
        var b = Base(text, w, h); b.BackColor = Color.Transparent; b.ForeColor = Muted; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 238, 242); return b;
    }

    public static Button Icon(string glyph, string tip)
    {
        var b = Base(glyph, 26, 26); b.BackColor = Color.Transparent; b.ForeColor = Muted; b.Font = new Font("Segoe UI Symbol", Pt(10f)); b.TabStop = false; b.Padding = Padding.Empty; b.AutoSize = false; b.MinimumSize = Size.Empty;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 238, 242); new ToolTip().SetToolTip(b, tip); return b;
    }

    public static TextBox Field(int w) => new TextBox { Width = w, Font = Font(10.5f), BorderStyle = BorderStyle.FixedSingle };
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
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillEllipse(b, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, Text, Theme.Font(11f, true), new Rectangle(0, 0, Width, Height), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
