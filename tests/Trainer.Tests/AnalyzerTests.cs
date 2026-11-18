using System.Diagnostics;
using Iced.Intel;
using TLOUTrainer.Core;
using TLOUTrainer.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Trainer.Tests;

public class AnalyzerTests
{
    private readonly ITestOutputHelper _output;
    public AnalyzerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void PatternParsingHandlesWildcards()
    {
        var p = Analyzer.ParsePattern("48 8B ?? 0F");
        Assert.Equal([0x48, 0x8B, -1, 0x0F], p);
        var bytes = new byte[] { 0x48, 0x8B, 0x99, 0x0F, 0x48, 0x8B, 0x00, 0x0F };
        Assert.Equal([0, 4], Analyzer.FindPattern(bytes, 0, bytes.Length, p));
    }

    [Fact]
    public void PatternSearchHandlesLeadingWildcardsAndFinalOffset()
    {
        byte[] bytes = [9, 1, 2, 7, 1, 2];
        Assert.Equal([0, 3], Analyzer.FindPattern(bytes, 0, bytes.Length, [-1, 1, 2]));
        Assert.Equal([3], Analyzer.FindPattern(bytes, 2, 4, [-1, 1, 2]));
        Assert.Equal([0, 1, 2, 3, 4], Analyzer.FindPattern(bytes, 0, bytes.Length, [-1, -1]));
    }

    [Fact]
    public void FastReadFilterPreservesRexInstructions()
    {
        var image = SyntheticImage.Build();
        new byte[] { 0x44, 0x38, 0x0D }.CopyTo(image.Bytes, 0x1600);
        BitConverter.GetBytes(SyntheticImage.Infinite - (0x1600 + 7)).CopyTo(image.Bytes, 0x1603);
        var result = Analyzer.Analyze(image);
        Assert.True(result.Ammo.Supported, result.Ammo.Detail);
        Assert.Contains(result.Ammo.Sites, s => s.Rva == 0x1600 && s.Length == 7 && s.DisplacementOffset == 3);
        Assert.Equal(12, result.Ammo.Sites.Count);
    }

    [Fact]
    public void SyntheticImageResolvesBothFeatures()
    {
        var image = SyntheticImage.Build();
        var result = Analyzer.Analyze(image);

        Assert.True(result.Ammo.Supported, result.Ammo.Detail);
        Assert.True(result.Health.Supported, result.Health.Detail);
        Assert.Equal([SyntheticImage.Infinite, SyntheticImage.Bottomless,
                      SyntheticImage.WeaponHealth, SyntheticImage.Craftables, SyntheticImage.Crafting],
            result.Ammo.Flags);
        Assert.Equal([SyntheticImage.Health], result.Health.Flags);
        Assert.Equal(12, result.Ammo.Sites.Count);
        Assert.Equal(3, result.Health.Sites.Count);
        Assert.Equal(2, result.Ammo.Sites.Count(s => s.FlagIndex == 0));
        Assert.Equal(5, result.Ammo.Sites.Count(s => s.FlagIndex == 1));
        Assert.Equal(2, result.Ammo.Sites.Count(s => s.FlagIndex == 2));
        Assert.Equal(1, result.Ammo.Sites.Count(s => s.FlagIndex == 3));
        Assert.Equal(2, result.Ammo.Sites.Count(s => s.FlagIndex == 4));
        Assert.Contains("dövüş", result.Ammo.Detail);
        Assert.Contains("çakı", result.Ammo.Detail);
        Assert.Equal([SyntheticImage.ShivBaseCall, SyntheticImage.ShivUpgradeCall],
            result.Ammo.CodePatches.Select(s => s.Rva));
        Assert.All(result.Ammo.CodePatches, s =>
        {
            Assert.Equal(3, s.Original.Length);
            Assert.Equal(new byte[] { 0x90, 0x90, 0x90 }, s.Replacement);
            Assert.Equal(Mnemonic.Call, Decode(image.Bytes, s.Rva).Mnemonic);
        });
        Assert.Contains(result.Ammo.Sites, s => s.Rva == 0x1102 && s.DisplacementOffset == 2 && s.Length == 6);
        Assert.Contains(result.Ammo.Sites, s => s.Rva == 0x1600 && s.DisplacementOffset == 2 && s.Length == 6);
        Assert.Contains(result.Ammo.Sites, s => s.Rva == 0x1620 && s.DisplacementOffset == 2 && s.Length == 7);
        // The movzx getter is a state helper, not a branch check: it must be left alone.
        Assert.DoesNotContain(result.Health.Sites, s => s.Rva == 0x1760);
    }

