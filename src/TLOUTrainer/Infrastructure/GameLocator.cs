using System.Diagnostics;
using System.Text.Json;

namespace TLOUTrainer.Infrastructure;

public static class GameLocator
{
    public static Process? FindRunning()
    {
        var processes = Process.GetProcessesByName("tlou-i").Concat(Process.GetProcessesByName("tlou-i-l")).ToArray();
        if (processes.Length > 1)
        {
            foreach (var p in processes) p.Dispose();
            throw new InvalidOperationException("Birden fazla oyun işlemi açık. Yalnızca birini açık bırak.");
        }
        return processes.SingleOrDefault();
    }

    public static string? FindInstalled()
    {
        string manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) return null;
        foreach (string path in Directory.EnumerateFiles(manifests, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (!root.TryGetProperty("DisplayName", out var display) ||
                    !display.GetString()!.Contains("The Last of Us", StringComparison.OrdinalIgnoreCase) ||
                    display.GetString()!.Contains("Part II", StringComparison.OrdinalIgnoreCase)) continue;
                var exe = Path.Combine(root.GetProperty("InstallLocation").GetString()!, "tlou-i.exe");
                if (File.Exists(exe)) return exe;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
            { Log.Write($"Epic manifest okunamadı: {path}: {e.Message}"); }
        }
        return null;
    }
}
