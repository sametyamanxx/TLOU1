using System.Drawing.Drawing2D;

namespace TLOUTrainer.UI;

/// <summary>Single source of truth for the terminal look used by every window.</summary>
internal static class Theme
{
    public static readonly Color Back = Color.FromArgb(17, 15, 13);
    public static readonly Color BackDeep = Color.FromArgb(11, 10, 9);
    public static readonly Color Panel = Color.FromArgb(25, 22, 19);
    public static readonly Color PanelHot = Color.FromArgb(36, 29, 23);
    public static readonly Color Border = Color.FromArgb(57, 47, 38);
    public static readonly Color BorderHot = Color.FromArgb(132, 91, 51);
    public static readonly Color Accent = Color.FromArgb(232, 174, 102);
    public static readonly Color AccentSoft = Color.FromArgb(177, 129, 77);
    public static readonly Color Text = Color.FromArgb(239, 230, 215);
    public static readonly Color Muted = Color.FromArgb(175, 160, 141);
    public static readonly Color Dim = Color.FromArgb(118, 101, 83);
    public static readonly Color Danger = Color.FromArgb(255, 93, 93);
    public static readonly Color Warn = Color.FromArgb(255, 184, 84);

    public static readonly Font Mono = new("Consolas", 9f);
    public static readonly Font MonoSmall = new("Consolas", 8.25f);
    public static readonly Font MonoTiny = new("Consolas", 7.5f);
    public static readonly Font MonoBold = new("Consolas", 9f, FontStyle.Bold);
    public static readonly Font Brand = new("Consolas", 14f, FontStyle.Bold);
    public static readonly Font Title = new("Consolas", 10f, FontStyle.Bold);
    public static readonly Font Chip = new("Consolas", 8.25f, FontStyle.Bold);

    public static GraphicsPath Round(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        var r = bounds;
        r.Width = Math.Max(d + 1, r.Width);
        r.Height = Math.Max(d + 1, r.Height);
        r.Width -= 1;
        r.Height -= 1;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, Rectangle bounds, int radius, Color color)
    {
        using var path = Round(bounds, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void StrokeRound(Graphics g, Rectangle bounds, int radius, Color color)
    {
        using var path = Round(bounds, radius);
        using var pen = new Pen(color);
        g.DrawPath(pen, path);
    }

    /// <summary>Faint dot grid; drawn only on repaint, so it costs nothing at runtime.</summary>
    public static void Grid(Graphics g, Rectangle area)
    {
        using var dot = new SolidBrush(Color.FromArgb(38, 31, 25));
        for (int y = area.Top + 14; y < area.Bottom; y += 24)
            for (int x = area.Left + 14; x < area.Right; x += 24)
                g.FillRectangle(dot, x, y, 1, 1);
    }

    /// <summary>Corner brackets, the signature frame of a terminal panel.</summary>
    public static void Brackets(Graphics g, Rectangle r, Color color, int len = 9)
    {
        using var pen = new Pen(color);
        g.DrawLine(pen, r.Left, r.Top, r.Left + len, r.Top);
        g.DrawLine(pen, r.Left, r.Top, r.Left, r.Top + len);
        g.DrawLine(pen, r.Right - len, r.Top, r.Right, r.Top);
        g.DrawLine(pen, r.Right, r.Top, r.Right, r.Top + len);
        g.DrawLine(pen, r.Left, r.Bottom, r.Left + len, r.Bottom);
        g.DrawLine(pen, r.Left, r.Bottom - len, r.Left, r.Bottom);
        g.DrawLine(pen, r.Right - len, r.Bottom, r.Right, r.Bottom);
        g.DrawLine(pen, r.Right, r.Bottom - len, r.Right, r.Bottom);
    }

    public static void Hairline(Graphics g, int x1, int x2, int y, Color color)
    {
        using var pen = new Pen(color);
        g.DrawLine(pen, x1, y, x2, y);
    }

    public static Color Mix(Color a, Color b, double t)
    {
        static int L(int c, int d, double f) => (int)Math.Round(c * (1 - f) + d * f);
        return Color.FromArgb(L(a.R, b.R, t), L(a.G, b.G, t), L(a.B, b.B, t));
    }
}
