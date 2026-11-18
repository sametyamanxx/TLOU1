namespace TLOUTrainer.UI;

/// <summary>Replacement for MessageBox so dialogs stay inside the terminal theme.</summary>
internal static class ThemeDialog
{
    public static void Info(IWin32Window? owner, string title, string message) =>
        Show(owner, title, message, false);

    public static void Error(IWin32Window? owner, string title, string message) =>
        Show(owner, title, message, true);

    private static void Show(IWin32Window? owner, string title, string message, bool error)
    {
        using var dialog = new DialogForm(title, message, error);
        if (owner is Form { IsDisposed: false } form && form.Visible) dialog.ShowDialog(form);
        else dialog.ShowDialog();
    }

    private sealed class DialogForm : Form
    {
        private const int TextWidth = 386;
        private const int HeaderHeight = 62;

        private readonly string _title;
        private readonly string _message;
        private readonly bool _error;
        private readonly ThemeButton _ok;

        public DialogForm(string title, string message, bool error)
        {
            _title = title;
            _message = message;
            _error = error;
            Text = title;
            AccessibleName = title;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.Mono;
            DoubleBuffered = true;
            KeyPreview = true;

            int textHeight = TextRenderer.MeasureText(message, Theme.MonoSmall,
                new Size(TextWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            ClientSize = new Size(430, Math.Max(196, HeaderHeight + 24 + textHeight + 56));

            _ok = new ThemeButton
            {
                Label = "TAMAM",
                Accent = true,
                Location = new Point(430 - 22 - 112, ClientSize.Height - 48),
                Size = new Size(112, 30)
            };
            _ok.Click += (_, _) => Close();
            Controls.Add(_ok);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Escape or Keys.Enter)
            {
                Close();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Grid(g, new Rectangle(0, HeaderHeight, Width, Height - HeaderHeight));

            var frame = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var pen = new Pen(_error ? Color.FromArgb(90, Theme.Danger) : Theme.Border))
                g.DrawRectangle(pen, frame);
            Theme.Brackets(g, new Rectangle(3, 3, Width - 7, Height - 7),
                _error ? Theme.Mix(Theme.Danger, Theme.Back, 0.35) : Theme.AccentSoft, 12);

            LogoMark.Paint(g, new RectangleF(22, 17, 30, 30));

            TextRenderer.DrawText(g, _title, Theme.Title, new Point(62, 19),
                _error ? Theme.Text : Theme.Text, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _error ? "// UYARI" : "// BİLGİ", Theme.MonoTiny,
                new Rectangle(Width - 140, 23, 118, 14),
                _error ? Theme.Danger : Theme.AccentSoft,
                TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "TLOUP1", Theme.MonoTiny, new Point(63, 36),
                Theme.Dim, TextFormatFlags.NoPrefix);

            Theme.Hairline(g, 22, Width - 23, HeaderHeight, Theme.Border);

            TextRenderer.DrawText(g, _message, Theme.MonoSmall,
                new Rectangle(23, HeaderHeight + 16, TextWidth, Height - HeaderHeight - 62),
                Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}
