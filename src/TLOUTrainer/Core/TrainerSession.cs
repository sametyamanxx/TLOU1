using System.Diagnostics;
using TLOUTrainer.Infrastructure;

namespace TLOUTrainer.Core;

/// <summary>
/// Owns the live connection to the game: verifies the analysed file matches the
/// running process, then installs length-preserving disp32 rewrites so the engine
/// reads private flags instead of its own volatile debug bytes.
/// </summary>
public sealed class TrainerSession : IDisposable
{
    private readonly Process _process;
    private readonly RemoteMemory _memory;
    private readonly IntPtr _moduleBase;
    private readonly IntPtr _scratch;
    private readonly AnalysisResult _analysis;
    private readonly Dictionary<int, IntPtr> _privateFlags;
    private readonly bool[] _enabled = new bool[2];
    private readonly RecoveryGuard _guard;
    private readonly RecoveryField[] _recoveryFields;
    private bool _disposed;

    public string Version { get; }
    public bool AmmoSupported => _analysis.Ammo.Supported;
    public bool HealthSupported => _analysis.Health.Supported;
    public string AmmoDetail => _analysis.Ammo.Detail;
    public string HealthDetail => _analysis.Health.Detail;
    public bool AmmoEnabled => _enabled[0];
    public bool HealthEnabled => _enabled[1];
    public IntPtr WindowHandle => _process.MainWindowHandle;
    public bool HasExited { get { try { return _process.HasExited; } catch { return true; } } }
    public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

    public TrainerSession(Process process, GameImage image, AnalysisResult analysis)
    {
        _process = process;
        _analysis = analysis;
        _memory = RemoteMemory.Open(process.Id);

        ProcessModule module;
        try
        {
            module = process.MainModule ?? throw new InvalidOperationException("Oyun modülü okunamadı.");
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _memory.Dispose();
            throw new InvalidOperationException("Oyun modülü okunamadı: " + e.Message);
        }

        _moduleBase = module.BaseAddress;
        string exePath = module.FileName;
        Version = FileVersionInfo.GetVersionInfo(exePath).FileVersion ?? "bilinmiyor";

        try
        {
            VerifyImage(image);
            _scratch = _memory.AllocateNear(_moduleBase.ToInt64(), image.Bytes.Length);
            _privateFlags = new Dictionary<int, IntPtr>();
            int slot = 0;
            foreach (int flag in analysis.Ammo.Flags.Concat(analysis.Health.Flags).Distinct())
                _privateFlags[flag] = _scratch + slot++ * 4;
            _recoveryFields = analysis.Ammo.Sites.Select(site => RecoverySite(site, analysis.Ammo))
                .Concat(analysis.Health.Sites.Select(site => RecoverySite(site, analysis.Health)))
                .Concat(analysis.Ammo.CodePatches.Concat(analysis.Health.CodePatches).Select(site =>
                    new RecoveryField((_moduleBase + site.Rva).ToInt64(), site.Original, site.Replacement)))
                .ToArray();
            _guard = new RecoveryGuard(new RecoveryPlan(process.Id,
                process.StartTime.ToUniversalTime().Ticks, _scratch.ToInt64(), _recoveryFields));
            Log.Write($"Bağlandı: pid={process.Id} base=0x{_moduleBase.ToInt64():X} sürüm={Version} " +
                      $"mermi={(AmmoSupported ? "destekli" : "yok")} sağlık={(HealthSupported ? "destekli" : "yok")}");
        }
        catch
        {
            _memory.Free(_scratch);
            _memory.Dispose();
            throw;
        }
    }

    public static string BuildId(AnalysisResult analysis) =>
        (analysis.Fingerprint.Length >= 12 ? analysis.Fingerprint[..12] : analysis.Fingerprint).ToLowerInvariant();

    private RecoveryField RecoverySite(ReadSite site, FeatureAnalysis feature)
    {
        long displacement = _privateFlags[feature.Flags[site.FlagIndex]].ToInt64() -
                            (_moduleBase.ToInt64() + site.Rva + site.Length);
        var patched = (byte[])site.Original.Clone();
        BitConverter.GetBytes(checked((int)displacement)).CopyTo(patched, site.DisplacementOffset);
        return new RecoveryField((_moduleBase + site.Rva).ToInt64(), site.Original, patched);
    }

