using System.Text;
using Iced.Intel;

namespace TLOUTrainer.Core;

/// <summary>A single RIP-relative read of a debug flag. Only its disp32 is rewritten.</summary>
public sealed record ReadSite(int Rva, int DisplacementOffset, int Length, int FlagIndex, byte[] Original);

public sealed record FeatureAnalysis(string Name, bool Supported, string Detail,
    IReadOnlyList<int> Flags, IReadOnlyList<ReadSite> Sites)
{
    public IReadOnlyList<CodeSite> CodePatches { get; init; } = [];
}

public sealed record AnalysisResult(string Fingerprint, FeatureAnalysis Ammo, FeatureAnalysis Health);

/// <summary>
/// Locates the engine's own debug flags without relying on version-specific RVAs:
///   1. two byte signatures identify the ammo flag block,
///   2. the debug-menu registrations identify the remaining flags by their labels,
///   3. every branch instruction that reads those bytes is collected so the trainer
///      can point those reads at private memory instead of racing the game.
/// </summary>
public static class Analyzer
{
    // Engine signatures published by w1zr/TLOU-Infinite-Ammo (MIT; see THIRD-PARTY-NOTICES.md).
    private const string InfiniteSignature =
        "32 C9 38 0D ?? ?? ?? ?? 75 0F 48 8B 05 ?? ?? ?? ?? 38 88 ?? ?? ?? ?? 74 02 B1 01 8A C1 C3";
    private const string ReloadSignature =
        "40 38 3D ?? ?? ?? ?? 40 8A CF 74 02 B1 01 40 38 3D ?? ?? ?? ?? 75 0C 40 38 3D ?? ?? ?? ??";
    private const string RegistrationSignature =
        "4C 8D 0D ?? ?? ?? ?? 48 89 7C 24 28 4C 8D 05 ?? ?? ?? ?? 48 89 7C 24 20 " +
        "48 8D 15 ?? ?? ?? ?? 48 8B CD E8 ?? ?? ?? ??";
    private const string AmmoLabel = "Infinite Ammo";
    private const string HealthLabel = "Invincible Player";
    private const string WeaponHealthLabel = "Infinite Weapon Health";
    private const string CraftablesLabel = "Infinite Craftables";
    private const string CraftingLabel = "Infinite Crafting";

    public static AnalysisResult Analyze(GameImage image)
    {
        int[] ammoFlags = [];
        string? ammoError = null;
        int? weaponHealth = null, craftables = null, crafting = null;
        var unavailable = new List<string>();
        string? healthError = null;
        int[] healthFlags = [];

        try
        {
            int infiniteCode = UniquePattern(image, InfiniteSignature);
            int reloadCode = UniquePattern(image, ReloadSignature);
            long infinite = image.RipTarget(infiniteCode + 2, 2, 6);
            long bottomless = image.RipTarget(reloadCode + 23, 3, 7);
            long noReload = image.RipTarget(reloadCode, 3, 7);
            long registered = ResolveRegisteredFlag(image, AmmoLabel);
            if (!image.IsData(infinite) || !image.IsData(bottomless) || !image.IsData(noReload))
                throw new InvalidDataException("Bayrak adresleri yazılabilir veri bölümünde değil.");
            if (bottomless != infinite + 1 || noReload != infinite + 4)
                throw new InvalidDataException("Bayrak yerleşimi beklenen düzende değil.");
            if (registered != infinite)
                throw new InvalidDataException("Dev menü kaydı ile kod imzası farklı adresi gösteriyor.");
            ammoFlags = [checked((int)infinite), checked((int)bottomless)];
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException)
        {
            ammoError = e.Message;
        }

        // Extra engine toggles that keep melee weapons/crafting materials from draining.
        if (ammoError == null)
        {
            weaponHealth = TryOptionalFlag(image, WeaponHealthLabel);
            if (weaponHealth == null) unavailable.Add("yakın dövüş");
            craftables = TryOptionalFlag(image, CraftablesLabel);
            if (craftables == null) unavailable.Add("sarf malzemesi");
            crafting = TryOptionalFlag(image, CraftingLabel);
            if (crafting == null) unavailable.Add("üretim");
        }

        try
        {
            healthFlags = [ResolveRegisteredFlag(image, HealthLabel)];
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException)
        {
            healthError = e.Message;
        }

        FeatureAnalysis BuildAmmo()
        {
            if (ammoError != null) return new("Sınırsız mermi", false, ammoError, [], []);
            var shivSites = ShivAnalyzer.FindConsumptionCalls(image);
            var requested = new List<(int Flag, string Name, int Min, int Max)>
            {
                (ammoFlags[0], "şarjör", 2, 2),
                (ammoFlags[1], "yedek", 5, 5)
            };
            if (weaponHealth is { } wh) requested.Add((wh, "dövüş", 1, 12));
            if (craftables is { } cb) requested.Add((cb, "sarf", 1, 12));
            if (crafting is { } cr) requested.Add((cr, "üretim", 1, 12));

            var scans = FindReads(image, requested.Select(r => r.Flag).ToArray());
            var used = new List<(int Flag, string Name, ReadSite[] Sites)>();
            var missing = new List<string>(unavailable);
            if (shivSites.Count == 0) missing.Add("çakı");
            foreach (var (flag, name, min, max) in requested)
            {
                if (used.Any(u => u.Flag == flag)) continue;
                int index = requested.FindIndex(r => r.Flag == flag);
                var sites = scans.Where(s => s.FlagIndex == index).Select(s => s with { FlagIndex = used.Count }).ToArray();
                if (sites.Length < min || sites.Length > max)
                {
                    if (min == max)
                        return new("Sınırsız mermi", false,
                            $"{name} bayrağının beklenen {min} okuma noktası yerine {sites.Length} nokta bulundu; " +
                            "oyun kodu değişmiş olabilir.", [], []);
                    missing.Add(name);
                    continue;
                }
                used.Add((flag, name, sites));
            }

            var flags = used.Select(u => u.Flag).ToArray();
            var all = used.SelectMany(u => u.Sites).OrderBy(s => s.Rva).ToArray();
            string detail = $"{all.Length + shivSites.Count} kontrol · " +
                            string.Join(" · ", used.Select(u => u.Name));
            if (shivSites.Count != 0) detail += " · çakı";
            if (missing.Count > 0) detail += $" · yok: {string.Join(", ", missing)}";
            return new FeatureAnalysis("Sınırsız mermi", true, detail, flags, all) { CodePatches = shivSites };
        }

        FeatureAnalysis BuildHealth()
        {
            if (healthError != null) return new("Sınırsız sağlık", false, healthError, [], []);
            var selected = FindReads(image, healthFlags).ToArray();
            if (selected.Length is < 3 or > 64)
                return new("Sınırsız sağlık", false,
                    $"Dokunulmazlık bayrağının {selected.Length} okuma noktası var; güvenli sınırın dışında.",
                    healthFlags, []);
            return new("Sınırsız sağlık", true,
                $"{selected.Length} kontrol doğrulandı (hasar)", healthFlags, selected);
        }

        return new(image.Fingerprint, BuildAmmo(), BuildHealth());
    }

