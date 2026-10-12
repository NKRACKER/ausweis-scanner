using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AusweisScanner.Core;

namespace AusweisScanner.App;

/// <summary>Kachel für eine erkannte Karte: Vorschau, zwei Namensfelder, Status. Farbiger Streifen links zeigt auf einen Blick, ob geprüft werden muss.</summary>
sealed class CardControl : Panel
{
    public CardItem Item { get; }
    public event Action<CardControl>? RemoveRequested, PreviewRequested, EnterOnLast;
    public event Action? NameEdited;
    public event Action<CardControl, int>? RotateRequested;

    readonly PictureBox _pic = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Hand, BackColor = Color.FromArgb(243, 244, 246) };
    readonly TextBox _last = Theme.Field(150), _first = Theme.Field(150);
    readonly Label _status = new Label { AutoEllipsis = true, Font = Theme.Font(9f), BackColor = Color.Transparent };
    readonly Button _more = Theme.Icon("⋯", "Weitere Vorschläge, Drehen"), _swap = Theme.Icon("⇄", "Nachname und Vorname tauschen"), _rot = Theme.Icon("⟳", "90° drehen"), _del = Theme.Icon("✕", "Diesen Ausweis nicht speichern");
    bool _binding, _dup; int _number; Color _stripe = Theme.Muted;

    public CardControl(CardItem item, int number)
    {
        Item = item; Size = new Size(350, 296); Margin = new Padding(10); BackColor = Theme.Surface; DoubleBuffered = true;
        _pic.SetBounds(16, 10, 326, 184); _pic.Click += (_, __) => PreviewRequested?.Invoke(this);
        new ToolTip().SetToolTip(_pic, "Klicken für Großansicht");
        _status.SetBounds(16, 200, 190, 22);
        _del.Location = new Point(316, 198); _rot.Location = new Point(290, 198); _swap.Location = new Point(264, 198); _more.Location = new Point(238, 198);
        _status.Width = 220;
        var l1 = Small("Nachname", 16, 228); var l2 = Small("Vorname", 182, 228);
        _last.Location = new Point(16, 246); _first.Location = new Point(182, 246);
        Controls.AddRange(new Control[] { _pic, _status, _more, _swap, _rot, _del, l1, l2, _last, _first });

        _last.TextChanged += (_, __) => { if (!_binding) { Item.Last = _last.Text; Touched(); NameEdited?.Invoke(); } };
        _first.TextChanged += (_, __) => { if (!_binding) { Item.First = _first.Text; Touched(); NameEdited?.Invoke(); } };
        _last.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _first.Focus(); _first.SelectAll(); } };
        _first.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EnterOnLast?.Invoke(this); } };
        _last.Enter += (_, __) => _last.SelectAll(); _first.Enter += (_, __) => _first.SelectAll();
        _swap.Click += (_, __) => { var t = _last.Text; _last.Text = _first.Text; _first.Text = t; };
        _rot.Click += (_, __) => RotateRequested?.Invoke(this, 1);
        _del.Click += (_, __) => RemoveRequested?.Invoke(this);
        _more.Click += (_, __) => ShowMenu();
        RefreshFromItem(number);
    }

    static Label Small(string t, int x, int y) => new Label { Text = t, AutoSize = true, Left = x, Top = y, Font = Theme.Font(8.5f), ForeColor = Theme.Muted, BackColor = Color.Transparent };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        using (var b = new SolidBrush(_stripe)) e.Graphics.FillRectangle(b, 0, 0, 6, Height);
    }

    public void RefreshFromItem(int? number = null)
    {
        if (number.HasValue) _number = number.Value;
        _binding = true; _last.Text = Item.Last; _first.Text = Item.First; _binding = false;
        _pic.Image?.Dispose(); _pic.Image = BitmapCodec.Thumbnail(Item.Image, 326, 184);
        UpdateStatus();
    }

    void Touched() { Item.Source = "von Hand"; Item.Score = string.IsNullOrWhiteSpace(Item.Last) ? 0 : 0.99; UpdateStatus(); }
    public void SetNumber(int n) { _number = n; UpdateStatus(); }
    public void MarkDuplicate(bool dup) { _dup = dup; UpdateStatus(); }

    void UpdateStatus()
    {
        string text; Color c;
        if (string.IsNullOrWhiteSpace(Item.Last)) { text = "Kein Name erkannt – bitte eintippen"; c = Theme.Bad; }
        else if (Item.Source == "von Hand") { text = "Eingetragen"; c = Theme.Ok; }
        else if (Item.Score >= 0.7) { text = "Erkannt"; c = Theme.Ok; }
        else { text = "Bitte prüfen"; c = Theme.Warn; }
        if (_dup) { text = "Name doppelt (wird mit _2 ergänzt)"; c = Theme.Warn; }
        _status.Text = $"#{_number}   {text}"; _status.ForeColor = c; _stripe = c; Invalidate();
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
        m.Items.Add("Großansicht", null, (_, __) => PreviewRequested?.Invoke(this));
        m.Show(_more, new Point(0, _more.Height));
    }

    public void FocusLast() { _last.Focus(); _last.SelectAll(); }
}
