using System.Drawing.Drawing2D;

namespace TLOUTrainer.UI;

/// <summary>Connection panel: status dot, bracketed tag, title and wrapped detail line.</summary>
public sealed class CardPanel : Panel
{
    public Color DotColor { get; set; } = Theme.Warn;
    public string Badge { get; set; } = "SYS";
    public string Title { get; set; } = "";
    public string Sub { get; set; } = "";

    public CardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Back;
        Height = 66;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Back);

        var body = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.Round(body, 7))
        {
            using var brush = new SolidBrush(Theme.Panel);
            g.FillPath(brush, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        Theme.Brackets(g, new Rectangle(2, 2, Width - 5, Height - 5), Theme.Mix(DotColor, Theme.Back, 0.62), 8);

        // Radar dot with a soft ring.
        using (var glow = new SolidBrush(Color.FromArgb(46, DotColor)))
            g.FillEllipse(glow, 17, 27, 18, 18);
        using (var ring = new Pen(Color.FromArgb(120, DotColor)))
            g.DrawEllipse(ring, 20, 30, 12, 12);
        using (var dot = new SolidBrush(DotColor))
            g.FillEllipse(dot, 24, 34, 4, 4);

        TextRenderer.DrawText(g, $"[{Badge}]", Theme.MonoTiny, new Rectangle(46, 8, 120, 14),
            Theme.Dim, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Title, Theme.Title, new Point(46, 21),
            DotColor == Theme.Accent ? Theme.Text : DotColor, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Sub, Theme.MonoSmall, new Rectangle(46, 40, Width - 62, 22),
            Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix |
                         TextFormatFlags.EndEllipsis | TextFormatFlags.TextBoxControl);
    }
}
