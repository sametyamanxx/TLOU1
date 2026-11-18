using System.Text;
using Iced.Intel;

namespace TLOUTrainer.Core;

/// <summary>Validated, length-preserving changes in the engine's shiv-only consumption path.</summary>
public sealed record CodeSite(int Rva, byte[] Original, byte[] Replacement);

internal static class ShivAnalyzer
{
    public static IReadOnlyList<CodeSite> FindConsumptionCalls(GameImage image)
    {
        var baseLabels = Labels(image, "t1x-shiv");
        var upgradeLabels = Labels(image, "t1x-shiv-upgrade");
        if (baseLabels.Count != 1 || upgradeLabels.Count != 1) return [];
        var pdata = image.Sections.FirstOrDefault(s => s.Name == ".pdata");
        if (pdata == null) return [];

        var candidates = new Dictionary<int, IReadOnlyList<CodeSite>>();
        foreach (var section in image.Code)
        {
            foreach (int at in Analyzer.FindPattern(image.Bytes, section.Rva, section.Size,
                         Analyzer.ParsePattern("48 8D 35 ?? ?? ?? ??"))) // lea rsi,[shiv-upgrade]
            {
                if (!upgradeLabels.Contains(image.RipTarget(at, 3, 7))) continue;
                for (int entry = pdata.Rva; entry + 12 <= pdata.Rva + pdata.Size; entry += 12)
                {
                    int begin = BitConverter.ToInt32(image.Bytes, entry);
                    int end = BitConverter.ToInt32(image.Bytes, entry + 4);
                    if (at < begin || at >= end) continue;
                    if (begin < 0 || end <= begin || end > image.Bytes.Length ||
                        end - begin > 2048 || !image.IsCode(begin) || !image.IsCode(end - 1)) break;
                    var sites = ValidateFunction(image, begin, end, baseLabels, upgradeLabels);
                    if (sites.Count == 2) candidates[begin] = sites;
                    break;
                }
            }
        }
        return candidates.Count == 1 ? candidates.Values.Single() : [];
    }

    private static HashSet<long> Labels(GameImage image, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text + "\0").Select(b => (int)b).ToArray();
        return image.Constants.SelectMany(s => Analyzer.FindPattern(image.Bytes, s.Rva, s.Size, bytes))
            .Select(v => (long)v).ToHashSet();
    }

    private static IReadOnlyList<CodeSite> ValidateFunction(GameImage image, int begin, int end,
        HashSet<long> baseLabels, HashSet<long> upgradeLabels)
    {
        var decoder = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(image.Bytes, begin, end - begin));
        decoder.IP = (ulong)begin;
        var instructions = new List<Instruction>();
        while (decoder.IP < (ulong)end)
        {
            var instruction = decoder.Decode();
            if (instruction.IsInvalid || instruction.NextIP > (ulong)end) return [];
            instructions.Add(instruction);
        }

        bool Label(Register register, HashSet<long> labels) => instructions.Any(i =>
            i.Mnemonic == Mnemonic.Lea && i.Op0Register == register && i.IsIPRelativeMemoryOperand &&
            labels.Contains((long)i.IPRelativeMemoryAddress));
        bool RemoveSlot(Register register) => instructions.Any(i =>
            i.Mnemonic == Mnemonic.Mov && i.Op0Register == register && i.Op1Kind == OpKind.Memory &&
            i.MemoryBase == Register.RAX && i.MemoryIndex == Register.None && i.MemoryDisplacement64 == 0x120);
        if (!Label(Register.R14, baseLabels) || !Label(Register.RSI, upgradeLabels) ||
            !RemoveSlot(Register.R10) || !RemoveSlot(Register.R9)) return [];

        static bool OneItem(in Instruction i, Register counter) =>
            i.Mnemonic == Mnemonic.Lea && i.Op0Register == Register.R8D &&
            i.MemoryBase == counter && i.MemoryIndex == Register.None && i.MemoryDisplacement64 == 1;
        var baseCalls = new List<int>();
        var upgradeCalls = new List<int>();
        for (int n = 1; n < instructions.Count; n++)
        {
            var call = instructions[n];
            if (call.Mnemonic != Mnemonic.Call || call.Op0Kind != OpKind.Register || call.Length != 3) continue;
            if (call.Op0Register == Register.R10 && OneItem(instructions[n - 1], Register.R9))
                baseCalls.Add((int)call.IP);
            if (n >= 2 && call.Op0Register == Register.R9 &&
                OneItem(instructions[n - 2], Register.R15) &&
                instructions[n - 1].Mnemonic == Mnemonic.Mov &&
                instructions[n - 1].Op0Register == Register.RDX &&
                instructions[n - 1].Op1Register == Register.RDI)
                upgradeCalls.Add((int)call.IP);
        }
        if (baseCalls.Count != 1 || upgradeCalls.Count != 1) return [];
        CodeSite Site(int at)
        {
            var original = image.Bytes.AsSpan(at, 3).ToArray();
            return new CodeSite(at, original, [0x90, 0x90, 0x90]);
        }
        int baseAt = baseCalls.Single(), upgradeAt = upgradeCalls.Single();
        if (!image.Bytes.AsSpan(baseAt, 3).SequenceEqual(new byte[] { 0x41, 0xFF, 0xD2 }) ||
            !image.Bytes.AsSpan(upgradeAt, 3).SequenceEqual(new byte[] { 0x41, 0xFF, 0xD1 })) return [];
        return [Site(baseAt), Site(upgradeAt)];
    }
}