    private void VerifyImage(GameImage image)
    {
        var header = _memory.Read(_moduleBase, 0x1000);
        if (BitConverter.ToUInt16(header, 0) != 0x5A4D)
            throw new InvalidDataException("Oyun belleğindeki PE başlığı geçersiz.");
        int lfanew = BitConverter.ToInt32(header, 0x3C);
        if (lfanew < 0x40 || lfanew > 0xF00)
            throw new InvalidDataException("PE başlık konumu geçersiz.");
        var nt = _memory.Read(_moduleBase + lfanew, 0x200);
        if (BitConverter.ToUInt32(nt, 0) != 0x00004550)
            throw new InvalidDataException("NT imzası bulunamadı.");
        if (BitConverter.ToInt32(nt, 8) != image.Timestamp)
            throw new InvalidDataException("Çalışan oyun dosyayla aynı sürümde değil. Oyunu yeniden başlat.");
        if (BitConverter.ToInt32(nt, 0x50) != image.Bytes.Length)
            throw new InvalidDataException("Çalışan oyunun bellek boyutu analizle eşleşmiyor. Oyunu yeniden başlat.");

        var sites = _analysis.Ammo.Sites.Concat(_analysis.Health.Sites).ToArray();
        if (sites.Length == 0) throw new InvalidDataException("Yama noktası bulunamadı.");
        foreach (var site in sites)
        {
            var current = _memory.Read(_moduleBase + site.Rva, site.Original.Length);
            if (!current.AsSpan().SequenceEqual(site.Original))
                throw new InvalidDataException(
                    $"Bellekteki kod analiz edilen dosyayla eşleşmiyor (0x{site.Rva:X}). Oyun güncellenmiş olabilir.");
        }
        foreach (var site in _analysis.Ammo.CodePatches.Concat(_analysis.Health.CodePatches))
            if (!_memory.Read(_moduleBase + site.Rva, site.Original.Length).AsSpan().SequenceEqual(site.Original))
                throw new InvalidDataException("Çakı tüketim kodu analiz edilen dosyayla eşleşmiyor.");

    }

    public void SetAmmo(bool enabled) => Apply(0, _analysis.Ammo, enabled);
    public void SetHealth(bool enabled) => Apply(1, _analysis.Health, enabled);

    private void Apply(int feature, FeatureAnalysis analysis, bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!analysis.Supported) throw new InvalidOperationException(analysis.Detail);
        if (_enabled[feature] == enabled) return;
        if (HasExited) throw new InvalidOperationException("Oyun kapandı.");
        if (enabled && !_guard.IsAlive)
            throw new InvalidOperationException("Kurtarma koruması çalışmıyor. Paneli yeniden başlat.");

        var patches = new List<Patch>();
        if (enabled)
        {
            // The private page always starts zeroed, but a retry after a failed
            // toggle may already hold 1: writing is unconditional, verifying later.
            foreach (int flag in analysis.Flags)
                patches.Add(new Patch(_privateFlags[flag], [1]));
            foreach (var site in analysis.Sites)
            {
                IntPtr address = _moduleBase + site.Rva + site.DisplacementOffset;
                long disp = _privateFlags[analysis.Flags[site.FlagIndex]].ToInt64() -
                            (_moduleBase.ToInt64() + site.Rva + site.Length);
                if (disp is > int.MaxValue or < int.MinValue)
                    throw new InvalidOperationException("Özel bellek koda çok uzak.");
                var expected = site.Original.AsSpan(site.DisplacementOffset, 4).ToArray();
                if (!_memory.Read(address, 4).AsSpan().SequenceEqual(expected))
                    throw new InvalidOperationException(
                        $"Yama noktası değişmiş (0x{site.Rva:X}); oyun güncellenmiş olabilir.");
                patches.Add(new Patch(address, BitConverter.GetBytes((int)disp), expected));
            }
        }
        else
        {
            foreach (var site in analysis.Sites)
            {
                IntPtr address = _moduleBase + site.Rva + site.DisplacementOffset;
                var field = RecoverySite(site, analysis);
                var patched = field.Patched.AsSpan(site.DisplacementOffset, 4).ToArray();
                var current = _memory.Read(address, 4);
                var original = site.Original.AsSpan(site.DisplacementOffset, 4).ToArray();
                if (current.AsSpan().SequenceEqual(original))
                    continue; // already restored
                patches.Add(new Patch(address, original, patched));
            }
            foreach (int flag in analysis.Flags)
                patches.Add(new Patch(_privateFlags[flag], [0]));
        }

        foreach (var site in analysis.CodePatches)
        {
            IntPtr address = _moduleBase + site.Rva;
            byte[] expected = enabled ? site.Original : site.Replacement;
            byte[] replacement = enabled ? site.Replacement : site.Original;
            if (!enabled && _memory.Read(address, replacement.Length).AsSpan().SequenceEqual(replacement)) continue;
            patches.Add(new Patch(address, replacement, expected));
        }
        // Suspend the game's threads so no instruction can be seen half-rewritten.
        _guard.Apply(patches);

        _enabled[feature] = enabled;
        Log.Write($"{(feature == 0 ? "Mermi" : "Sağlık")} {(enabled ? "açıldı" : "kapatıldı")} " +
                  $"({analysis.Sites.Count} nokta)");
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (!HasExited)
        {
            try
            {
                // Also catches an earlier partial write whose rollback failed.
                _guard.Complete();
            }
            catch (Exception e) { Log.Write("Kapanış kurtarması korumaya bırakıldı: " + e.Message); }
        }
        _disposed = true;
        // Never free a page while any instruction might still reference it.
        _guard.Dispose();
        _memory.Dispose();
        _process.Dispose();
        Log.Write("Oturum kapatıldı.");
    }
}
