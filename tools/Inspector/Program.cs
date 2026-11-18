using System.Reflection.PortableExecutable;
using System.Text;
using Iced.Intel;

using var ownProcess = System.Diagnostics.Process.GetCurrentProcess();
ownProcess.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
using var file = File.OpenRead(args[0]);
using var pe = new PEReader(file);
var header = pe.PEHeaders;
var gameImage = TLOUTrainer.Core.GameImage.Load(args[0]);
var image = gameImage.Bytes;
Console.WriteLine($"Image size: {image.Length:X}; Base: {header.PEHeader!.ImageBase:X}");
foreach (var s in header.SectionHeaders) Console.WriteLine($"{s.Name}: {s.VirtualAddress:X} +{s.VirtualSize:X} {s.SectionCharacteristics}");
if (args.Length > 1 && args[1] == "--callers")
{
    int target = Convert.ToInt32(args[2], 16);
    foreach (var sec in header.SectionHeaders.Where(s => (s.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0))
        for (int i = sec.VirtualAddress; i + 5 <= sec.VirtualAddress + sec.VirtualSize; i++)
            if (image[i] == 0xE8 && i + 5 + BitConverter.ToInt32(image, i + 1) == target)
                Console.WriteLine($"CALL {i:X}");
    return;
}
if (args.Length > 1 && args[1] == "--strings")
{
    string filter = args.Length > 2 ? args[2] : "";
    foreach (var sec in header.SectionHeaders.Where(s => (s.SectionCharacteristics & SectionCharacteristics.ContainsInitializedData) != 0))
    {
        int a = sec.VirtualAddress, end = a + sec.VirtualSize;
        int start = -1;
        for (int i = a; i <= end; i++)
        {
            bool printable = i < end && image[i] >= 0x20 && image[i] < 0x7F;
            if (printable) { if (start < 0) start = i; continue; }
            if (start >= 0 && i - start >= 4)
            {
                string s = Encoding.ASCII.GetString(image, start, i - start);
                if (filter.Length == 0 || s.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"{start:X} {s}");
            }
            start = -1;
        }
    }
    return;
}
if (args.Length > 1 && args[1] == "--registrations")
{
    // lea r9,[flag]; lea r8,[callback]; lea rdx,[name]; mov rcx,menu; call register
    var pat = TLOUTrainer.Core.Analyzer.ParsePattern(
        "4C 8D 0D ?? ?? ?? ?? 48 89 7C 24 28 4C 8D 05 ?? ?? ?? ?? 48 89 7C 24 20 " +
        "48 8D 15 ?? ?? ?? ?? 48 8B CD E8 ?? ?? ?? ??");
    string CStr(long rva)
    {
        if (rva < 0 || rva >= image.Length) return "<dış>";
        int end = (int)rva;
        while (end < image.Length && image[end] != 0 && end - rva < 120) end++;
        return Encoding.ASCII.GetString(image, (int)rva, end - (int)rva);
    }
    foreach (var sec in header.SectionHeaders.Where(s => (s.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0))
        foreach (int at in TLOUTrainer.Core.Analyzer.FindPattern(image, sec.VirtualAddress, sec.VirtualSize, pat))
        {
            long flag = at + 7L + BitConverter.ToInt32(image, at + 3);
            long cb = at + 12 + 7L + BitConverter.ToInt32(image, at + 15);
            long name = at + 24 + 7L + BitConverter.ToInt32(image, at + 27);
            long target = at + 34 + 5L + BitConverter.ToInt32(image, at + 35);
            Console.WriteLine($"at={at:X} flag={flag:X} cb={cb:X} target={target:X} label=\"{CStr(name)}\"");
        }
    return;
}
if (args.Length > 1 && args[1] == "--findstr")
{
    var needle = Encoding.ASCII.GetBytes(args[2]);
    long i = 0;
    int hits = 0;
    while (i <= image.Length - needle.Length && hits < 40)
    {
        int at = Array.IndexOf(image, needle[0], (int)i, image.Length - (int)i - needle.Length + 1);
        if (at < 0) break;
        if (image.AsSpan(at, needle.Length).SequenceEqual(needle)) { Console.WriteLine($"{at:X}"); hits++; i = at + 1; }
        else i = at + 1;
    }
    return;
}
if (args.Length > 1 && args[1] == "--bools")
{
    // Any bool registration: lea r9,[flag]; mov [rsp+28h],r7; lea r8,[cb];
    // mov [rsp+20h],r7; lea rdx,[label] — the menu pointer may live in any register.
    var pat = TLOUTrainer.Core.Analyzer.ParsePattern(
        "4C 8D 0D ?? ?? ?? ?? 48 89 7C 24 28 4C 8D 05 ?? ?? ?? ?? 48 89 7C 24 20 48 8D 15 ?? ?? ?? ??");
    string CStr(long rva)
    {
        if (rva < 0 || rva >= image.Length) return "<dış>";
        int end = (int)rva;
        while (end < image.Length && image[end] != 0 && end - rva < 160) end++;
        return Encoding.ASCII.GetString(image, (int)rva, end - (int)rva);
    }
    foreach (var sec in header.SectionHeaders.Where(s => (s.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0))
        foreach (int at in TLOUTrainer.Core.Analyzer.FindPattern(image, sec.VirtualAddress, sec.VirtualSize, pat))
        {
            long flag = at + 7L + BitConverter.ToInt32(image, at + 3);
            long cb = at + 12 + 7L + BitConverter.ToInt32(image, at + 15);
            long name = at + 24 + 7L + BitConverter.ToInt32(image, at + 27);
            Console.WriteLine($"at={at:X} flag={flag:X} cb={cb:X} label=\"{CStr(name)}\"");
        }
    return;
}
if (args.Length > 1 && args[1] == "--resolve")
{
    var img = gameImage;
    foreach (string label in args.Skip(2))
    {
        try { Console.WriteLine($"{label}: 0x{TLOUTrainer.Core.Analyzer.ResolveRegisteredFlag(img, label):X}"); }
        catch (Exception e) { Console.WriteLine($"{label}: HATA {e.Message}"); }
    }
    return;
}
if (args.Length > 1 && args[1] == "--cstr")
{
    foreach (string hex in args.Skip(2))
    {
        long rva = Convert.ToInt64(hex, 16);
        int end = (int)rva;
        while (end < image.Length && image[end] != 0 && end - (int)rva < 200) end++;
        Console.WriteLine($"{rva:X} \"{Encoding.ASCII.GetString(image, (int)rva, end - (int)rva)}\"");
    }
    return;
}
if (args.Length > 1 && args[1] == "--func")
{
    int rva = Convert.ToInt32(args[2], 16);
    var pdata = header.SectionHeaders.First(s => s.Name == ".pdata");
    int count = pdata.VirtualSize / 12;
    bool found = false;
    for (int i = 0; i < count; i++)
    {
        int at = pdata.VirtualAddress + i * 12;
        int begin = BitConverter.ToInt32(image, at);
        int end = BitConverter.ToInt32(image, at + 4);
        if (rva < begin || rva >= end) continue;
        found = true;
        Console.WriteLine($"function {begin:X}-{end:X} (target {rva:X})");
        var dec = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(image, begin, end - begin));
        dec.IP = (ulong)begin;
        var fmt = new IntelFormatter(); var o = new StringOutput();
        while (dec.IP < (ulong)end)
        {
            dec.Decode(out var ins);
            o.Reset(); fmt.Format(ins, o);
            Console.WriteLine($"{ins.IP:X8}: {o}");
            if (ins.IsInvalid) break;
        }
        break;
    }
    if (!found) Console.WriteLine("işlev bulunamadı");
    return;
}
if (args.Length > 1 && args[1] == "--xref")
{
    long target = Convert.ToInt64(args[2], 16);
    int before = args.Length > 3 ? Convert.ToInt32(args[3], 16) : 48;
    int after = args.Length > 4 ? Convert.ToInt32(args[4], 16) : 96;
    foreach (var sec in header.SectionHeaders.Where(s => (s.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0))
        for (int i = sec.VirtualAddress; i + 7 < sec.VirtualAddress + sec.VirtualSize; i++)
        {
            if (!((image[i] & 0xF8) == 0x48 || (image[i] & 0xFE) == 0x4C)) continue;
            if (image[i + 1] != 0x8D && image[i + 1] != 0x8B) continue;
            if ((image[i + 2] & 0xC7) != 0x05) continue;
            if (i + 7L + BitConverter.ToInt32(image, i + 3) != target) continue;
            Console.WriteLine($"\n--- xref {i:X} at RVA {i:X} ---");
            int start = Math.Max(sec.VirtualAddress, i - before);
            var dec = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(image, start, after + before));
            dec.IP = (ulong)start;
            var fmt = new IntelFormatter(); var o = new StringOutput();
            for (int guard = 0; guard < 40 && dec.IP < (ulong)(i + after); guard++)
            {
                dec.Decode(out var ins);
                o.Reset(); fmt.Format(ins, o);
                Console.WriteLine($"{ins.IP:X8}: {o}");
                if (ins.IsInvalid) break;
            }
        }
    return;
}
if (args.Length > 1 && args[1] == "--analyze")
{
    var img = gameImage;
    var res = TLOUTrainer.Core.Analyzer.Analyze(img);
    Console.WriteLine($"Ammo: {res.Ammo.Supported} {res.Ammo.Detail} flags={string.Join(",", res.Ammo.Flags.Select(f => f.ToString("X")))}");
    foreach (var s in res.Ammo.Sites) Console.WriteLine($"  ammo  {s.Rva:X} off={s.DisplacementOffset} len={s.Length} flag={res.Ammo.Flags[s.FlagIndex]:X} {BitConverter.ToString(s.Original)}");
    foreach (var s in res.Ammo.CodePatches) Console.WriteLine($"  shiv  {s.Rva:X} {BitConverter.ToString(s.Original)} -> {BitConverter.ToString(s.Replacement)}");
    Console.WriteLine($"Health: {res.Health.Supported} {res.Health.Detail} flags={string.Join(",", res.Health.Flags.Select(f => f.ToString("X")))}");
    foreach (var s in res.Health.Sites) Console.WriteLine($"  health {s.Rva:X} off={s.DisplacementOffset} len={s.Length} {BitConverter.ToString(s.Original)}");
    return;
}
if (args.Length > 1 && args[1] == "--pattern")
{
    var p = args[2].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => s is "?" or "??" ? -1 : Convert.ToInt32(s,16)).ToArray();
    foreach (var sec in header.SectionHeaders.Where(s => (s.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0))
        for (int i = sec.VirtualAddress; i <= sec.VirtualAddress + sec.VirtualSize - p.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < p.Length && ok; j++) ok = p[j] < 0 || image[i+j] == p[j];
            if (ok) Console.WriteLine($"HIT {i:X}");
        }
    return;
}
if (args.Length > 1 && args[1] == "--disasm")
{
    ulong startIp = (ulong)Convert.ToInt64(args[2],16), endIp = startIp + (ulong)Convert.ToInt32(args[3],16);
    var rd = new ByteArrayCodeReader(image, (int)startIp, (int)(endIp-startIp));
    var dec = Iced.Intel.Decoder.Create(64, rd);
    dec.IP = startIp;
    var fmt = new IntelFormatter(); var o = new StringOutput();
    while (dec.IP < endIp) { dec.Decode(out var ins); o.Reset(); fmt.Format(ins,o); Console.WriteLine($"{ins.IP:X8}: {o}"); }
    return;
}
if (args.Length > 1 && args[1] == "--reads")
{
    var targets = args.Skip(2).Select(s => Convert.ToUInt64(s,16)).ToHashSet();
    foreach(var sec in header.SectionHeaders.Where(s => (s.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0))
    {
        var decoder = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(image,sec.VirtualAddress,sec.VirtualSize));
        decoder.IP = (ulong)sec.VirtualAddress;
        var formatter = new IntelFormatter();
        var output = new StringOutput();
        while(decoder.IP < (ulong)(sec.VirtualAddress+sec.VirtualSize))
        {
            decoder.Decode(out var ins);
            if (!ins.IsIPRelativeMemoryOperand || !targets.Contains(ins.IPRelativeMemoryAddress)) continue;
            output.Reset(); formatter.Format(ins,output);
            Console.WriteLine($"{ins.IP:X8}: {output}");
        }
    }
    return;
}
foreach (var needle in args.Skip(1).DefaultIfEmpty("Invincible"))
{
    var pattern = Encoding.ASCII.GetBytes(needle);
    for (int at = 0; at <= image.Length - pattern.Length; at++)
    {
        if (!image.AsSpan(at, pattern.Length).SequenceEqual(pattern)) continue;
        Console.WriteLine($"\nString '{needle}' @ RVA {at:X}: {Encoding.ASCII.GetString(image, Math.Max(at-25,0), Math.Min(120,image.Length-at))}");
        foreach (var sec in header.SectionHeaders.Where(s => (s.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0))
        {
            for (int i = sec.VirtualAddress; i + 7 < sec.VirtualAddress + sec.VirtualSize; i++)
            {
                if ((image[i] & 0xF8) != 0x48 || image[i+1] != 0x8D || (image[i+2] & 0xC7) != 5) continue;
                if (i + 7L + BitConverter.ToInt32(image, i+3) != at) continue;
                Console.WriteLine($"Xref {i:X}");
                int start = i;
                for(int j = Math.Max(i-40,sec.VirtualAddress); j < i; j++)
                    if ((image[j] & 0xF8) == 0x48 && image[j+1] == 0x8D && (image[j+2] & 0xC7) == 5) { start = j; break; }
                var decoder = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(image, start, Math.Min(200,image.Length-start)));
                decoder.IP = (ulong)start;
                var formatter = new IntelFormatter();
                var output = new StringOutput();
                while (decoder.IP < (ulong)i+140)
                {
                    decoder.Decode(out var ins);
                    output.Reset();
                    formatter.Format(ins, output);
                    Console.WriteLine($"{ins.IP:X8}: {output}");
                }
            }
        }
    }
}