    [Fact]
    public void RegistrationLookupFindsExactLabels()
    {
        var image = SyntheticImage.Build();
        Assert.Equal(SyntheticImage.Infinite, Analyzer.ResolveRegisteredFlag(image, "Infinite Ammo"));
        Assert.Equal(SyntheticImage.Health, Analyzer.ResolveRegisteredFlag(image, "Invincible Player"));
        Assert.Throws<InvalidDataException>(() => Analyzer.ResolveRegisteredFlag(image, "Undetectable Player"));
    }

    [Fact]
    public void MissingOptionalToggleKeepsAmmoWorking()
    {
        var image = SyntheticImage.Build();
        image.Bytes[0x3040] = (byte)'X'; // break "Infinite Weapon Health"
        var result = Analyzer.Analyze(image);
        Assert.True(result.Ammo.Supported, result.Ammo.Detail);
        Assert.DoesNotContain(SyntheticImage.WeaponHealth, result.Ammo.Flags);
        Assert.Contains("yok", result.Ammo.Detail);
        Assert.Equal(10, result.Ammo.Sites.Count);
    }

    [Fact]
    public void MissingShivLabelDoesNotPatchUnrelatedInventory()
    {
        var image = SyntheticImage.Build();
        image.Bytes[0x30C0] = (byte)'X';
        var result = Analyzer.Analyze(image);
        Assert.True(result.Ammo.Supported);
        Assert.Empty(result.Ammo.CodePatches);
        Assert.Contains("yok: çakı", result.Ammo.Detail);
    }

    [Fact]
    public void UnrecognizedShivConsumptionCallFailsClosed()
    {
        var image = SyntheticImage.Build();
        image.Bytes[SyntheticImage.ShivBaseCall + 2] = 0xD3;
        var result = Analyzer.Analyze(image);
        Assert.True(result.Ammo.Supported);
        Assert.Empty(result.Ammo.CodePatches);
    }

    [Fact]
    public void AmbiguousShivFunctionDisablesOnlyShivPatches()
    {
        var image = SyntheticImage.Build();
        int copy = 0x1A00;
        image.Bytes.AsSpan(SyntheticImage.ShivFunction, SyntheticImage.ShivFunctionEnd - SyntheticImage.ShivFunction)
            .CopyTo(image.Bytes.AsSpan(copy));
        BitConverter.GetBytes(0x30C0 - (copy + 7)).CopyTo(image.Bytes, copy + 3);
        BitConverter.GetBytes(0x30A0 - (copy + 14)).CopyTo(image.Bytes, copy + 10);
        BitConverter.GetBytes(copy).CopyTo(image.Bytes, 0x500C);
        BitConverter.GetBytes(copy + SyntheticImage.ShivFunctionEnd - SyntheticImage.ShivFunction).CopyTo(image.Bytes, 0x5010);
        Assert.Empty(Analyzer.Analyze(image).Ammo.CodePatches);
    }

    [Fact]
    public void MissingSignatureDisablesAmmoButKeepsHealth()
    {
        var image = SyntheticImage.Build();
        image.Bytes[0x1100] = 0x31; // break the ammo function
        var result = Analyzer.Analyze(image);
        Assert.False(result.Ammo.Supported);
        Assert.True(result.Health.Supported, result.Health.Detail);
        Assert.Empty(result.Ammo.Sites);
    }

