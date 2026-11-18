using System.Diagnostics;
using TLOUTrainer.Core;
using TLOUTrainer.Infrastructure;
using TLOUTrainer.Native;

namespace TLOUTrainer.UI;

public sealed class MainForm : Form
{
    private const int IdInsert = 1, IdAmmo = 2, IdHealth = 3;
    private const int TitleBarHeight = 34;

    private readonly FeatureRow _ammoRow;
    private readonly FeatureRow _healthRow;
    private readonly CardPanel _card;
    private readonly ConsolePanel _console;
    private readonly ChromeButton _hideButton;
    private readonly ChromeButton _close;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly CancellationTokenSource _closing = new();

    private AnalysisResult? _analysis;
    private TrainerSession? _session;
    private bool _busy;
    private bool _hotkeysOk;
    private bool _cardError;
    private DateTime _retryAt = DateTime.MinValue;

    public MainForm()
    {
        Text = "The Last of Us™ Part I";
        ClientSize = new Size(500, 448);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Theme.Mono;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        KeyPreview = true;

        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch (Exception) { }

        _card = new CardPanel
        {
            Location = new Point(22, 100), Size = new Size(456, 66),
            Title = "Oyun aranıyor…", Sub = "Yerel analiz başlatılıyor…"
        };

        _ammoRow = new FeatureRow
        {
            Index = "01",
            Title = "Sınırsız Mühimmat",
            Hotkey = "F1",
            Detail = "analiz bekleniyor…",
            Location = new Point(22, 188),
            Size = new Size(456, 52)
        };
        _ammoRow.Toggled += (_, _) => ToggleFeature(0);

        _healthRow = new FeatureRow
        {
            Index = "02",
            Title = "Sınırsız Sağlık",
            Hotkey = "F2",
            Detail = "analiz bekleniyor…",
            Location = new Point(22, 246),
            Size = new Size(456, 52)
        };
        _healthRow.Toggled += (_, _) => ToggleFeature(1);

        _console = new ConsolePanel
        {
            Title = "ÇIKTI",
            Location = new Point(22, 308),
            Size = new Size(456, 108)
        };

        _hideButton = new ChromeButton
        {
            Glyph = "—", Location = new Point(ClientSize.Width - 90, 5),
            Size = new Size(34, 24), Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AccessibleName = "Paneli gizle. Insert ile geri aç.",
            AccessibleDescription = "Uygulamayı kapatmadan paneli gizler."
        };
        _hideButton.Click += (_, _) => ToggleVisibility();
        _close = new ChromeButton
        {
            Glyph = "×", Danger = true, Location = new Point(ClientSize.Width - 48, 5),
            Size = new Size(34, 24), Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AccessibleName = "TLOUP1 uygulamasını kapat",
            AccessibleDescription = "Kod yamalarını geri alır ve uygulamayı kapatır."
        };
        _close.Click += (_, _) => Close();

        Controls.AddRange([_card, _ammoRow, _healthRow, _console,
                            _hideButton, _close]);

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (_, _) => Poll();
    }

