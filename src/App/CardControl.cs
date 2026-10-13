using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>Vorschau des ausgeschnittenen Ausweises: Nummer links oben, beim Überfahren der Hinweis „Zuschnitt anpassen“. Bild wird ganz gezeigt, ohne Zuschneiden.</summary>
sealed class ThumbBox : Control
{
    Bitmap? _img; bool _hover; int _number;
    public Bitmap? Image { get => _img; set { _img?.Dispose(); _img = value; Invalidate(); } }
    public int Number { get => _number; set { _number = value; Invalidate(); } }

    public ThumbBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand; BackColor = Theme.SurfaceAlt;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var box = new RectangleF(0, 0, Width - 1, Height - 1);
        using var clip = Theme.Round(box, Theme.Px(9));
        g.SetClip(clip); g.Clear(Theme.SurfaceAlt);
        if (_img != null)
        {
            float k = Math.Min(Width / (float)_img.Width, Height / (float)_img.Height), w = _img.Width * k, h = _img.Height * k;
            g.DrawImage(_img, (Width - w) / 2, (Height - h) / 2, w, h);
        }
        if (_hover)
        {
            int bh = Theme.Px(40);
            using (var b = new LinearGradientBrush(new Rectangle(0, Height - bh, Width, bh + 1), Color.FromArgb(0, 15, 23, 42), Color.FromArgb(190, 15, 23, 42), 90f)) g.FillRectangle(b, 0, Height - bh, Width, bh + 1);
            TextRenderer.DrawText(g, "✂  Zuschnitt anpassen", Theme.Font(9.5f, true), new Rectangle(0, Height - bh, Width, bh), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        g.ResetClip();
        if (_number > 0)
        {
            int d = Theme.Px(26), m = Theme.Px(8);
            using (var b = new SolidBrush(Color.FromArgb(235, Theme.Accent))) g.FillEllipse(b, m, m, d, d);
            TextRenderer.DrawText(g, _number.ToString(), Theme.Font(9.5f, true), new Rectangle(m, m, d, d), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        using (var p = new Pen(Theme.Line)) g.DrawPath(p, clip);
    }

    protected override void Dispose(bool disposing) { if (disposing) _img?.Dispose(); base.Dispose(disposing); }
}

/// <summary>Kachel für eine erkannte Karte: Vorschau, Status, zwei Namensfelder. Ein farbiger Rahmen zeigt auf einen Blick, ob geprüft werden muss.</summary>
sealed class CardControl : Panel
{
    public CardItem Item { get; }
    public event Action<CardControl>? RemoveRequested, PreviewRequested, EnterOnLast, CropRequested;
    public event Action? NameEdited;
    public event Action<CardControl, int>? RotateRequested;

    readonly ThumbBox _pic = new ThumbBox();
    readonly TextBox _last = Theme.Field(148), _first = Theme.Field(148);
    readonly Chip _chip = new Chip();
    readonly Button _more = Theme.Icon("⋯", "Weitere Namensvorschläge, drehen, Zuschnitt"), _swap = Theme.Icon("⇄", "Nachname und Vorname tauschen"), _rot = Theme.Icon("⟳", "90° drehen"), _del = Theme.Icon("✕", "Diesen Ausweis nicht speichern");
    bool _binding, _dup; Color _border = Theme.Line; float _borderW = 1f; readonly ToolTip _tip = new ToolTip { InitialDelay = 300 };

    public CardControl(CardItem item, int number)
    {
        Item = item; Size = new Size(340, 322); DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent;   // gerundete Fläche selbst zeichnen
        _pic.SetBounds(14, 14, 312, 197); _pic.Click += (_, __) => PreviewRequested?.Invoke(this);
        _tip.SetToolTip(_pic, "Klicken: Großansicht – dort lässt sich der Zuschnitt direkt anpassen");
        _chip.Location = new Point(14, 222);
        _swap.Location = new Point(208, 219); _rot.Location = new Point(238, 219); _more.Location = new Point(268, 219); _del.Location = new Point(298, 219);
        var l1 = Small("Nachname", 14, 256); var l2 = Small("Vorname", 178, 256);
        _last.Location = new Point(14, 274); _first.Location = new Point(178, 274);
        Controls.AddRange(new Control[] { _pic, _chip, _swap, _rot, _more, _del, l1, l2, _last, _first });

        _last.TextChanged += (_, __) => { if (!_binding) { Item.Last = _last.Text; Touched(); NameEdited?.Invoke(); } };
        _first.TextChanged += (_, __) => { if (!_binding) { Item.First = _first.Text; Touched(); NameEdited?.Invoke(); } };
        _last.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _first.Focus(); _first.SelectAll(); } };
        _first.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EnterOnLast?.Invoke(this); } };
        _last.Enter += (_, __) => _last.SelectAll(); _first.Enter += (_, __) => _first.SelectAll();
        _swap.Click += (_, __) => { var t = _last.Text; _last.Text = _first.Text; _first.Text = t; };
        _rot.Click += (_, __) => RotateRequested?.Invoke(this, 1);
        _del.Click += (_, __) => RemoveRequested?.Invoke(this);
        _more.Click += (_, __) => ShowMenu();
        Scale(new SizeF(Theme.Factor, Theme.Factor)); Margin = new Padding(Theme.Px(8));   // Kacheln entstehen erst nach dem Start → nicht von der Fenster-Skalierung erfasst
        _pic.Number = number; RefreshFromItem(number);
    }

    static Label Small(string t, int x, int y) => new Label { Text = t, AutoSize = true, Left = x, Top = y, Font = Theme.Font(8.5f), ForeColor = Theme.Muted, BackColor = Color.Transparent, UseMnemonic = false };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), Theme.Px(12));
        using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
        using (var p = new Pen(_border, _borderW)) g.DrawPath(p, path);
    }

    public void RefreshFromItem(int? number = null)
    {
        if (number.HasValue) _pic.Number = number.Value;
        _binding = true; _last.Text = Item.Last; _first.Text = Item.First; _binding = false;
        _pic.Image = BitmapCodec.Thumbnail(Item.Image, Math.Max(1, _pic.Width), Math.Max(1, _pic.Height));
        UpdateStatus();
    }

    void Touched() { Item.Source = "von Hand"; Item.Score = string.IsNullOrWhiteSpace(Item.Last) ? 0 : 0.99; UpdateStatus(); }
    public void SetNumber(int n) { _pic.Number = n; }
    public void MarkDuplicate(bool dup) { _dup = dup; UpdateStatus(); }

    void UpdateStatus()
    {
        string tip = "";
        if (_dup) { _chip.Set("Name doppelt", Theme.Warn, Theme.WarnSoft); _border = Theme.Warn; _borderW = 1.6f; tip = "Dieser Name kommt mehrfach vor – die Datei bekommt automatisch _2, _3 … angehängt."; }
        else if (string.IsNullOrWhiteSpace(Item.Last)) { _chip.Set("Name fehlt", Theme.Bad, Theme.BadSoft); _border = Theme.Bad; _borderW = 1.6f; tip = "Es wurde kein Name erkannt – bitte eintippen."; }
        else if (Item.Source == "von Hand") { _chip.Set("Eingetragen", Theme.Ok, Theme.OkSoft); _border = Theme.Line; _borderW = 1f; }
        else if (Item.Score >= 0.7) { _chip.Set("Erkannt", Theme.Ok, Theme.OkSoft); _border = Theme.Line; _borderW = 1f; }
        else { _chip.Set("Bitte prüfen", Theme.Warn, Theme.WarnSoft); _border = Theme.Warn; _borderW = 1.6f; tip = "Der Name wurde erkannt, ist aber unsicher – bitte kurz kontrollieren."; }
        _tip.SetToolTip(_chip, tip); Invalidate();
    }

    void ShowMenu()
    {
        var m = new ContextMenuStrip { Font = Theme.Font(9.5f) };
        foreach (var cand in Item.Candidates.Take(6))
        {
            var cc = cand;
            m.Items.Add($"{cc.Name}   ({cc.Source})", null, (_, __) =>
            { _binding = true; _last.Text = cc.Name.Last; _first.Text = cc.Name.First; _binding = false; Item.Last = cc.Name.Last; Item.First = cc.Name.First; Item.Source = cc.Source; Item.Score = cc.Score; UpdateStatus(); NameEdited?.Invoke(); });
        }
        if (Item.Candidates.Count > 0) m.Items.Add(new ToolStripSeparator());
        m.Items.Add("90° nach links drehen", null, (_, __) => RotateRequested?.Invoke(this, 3));
        if (Item.Scan != null) m.Items.Add("Zuschnitt anpassen …", null, (_, __) => CropRequested?.Invoke(this));
        m.Show(_more, new Point(0, _more.Height));
    }

    public void FocusLast() { _last.Focus(); _last.SelectAll(); }
}
