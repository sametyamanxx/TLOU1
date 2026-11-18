using System.Diagnostics;
using TLOUTrainer.Native;

namespace TLOUTrainer.Core;

/// <summary>Abort the batch if any enumerated game thread cannot be suspended.</summary>
internal sealed class ThreadFreeze : IDisposable
{
    private readonly List<IntPtr> _threads = [];

    public ThreadFreeze(Process process)
    {
        try
        {
            var frozenIds = new HashSet<int>();
            for (int pass = 0; pass < 4; pass++)
            {
                process.Refresh();
                var snapshot = process.Threads.Cast<ProcessThread>().Select(t => t.Id).ToArray();
                if (snapshot.Length > 0 && snapshot.All(frozenIds.Contains)) return;
                foreach (int id in snapshot)
                {
                    if (frozenIds.Contains(id)) continue;
                    IntPtr handle = NativeMethods.OpenThread(NativeMethods.ThreadSuspendResume, false, (uint)id);
                    if (handle == IntPtr.Zero)
                    {
                        if (ThreadHasExited(process, id)) continue;
                        throw new InvalidOperationException("Oyun iş parçacığı güvenli biçimde durdurulamadı.");
                    }
                    if (NativeMethods.SuspendThread(handle) == uint.MaxValue)
                    {
                        NativeMethods.CloseHandle(handle);
                        if (ThreadHasExited(process, id)) continue;
                        throw new InvalidOperationException("Oyun iş parçacığı askıya alınamadı.");
                    }
                    _threads.Add(handle);
                    frozenIds.Add(id);
                }
            }
            throw new InvalidOperationException("Oyun iş parçacıkları sabitlenemedi; işlem iptal edildi.");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static bool ThreadHasExited(Process process, int id)
    {
        process.Refresh();
        return !process.Threads.Cast<ProcessThread>().Any(t => t.Id == id);
    }

    public void Dispose()
    {
        foreach (IntPtr handle in _threads)
        {
            NativeMethods.ResumeThread(handle);
            NativeMethods.CloseHandle(handle);
        }
        _threads.Clear();
    }
}