    private Rectangle DragArea => new(0, 0, ClientSize.Width - 100, TitleBarHeight);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Back);
        Theme.Grid(g, new Rectangle(0, TitleBarHeight, Width, Height - TitleBarHeight));

        using (var bar = new SolidBrush(Theme.BackDeep))
            g.FillRectangle(bar, 0, 0, Width, TitleBarHeight);
        Theme.Hairline(g, 0, Width, TitleBarHeight, Theme.Border);

        // Emblem + wordmark.
        LogoMark.Paint(g, new RectangleF(22, 42, 38, 38));
        TextRenderer.DrawText(g, "The Last of Us™ Part I", Theme.Brand,
            new Rectangle(72, 42, Width - 94, 38), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        // Section header.
        int labelWidth = TextRenderer.MeasureText(g, "KOMUTLAR", Theme.MonoTiny).Width;
        TextRenderer.DrawText(g, "KOMUTLAR", Theme.MonoTiny, new Point(24, 174), Theme.AccentSoft,
            TextFormatFlags.NoPrefix);
        Theme.Hairline(g, 24 + labelWidth + 12, Width - 24, 180, Theme.Border);

        // Build version.
        TextRenderer.DrawText(g, "v1.0", Theme.MonoTiny, new Rectangle(Width - 80, 426, 56, 14),
            Theme.Dim, TextFormatFlags.Right | TextFormatFlags.NoPrefix);

        using var frame = new Pen(Theme.Border);
        g.DrawRectangle(frame, 0, 0, Width - 1, Height - 1);
        Theme.Brackets(g, new Rectangle(1, 1, Width - 3, Height - 3), Theme.AccentSoft, 12);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_hotkeysOk) return;
        var failed = new List<string>();
        if (!NativeMethods.RegisterHotKey(Handle, IdInsert, NativeMethods.ModNoRepeat, 0x2D)) failed.Add("Insert");
        if (!NativeMethods.RegisterHotKey(Handle, IdAmmo, NativeMethods.ModNoRepeat, (uint)Keys.F1)) failed.Add("F1");
        if (!NativeMethods.RegisterHotKey(Handle, IdHealth, NativeMethods.ModNoRepeat, (uint)Keys.F2)) failed.Add("F2");
        _hotkeysOk = failed.Count == 0;
        if (!_hotkeysOk)
        {
            foreach (var (id, key) in new[]
                     {
                          (IdInsert, "Insert"), (IdAmmo, "F1"), (IdHealth, "F2")
                     })
                if (failed.Contains(key)) NativeMethods.UnregisterHotKey(Handle, id);
            AppendLog("Kısayol kaydedilemedi: " + string.Join(", ", failed) +
                      " başka bir program tarafından kullanılıyor.", Theme.Warn);
        }
        else
        {
            AppendLog("Kısayollar hazır: Insert · F1 · F2");
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            AppendLog("Bağlantı işlemi tamamlanınca kapatmayı tekrar dene.", Theme.Warn);
            return;
        }
        _timer.Stop();
        _closing.Cancel();
        NativeMethods.UnregisterHotKey(Handle, IdInsert);
        NativeMethods.UnregisterHotKey(Handle, IdAmmo);
        NativeMethods.UnregisterHotKey(Handle, IdHealth);
        _session?.Dispose();
        _session = null;
        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
        AppendLog("TLOUP1 başladı; oyun bekleniyor…");
        TryAttach();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84, WM_NCLBUTTONDBLCLK = 0xA3;
        const int HTCLIENT = 1, HTCAPTION = 2;

        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HTCLIENT)
            {
                long lp = m.LParam.ToInt64();
                var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                if (DragArea.Contains(PointToClient(screen))) m.Result = HTCAPTION;
            }
            return;
        }
        if (m.Msg == WM_NCLBUTTONDBLCLK)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == (int)NativeMethods.WmHotkey)
        {
            switch (m.WParam.ToInt32())
            {
                case IdInsert: ToggleVisibility(); return;
                case IdAmmo: ToggleFeature(0); return;
                case IdHealth: ToggleFeature(1); return;
            }
        }
        base.WndProc(ref m);
    }

    private void ToggleVisibility()
    {
        if (Visible)
        {
            Hide();
            FocusGame();
        }
        else
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }
    }

    private void FocusGame()
    {
        try
        {
            IntPtr handle = _session?.WindowHandle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
            {
                using var process = GameLocator.FindRunning();
                handle = process?.MainWindowHandle ?? IntPtr.Zero;
            }
            if (handle != IntPtr.Zero) NativeMethods.SetForegroundWindow(handle);
        }
        catch (Exception e) { Log.Write("Oyun öne alınamadı: " + e.Message); }
    }

    private void TryAttach()
    {
        if (_session != null || _busy) return;
        Process? process;
        try { process = GameLocator.FindRunning(); }
        catch (InvalidOperationException e)
        {
            AppendLog(e.Message, Theme.Danger);
            return;
        }
        if (process == null)
        {
            if (!_cardError)
                SetCard("Oyun bekleniyor",
                    "The Last of Us Part I açılınca otomatik bağlanır.", Theme.Warn, "SYS");
            return;
        }

        _busy = true;
        AppendLog($"Oyun bulundu (PID {process.Id}); bellek analiz ediliyor…");
        BackgroundAnalysis.Run(() =>
        {
            try
            {
                string path = process.MainModule?.FileName ?? throw new InvalidOperationException("Oyun yolu okunamadı.");
                var image = GameImage.Load(path);
                var analysis = Analyzer.Analyze(image);
                var session = new TrainerSession(process, image, analysis);
                Ui(() =>
                {
                    _session = session;
                    _analysis = analysis;
                    _busy = false;
                    _timer.Stop(); // Connected mode is entirely event-driven.
                    _retryAt = DateTime.MinValue;
                    _cardError = false;
                    AppendLog($"Bağlandı · sürüm {session.Version} · derleme {TrainerSession.BuildId(analysis)}",
                        Theme.Accent);
                    if (!analysis.Ammo.Supported || analysis.Ammo.Detail.Contains("yok:", StringComparison.Ordinal))
                        AppendLog("Mühimmat: " + analysis.Ammo.Detail, Theme.Warn);
                    SetCard("Oyun bağlı",
                        $"Sürüm {session.Version} · {TrainerSession.BuildId(analysis)} · kurtarma koruması hazır",
                        Theme.Accent, "SYS");
                    RefreshRows();
                    _ = ObserveExitAsync(session);
                });
            }
            catch (Exception e)
            {
                Log.Write("Bağlantı hatası: " + e);
                Ui(() =>
                {
                    _busy = false;
                    _retryAt = DateTime.UtcNow.AddSeconds(5);
                    process.Dispose();
                    AppendLog("Bağlanamadı: " + e.Message, Theme.Danger);
                    SetCard("Bağlantı kurulamadı", e.Message, Theme.Danger, "ERR");
                    RefreshRows();
                });
            }
        });
    }

    private void Poll()
    {
        if (_session != null) return;
        if (DateTime.UtcNow < _retryAt) return;
        TryAttach();
    }

    private async Task ObserveExitAsync(TrainerSession session)
    {
        try { await session.WaitForExitAsync(_closing.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        catch (ObjectDisposedException) { return; }
        Ui(() =>
        {
            if (_session != session) return;
            session.Dispose();
            _session = null;
            AppendLog("Oyun kapandı; bağlantı sonlandırıldı.", Theme.Warn);
            _cardError = false;
            SetCard("Oyun kapandı", "Yeniden açılınca otomatik bağlanır.", Theme.Warn, "SYS");
            RefreshRows();
            _timer.Start();
        });
    }

    private void ToggleFeature(int feature)
    {
        if (_session == null)
        {
            AppendLog("Oyun bağlı değil. Önce oyunu başlat.", Theme.Warn);
            return;
        }
        if (_busy)
        {
            AppendLog("Başka bir işlem sürüyor; bir saniye sonra tekrar dene.", Theme.Warn);
            return;
        }
        try
        {
            if (feature == 0) _session.SetAmmo(!_session.AmmoEnabled);
            else _session.SetHealth(!_session.HealthEnabled);
            AppendLog(feature == 0
                ? _session.AmmoEnabled ? "Sınırsız mühimmat açık." : "Sınırsız mühimmat kapalı."
                : _session.HealthEnabled ? "Sınırsız sağlık açık." : "Sınırsız sağlık kapalı.",
                Theme.Accent);
        }
        catch (Exception e)
        {
            Log.Write("Açma/kapama hatası: " + e);
            AppendLog("Hata: " + e.Message, Theme.Danger);
            ThemeDialog.Error(this, "İşlem başarısız", e.Message);
        }
        RefreshRows();
    }

    private void RefreshRows()
    {
        var ammo = _analysis?.Ammo;
        var health = _analysis?.Health;
        _ammoRow.State = ammo is { Supported: true }
            ? (_session?.AmmoEnabled == true ? RowState.On : RowState.Off)
            : RowState.Unavailable;
        _ammoRow.Detail = ammo?.Detail ?? "analiz bekleniyor…";

        _healthRow.State = health is { Supported: true }
            ? (_session?.HealthEnabled == true ? RowState.On : RowState.Off)
            : RowState.Unavailable;
        _healthRow.Detail = health?.Detail ?? "analiz bekleniyor…";

        _ammoRow.Invalidate();
        _healthRow.Invalidate();
    }

    private void SetCard(string title, string sub, Color dot, string tag)
    {
        if (_card.Title == title && _card.Sub == sub && _card.DotColor == dot && _card.Badge == tag) return;
        _cardError = dot == Theme.Danger;
        _card.Title = title;
        _card.Sub = sub;
        _card.DotColor = dot;
        _card.Badge = tag;
        _card.Invalidate();
    }

    private void AppendLog(string message, Color? color = null)
    {
        if (IsDisposed) return;
        _console.Append(message, color);
        Log.Write(message);
    }

    private void Ui(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

}
