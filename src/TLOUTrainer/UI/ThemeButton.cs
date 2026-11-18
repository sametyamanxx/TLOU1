using System.Drawing.Drawing2D;

namespace TLOUTrainer.UI;

/// <summary>Flat terminal-style button: bracketed label, accent on hover.</summary>
public sealed class ThemeButton : Control
{
    public bool Accent { get; set; }
    public string Label { get; set; } = "";

    private bool _hover;
    private bool _pressed;

    public ThemeButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        BackColor = Theme.Back;
        Cursor = Cursors.Hand;
        Height = 28;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Focus(); Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Back);

        bool hot = _hover || Focused;
        var body = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill = _pressed ? Theme.Panel : hot ? Theme.PanelHot : Theme.BackDeep;
        Color border = Accent ? Theme.AccentSoft : hot ? Theme.BorderHot : Theme.Border;
        Theme.FillRound(g, body, 5, fill);
        Theme.StrokeRound(g, body, 5, border);

        Color text = Accent ? Theme.Accent : hot ? Theme.Text : Theme.Muted;
        TextRenderer.DrawText(g, Label, Theme.Chip, body, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        using var pen = new Pen(Accent ? Theme.AccentSoft : Theme.Border);
        g.DrawLine(pen, 8, 3, 8, 5);
        g.DrawLine(pen, 8, 3, 10, 3);
        g.DrawLine(pen, Width - 9, Height - 4, Width - 9, Height - 6);
        g.DrawLine(pen, Width - 9, Height - 4, Width - 11, Height - 4);
    }
}

/// <summary>High-contrast title-bar button; distinct hide and close treatments.</summary>
public sealed class ChromeButton : Control
{
    public string Glyph { get; set; } = "×";
    public bool Danger { get; set; }

    private bool _hover;
    private bool _pressed;

    public ChromeButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        BackColor = Theme.BackDeep;
        Cursor = Cursors.Hand;
        Size = new Size(34, 24);
        AccessibleRole = AccessibleRole.PushButton;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Focus(); Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.BackDeep);

        bool hot = _hover || Focused;
        Color ink = Danger ? Theme.Danger : Theme.Accent;
        var body = new Rectangle(0, 0, Width, Height);
        Color fill = Theme.Mix(Theme.BackDeep, ink, _pressed ? 0.07 : hot ? 0.20 : 0.10);
        Theme.FillRound(g, body, 5, fill);
        Theme.StrokeRound(g, body, 5, Theme.Mix(Theme.BackDeep, ink, hot ? 0.75 : 0.45));
        if (Danger)
        {
            using var cross = new Pen(hot ? Color.White : Theme.Danger, 1.5f)
            {
                StartCap = LineCap.Round, EndCap = LineCap.Round
            };
            float x = (Width - 1) / 2f, y = (Height - 1) / 2f;
            g.DrawLine(cross, x - 4, y - 4, x + 4, y + 4);
            g.DrawLine(cross, x + 4, y - 4, x - 4, y + 4);
        }
        else
        {
            using var line = new Pen(Theme.Accent, 1.5f)
            {
                StartCap = LineCap.Round, EndCap = LineCap.Round
            };
            float x = (Width - 1) / 2f, y = (Height - 1) / 2f + 1;
            g.DrawLine(line, x - 5, y, x + 5, y);
        }
    }
}
