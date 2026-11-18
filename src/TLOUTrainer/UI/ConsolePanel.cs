using System.Drawing.Drawing2D;

namespace TLOUTrainer.UI;

/// <summary>
/// Self-drawn console output. Avoiding a TextBox keeps the scrollbar and the caret inside
/// the theme, and redrawing only on append costs nothing while the game runs.
/// </summary>
public sealed class ConsolePanel : Panel
{
    private sealed record Entry(string Time, string Text, Color Color);

    private const int HeaderHeight = 22;
    private const int PadX = 12;
    private const int PadY = 8;
    private const int LineHeight = 14;
    private const int MaxEntries = 300;

    private readonly List<Entry> _entries = [];
    private int _scroll;

    public string Title { get; set; } = "ÇIKTI";
    public string Source { get; set; } = "OTURUM / RAM";

    public ConsolePanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Back;
        Height = 108;
    }

    public void Append(string text, Color? color = null)
    {
        _entries.Add(new Entry(DateTime.Now.ToString("HH:mm:ss"), text, color ?? Theme.Muted));
        if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
        Invalidate();
    }

    public void Clear()
    {
        _entries.Clear();
        _scroll = 0;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int step = e.Delta > 0 ? 3 : -3;
        _scroll = Math.Clamp(_scroll + step, 0, Math.Max(0, BuildLines().Count - VisibleLines()));
        Invalidate();
        base.OnMouseWheel(e);
    }

    private int VisibleLines() => Math.Max(1, (Height - HeaderHeight - PadY) / LineHeight);

    private List<(string Prefix, string Text, Color Color)> BuildLines()
    {
        int charWidth = Math.Max(1, TextRenderer.MeasureText("0000000000000000", Theme.MonoSmall,
            Size.Empty, TextFormatFlags.NoPadding).Width / 16);
        int capacity = Math.Max(16, (Width - PadX * 2 - 10) / charWidth);
        var lines = new List<(string, string, Color)>();
        foreach (var entry in _entries)
        {
            string prefix = $"[{entry.Time}] ";
            int width = Math.Max(8, capacity - prefix.Length);
            bool first = true;
            foreach (var chunk in Wrap(entry.Text, width))
            {
                lines.Add((first ? prefix : "  ", chunk, entry.Color));
                first = false;
            }
            if (first) lines.Add((prefix, "", entry.Color));
        }
        return lines;
    }

    /// <summary>Word wrap that only breaks a word when it cannot fit on its own line.</summary>
    private static IEnumerable<string> Wrap(string text, int width)
    {
        int index = 0;
        while (index < text.Length)
        {
            int take = Math.Min(width, text.Length - index);
            if (index + take < text.Length)
            {
                int space = text.LastIndexOf(' ', index + take - 1, take);
                if (space > index) take = space - index + 1;
            }
            yield return text.Substring(index, take).TrimEnd();
            index += take;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Back);

        var body = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.Round(body, 7))
        {
            using var brush = new SolidBrush(Theme.BackDeep);
            g.FillPath(brush, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, $"> {Title}", Theme.MonoTiny, new Rectangle(PadX, 5, 120, 13),
            Theme.AccentSoft, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Source, Theme.MonoTiny, new Rectangle(Width - 112, 5, 100, 13),
            Theme.Dim, TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        Theme.Hairline(g, PadX, Width - PadX - 1, HeaderHeight, Theme.Border);

        var lines = BuildLines();
        int visible = VisibleLines();
        int maxScroll = Math.Max(0, lines.Count - visible);
        if (_scroll > maxScroll) _scroll = maxScroll;
        int start = Math.Max(0, lines.Count - visible - _scroll);

        g.SetClip(new Rectangle(0, HeaderHeight + 1, Width - 10, Height - HeaderHeight - 2));
        int y = HeaderHeight + 4;
        for (int i = start; i < lines.Count && i < start + visible; i++)
        {
            var (prefix, text, color) = lines[i];
            int px = PadX;
            TextRenderer.DrawText(g, prefix, Theme.MonoSmall, new Point(px, y), Theme.Dim,
                TextFormatFlags.NoPrefix);
            px += TextRenderer.MeasureText(g, prefix, Theme.MonoSmall).Width - 4;
            TextRenderer.DrawText(g, text, Theme.MonoSmall, new Point(px, y), color,
                TextFormatFlags.NoPrefix);
            y += LineHeight;
        }
        g.ResetClip();

        if (maxScroll > 0)
        {
            int trackTop = HeaderHeight + 5;
            int trackHeight = Height - HeaderHeight - 12;
            int thumb = Math.Max(12, (int)(trackHeight * (visible / (double)lines.Count)));
            int pos = trackTop + (int)((trackHeight - thumb) * ((lines.Count - visible - _scroll) / (double)Math.Max(1, maxScroll)));
            Theme.FillRound(g, new Rectangle(Width - 8, trackTop, 3, trackHeight), 2, Theme.Border);
            Theme.FillRound(g, new Rectangle(Width - 8, pos, 3, thumb), 2, Theme.AccentSoft);
        }
    }
}
