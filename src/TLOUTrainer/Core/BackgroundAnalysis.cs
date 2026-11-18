using System.Runtime;

namespace TLOUTrainer.Core;

internal static class BackgroundAnalysis
{
    public static void Run(Action work)
    {
        new Thread(() =>
        {
            try { work(); }
            finally
            {
                // Once per connection, after the scan's stack has unwound. No image is
                // retained by the UI/session; return the large temporary buffers promptly.
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
        })
        {
            IsBackground = true,
            Name = "TLOUP1 analysis",
            Priority = ThreadPriority.BelowNormal
        }.Start();
    }
}
