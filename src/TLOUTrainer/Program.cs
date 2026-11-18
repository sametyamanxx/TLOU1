using TLOUTrainer.Infrastructure;
using TLOUTrainer.Core;
using TLOUTrainer.UI;

namespace TLOUTrainer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--licenses")
        {
            foreach (string name in new[] { "TLOUP1.Notices", "TLOUP1.RuntimeNotices" })
            {
                using var stream = typeof(Program).Assembly.GetManifestResourceStream(name);
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                Console.WriteLine(reader.ReadToEnd());
            }
            return;
        }
        if (args.FirstOrDefault() == "--recovery")
        {
            Environment.ExitCode = RecoveryGuard.RunAsync(args).GetAwaiter().GetResult();
            return;
        }
        if (args.FirstOrDefault() == "--close-and-wait")
        {
            Environment.ExitCode = AppLifetime.CloseAndWait();
            return;
        }
        using var instance = new Mutex(true, AppLifetime.MutexName, out bool first);
        if (!first) return;
        using var closeSignal = new EventWaitHandle(false, EventResetMode.AutoReset, AppLifetime.CloseEvent);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) =>
        {
            Log.Write("Arayüz hatası: " + e.Exception);
            ThemeDialog.Error(null, "Beklenmeyen hata", e.Exception.Message);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Kritik hata: " + e.ExceptionObject);

        using var form = new MainForm();
        var closeRegistration = ThreadPool.RegisterWaitForSingleObject(closeSignal, (_, _) =>
        {
            if (!form.IsDisposed && form.IsHandleCreated)
            {
                try { form.BeginInvoke(() => form.Close()); }
                catch (InvalidOperationException) { }
            }
        }, null, Timeout.Infinite, false);
        try { Application.Run(form); }
        finally
        {
            closeRegistration.Unregister(null);
            instance.ReleaseMutex();
        }
    }
}
