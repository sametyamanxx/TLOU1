namespace TLOUTrainer.Infrastructure;

/// <summary>Session diagnostics only; no AppData directory or log file is created.</summary>
public static class Log
{
    private static readonly Queue<string> Entries = new();
    private static readonly object Gate = new();

    public static void Write(string text)
    {
        lock (Gate)
        {
            Entries.Enqueue($"[{DateTime.Now:HH:mm:ss}] {text}");
            while (Entries.Count > 300) Entries.Dequeue();
        }
    }
}