    private static int? TryOptionalFlag(GameImage image, string label)
    {
        try { return ResolveRegisteredFlag(image, label); }
        catch (Exception e) when (e is InvalidDataException or ArgumentException) { return null; }
    }

    public static int[] ParsePattern(string pattern) => pattern
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s is "?" or "??" ? -1 : Convert.ToInt32(s, 16))
        .ToArray();

    public static List<int> FindPattern(byte[] bytes, int start, int length, int[] pattern)
    {
        if (pattern.Length == 0) throw new ArgumentException("Boş imza.");
        var found = new List<int>();
        if (length < pattern.Length) return found;
        int anchor = Array.FindIndex(pattern, b => b >= 0);
        byte anchorByte = (byte)(anchor >= 0 ? pattern[anchor] : 0);
        int last = start + length - pattern.Length;
        for (int i = start; i <= last; i++)
        {
            if (anchor >= 0)
            {
                // SIMD search avoids a managed comparison for every byte of every section.
                int next = bytes.AsSpan(i + anchor, last - i + 1).IndexOf(anchorByte);
                if (next < 0) break;
                i += next;
            }
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (pattern[j] >= 0 && bytes[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) found.Add(i);
        }
        return found;
    }

    private static int UniquePattern(GameImage image, string pattern)
    {
        var parsed = ParsePattern(pattern);
        var hits = image.Code.SelectMany(s => FindPattern(image.Bytes, s.Rva, s.Size, parsed)).ToArray();
        if (hits.Length != 1)
            throw new InvalidDataException($"Kod imzası {hits.Length} kez bulundu (beklenen: 1).");
        return hits[0];
    }

    /// <summary>
    /// Reads the flag address out of the engine's own debug-menu registration:
    /// lea r9,[flag]; lea r8,[callback]; lea rdx,[name]; mov rcx,menu; call register.
    /// The name string is looked up in read-only data, so a shifted build still resolves.
    /// </summary>
    public static int ResolveRegisteredFlag(GameImage image, string label)
    {
        var pattern = ParsePattern(RegistrationSignature);
        var labelBytes = Encoding.ASCII.GetBytes(label + "\0").Select(b => (int)b).ToArray();
        var labelAddresses = image.Constants
            .SelectMany(s => FindPattern(image.Bytes, s.Rva, s.Size, labelBytes))
            .Select(a => (long)a)
            .ToHashSet();
        if (labelAddresses.Count == 0)
            throw new InvalidDataException($"'{label}' metni oyun içinde bulunamadı.");

        var flags = new HashSet<int>();
        foreach (var section in image.Code)
        {
            foreach (int at in FindPattern(image.Bytes, section.Rva, section.Size, pattern))
            {
                long name = image.RipTarget(at + 24, 3, 7);
                if (!labelAddresses.Contains(name)) continue;
                long flag = image.RipTarget(at, 3, 7);
                long callback = image.RipTarget(at + 12, 3, 7);
                long register = image.RipTarget(at + 34, 1, 5);
                if (!image.IsData(flag))
                    throw new InvalidDataException($"'{label}' bayrağı yazılabilir veride değil.");
                if (!image.IsCode(callback) || !image.IsCode(register))
                    throw new InvalidDataException($"'{label}' kayıt çağrısı geçersiz.");
                flags.Add(checked((int)flag));
            }
            if (flags.Count == 0)
            {
                // Bool menu items are also registered as: lea r8,[flag]; lea rdx,[label]; mov rcx,menu; call add.
                var simple = ParsePattern("4C 8D 05 ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ??");
                foreach (int at in FindPattern(image.Bytes, section.Rva, section.Size, simple))
                {
                    long name = image.RipTarget(at + 7, 3, 7);
                    if (!labelAddresses.Contains(name)) continue;
                    long flag = image.RipTarget(at, 3, 7);
                    if (!image.IsData(flag))
                        throw new InvalidDataException($"'{label}' bayrağı yazılabilir veride değil.");
                    flags.Add(checked((int)flag));
                }
            }
        }
        if (flags.Count != 1)
            throw new InvalidDataException($"'{label}' için tek ve doğrulanmış bir adres bulunamadı ({flags.Count}).");
        return flags.Single();
    }

    // Every instruction form that reads one byte through RIP-relative addressing.
    private static List<ReadSite> FindReads(GameImage image, IReadOnlyList<int> flags)
    {
        var result = new List<ReadSite>();
        if (flags.Count == 0) return result;
        var targets = flags.Select(f => (long)f).ToHashSet();
        var seen = new HashSet<int>();
        foreach (var section in image.Code)
        {
            int start = section.Rva, end = section.Rva + section.Size;
            for (int i = start; i + 6 <= end; i++)
            {
                byte b = image.Bytes[i];
                int length;
                if ((b is 0x38 or 0x84) && (image.Bytes[i + 1] & 0xC7) == 0x05)
                    length = 6;
                else if (b == 0x80 && image.Bytes[i + 1] == 0x3D && i + 7 <= end)
                    length = 7;
                else continue;

                // Resolve the disp32 before allocating an Iced decoder. Previously every
                // unrelated CMP/TEST in the game created decoder objects and GC pressure.
                long target = i + (long)length + BitConverter.ToInt32(image.Bytes, i + 2);
                if (!targets.Contains(target)) continue;

                bool rexBefore = i > start && image.Bytes[i - 1] is >= 0x40 and <= 0x4F;
                if (rexBefore && TrySite(image, i - 1, flags) is { } withRex)
                {
                    if (seen.Add(withRex.Rva)) result.Add(withRex);
                    continue;
                }
                if (TrySite(image, i, flags) is { } site && seen.Add(site.Rva)) result.Add(site);
            }
        }
        return result.OrderBy(s => s.Rva).ToList();
    }

    private static ReadSite? TrySite(GameImage image, int rva, IReadOnlyList<int> flags)
    {
        if (!image.IsCode(rva)) return null;
        var decoder = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(image.Bytes, rva, Math.Min(16, image.Bytes.Length - rva)));
        decoder.IP = (ulong)rva;
        var instruction = decoder.Decode();
        if (instruction.IsInvalid) return null;
        if (!instruction.IsIPRelativeMemoryOperand || instruction.MemorySize != MemorySize.UInt8) return null;
        if (instruction.IPRelativeMemoryAddress > int.MaxValue) return null;
        int target = (int)instruction.IPRelativeMemoryAddress;
        int index = -1;
        for (int i = 0; i < flags.Count; i++)
            if (flags[i] == target) { index = i; break; }
        if (index < 0) return null;
        var offsets = decoder.GetConstantOffsets(in instruction);
        if (offsets.DisplacementSize != 4 || instruction.Length is < 6 or > 8) return null;
        // Only branch conditions are worth rewriting. `movzx eax,[flag]` sites are
        // state save/restore helpers (one of them zeroes the block), so feeding
        // those a forced value could leave the game's own flag set behind.
        if (instruction.Mnemonic is not (Mnemonic.Cmp or Mnemonic.Test)) return null;
        return new ReadSite(rva, offsets.DisplacementOffset, instruction.Length, index,
            image.Bytes.AsSpan(rva, instruction.Length).ToArray());
    }
}
