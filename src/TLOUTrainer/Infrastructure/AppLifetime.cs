using System.Diagnostics;

namespace TLOUTrainer.Infrastructure;

internal static class AppLifetime
{
    public const string MutexName = @"Local\TLOUP1-UI";
    public const string CloseEvent = @"Local\TLOUP1-Close";

    public static int CloseAndWait()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(CloseEvent);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            bool running = false;
            foreach (var process in Process.GetProcessesByName("TLOUP1"))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId) continue;
                    try
                    {
                        if (string.Equals(process.MainModule?.FileName, Environment.ProcessPath,
                                StringComparison.OrdinalIgnoreCase)) running = true;
                    }
                    catch { return 1; }
                }
            }
            if (!running) return 0;
            if (DateTime.UtcNow >= deadline) return 1;
            Thread.Sleep(100);
        }
    }
}