    [Fact]
    public void MismatchedRegistrationDisablesAmmo()
    {
        // Point the "Infinite Ammo" registration at a different writable byte.
        var image = SyntheticImage.Build();
        int disp = SyntheticImage.Health - (0x1300 + 7);
        BitConverter.GetBytes(disp).CopyTo(image.Bytes, 0x1303);
        var result = Analyzer.Analyze(image);
        Assert.False(result.Ammo.Supported);
        Assert.Contains("farklı adresi", result.Ammo.Detail);
    }

    [Fact]
    public void EverySiteIsALengthPreservingByteRead()
    {
        var image = SyntheticImage.Build();
        var result = Analyzer.Analyze(image);
        foreach (var site in result.Ammo.Sites.Concat(result.Health.Sites))
        {
            var instruction = Decode(image.Bytes, site.Rva);
            Assert.False(instruction.IsInvalid);
            Assert.True(instruction.IsIPRelativeMemoryOperand);
            Assert.Equal(MemorySize.UInt8, instruction.MemorySize);
            Assert.Equal(site.Length, instruction.Length);
            Assert.Equal(site.Original.AsSpan().ToArray(), image.Bytes.AsSpan(site.Rva, site.Length).ToArray());
        }
    }

    [Fact]
    public void Disp32RewritePointsAtPrivateMemory()
    {
        var image = SyntheticImage.Build();
        var result = Analyzer.Analyze(image);
        var patched = (byte[])image.Bytes.Clone();
        foreach (var site in result.Ammo.Sites.Concat(result.Health.Sites))
        {
            int disp = SyntheticImage.Scratch - (site.Rva + site.Length);
            BitConverter.GetBytes(disp).CopyTo(patched, site.Rva + site.DisplacementOffset);
            var instruction = Decode(patched, site.Rva);
            Assert.Equal((ulong)SyntheticImage.Scratch, instruction.IPRelativeMemoryAddress);
        }
    }

    [Fact]
    public void InstalledGameIfPresentIsConsistent()
    {
        string? path = GameLocator.FindInstalled();
        if (path == null)
        {
            _output.WriteLine("Oyun kurulu değil; entegrasyon kontrolü atlandı.");
            return;
        }
        var image = GameImage.Load(path);
        var result = Analyzer.Analyze(image);
        _output.WriteLine($"Sürüm {System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion}, " +
                          $"damga {image.Timestamp:X8}, mermi {result.Ammo.Detail}, sağlık {result.Health.Detail}");
        if (image.Fingerprint.StartsWith("088D37AFF3B8", StringComparison.Ordinal))
        {
            Assert.True(result.Ammo.Supported, result.Ammo.Detail);
            Assert.True(result.Health.Supported, result.Health.Detail);
            Assert.Equal(14, result.Ammo.Sites.Count);
            Assert.Equal(2, result.Ammo.CodePatches.Count);
            Assert.Equal(12, result.Health.Sites.Count);
        }
        if (result.Ammo.Supported)
            Assert.All(result.Ammo.Sites, s => Assert.Equal(s.Length, Decode(image.Bytes, s.Rva).Length));
        if (result.Health.Supported)
            Assert.All(result.Health.Sites, s => Assert.True(Decode(image.Bytes, s.Rva).IsIPRelativeMemoryOperand));
    }

    [Fact]
    public void RemoteMemoryRoundTripsNearOwnImage()
    {
        using var process = Process.GetCurrentProcess();
        using var memory = RemoteMemory.Open(process.Id);
        var module = process.MainModule!;
        IntPtr page = memory.AllocateNear(module.BaseAddress.ToInt64(), 0x1000);
        try
        {
            Assert.NotEqual(IntPtr.Zero, page);
            long delta = Math.Abs(page.ToInt64() - module.BaseAddress.ToInt64());
            Assert.InRange(delta, 0x10000, 0x7FFF_FFFF);
            byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF];
            memory.Write(page, payload);
            Assert.Equal(payload, memory.Read(page, payload.Length));
        }
        finally
        {
            memory.Free(page);
        }
    }

    private static Instruction Decode(byte[] bytes, int rva)
    {
        var decoder = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(bytes, rva, Math.Min(16, bytes.Length - rva)));
        decoder.IP = (ulong)rva;
        return decoder.Decode();
    }
}
