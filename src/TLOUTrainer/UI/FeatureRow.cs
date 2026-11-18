using System.Drawing.Drawing2D;

namespace TLOUTrainer.UI;

public enum RowState { Off, On, Unavailable }

/// <summary>A themed command row: hotkey chip, title, live detail and a bracketed state.</summary>
public sealed class FeatureRow : Panel
{
    public string Index { get; set; } = "01";
    public string Title { get; set; } = "";
    public string Hotkey { get; set; } = "";
    public string Detail { get; set; } = "";
    public RowState State { get; set; }
    public event EventHandler? Toggled;

    private bool _hover;
    private bool _pressed;

    public FeatureRow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        BackColor = Theme.Back;
        Height = 52;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnClick(EventArgs e)
    {
        if (State != RowState.Unavailable) Toggled?.Invoke(this, EventArgs.Empty);
        base.OnClick(e);
    }

    private bool Active => State == RowState.On;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Back);

        var body = new Rectangle(0, 0, Width - 1, Height - 1);
        bool usable = State != RowState.Unavailable;
        Color fill = _pressed ? Theme.BackDeep
            : _hover && usable ? Theme.PanelHot : Theme.Panel;
        using (var path = Theme.Round(body, 7))
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
            using var pen = new Pen(Active ? Theme.BorderHot
                : _hover && usable ? Theme.Mix(Theme.Border, Theme.BorderHot, 0.5) : Theme.Border);
            g.DrawPath(pen, path);
        }

        Color accent = State switch
        {
            RowState.On => Theme.Accent,
            RowState.Unavailable => Theme.Warn,
            _ => Theme.Border
        };
        using (var bar = new SolidBrush(accent))
            g.FillRectangle(bar, 0, 10, 2, Height - 21);

        // Hotkey chip: [ F1 ]
        var chip = new Rectangle(14, 15, 52, 22);
        Theme.FillRound(g, chip, 4, Theme.BackDeep);
        Theme.StrokeRound(g, chip, 4, Active ? Theme.AccentSoft : Theme.Border);
        TextRenderer.DrawText(g, Hotkey, Theme.Chip, chip,
            Active ? Theme.Accent : Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        TextRenderer.DrawText(g, Title, Theme.Title, new Point(78, 7),
            usable ? Theme.Text : Theme.Dim, TextFormatFlags.NoPrefix);

        var detailRect = new Rectangle(78, 28, Width - 96, 16);
        TextRenderer.DrawText(g, Detail, Theme.MonoSmall, detailRect,
            usable ? Theme.Muted : Theme.Mix(Theme.Muted, Theme.Back, 0.45),
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);

        string stateText = State switch
        {
            RowState.On => "[ AÇIK ]",
            RowState.Unavailable => "[ YOK ]",
            _ => "[ KAPALI ]"
        };
        TextRenderer.DrawText(g, stateText, Theme.Chip, new Rectangle(Width - 128, 8, 114, 18),
            accent == Theme.Border ? Theme.Dim : accent,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
