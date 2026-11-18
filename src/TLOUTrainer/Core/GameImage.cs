using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace TLOUTrainer.Core;

public sealed record ImageSection(string Name, int Rva, int Size, bool Executable, bool Writable);

public sealed class GameImage
{
    public byte[] Bytes { get; }
    public IReadOnlyList<ImageSection> Sections { get; }
    public string Fingerprint { get; }
    public int Timestamp { get; }

    public GameImage(byte[] mapped, IReadOnlyList<ImageSection> sections, string fingerprint = "test", int timestamp = 0)
    {
        Bytes = mapped;
        Sections = sections;
        Fingerprint = fingerprint;
        Timestamp = timestamp;
    }

    public static GameImage Load(string path)
    {
        // Read headers and sections directly: do not retain a second full EXE-sized array.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);
        using var pe = new PEReader(file, PEStreamOptions.LeaveOpen);
        var headers = pe.PEHeaders;
        var h = headers.PEHeader ?? throw new InvalidDataException("PE başlığı bulunamadı.");
        if (h.Magic != PEMagic.PE32Plus || headers.CoffHeader.Machine != System.Reflection.PortableExecutable.Machine.Amd64
            || h.SizeOfImage <= 0 || h.SizeOfImage > 512 * 1024 * 1024)
            throw new InvalidDataException("Desteklenmeyen x64 oyun dosyası.");
        var bytes = new byte[h.SizeOfImage];
        if (h.SizeOfHeaders < 0 || h.SizeOfHeaders > bytes.Length || h.SizeOfHeaders > file.Length)
            throw new InvalidDataException("Geçersiz PE başlık boyutu.");
        file.Position = 0;
        file.ReadExactly(bytes.AsSpan(0, h.SizeOfHeaders));
        var sections = new List<ImageSection>();
        foreach (var s in headers.SectionHeaders)
        {
            if (s.VirtualAddress < 0 || s.VirtualSize < 0 || (long)s.VirtualAddress + Math.Max(s.VirtualSize, s.SizeOfRawData) > bytes.Length)
                throw new InvalidDataException("Geçersiz PE bölümü.");
            if (s.SizeOfRawData < 0 || s.PointerToRawData < 0 ||
                (long)s.PointerToRawData + s.SizeOfRawData > file.Length)
                throw new InvalidDataException("Geçersiz PE veri boyutu.");
            if (s.SizeOfRawData > 0)
            {
                file.Position = s.PointerToRawData;
                file.ReadExactly(bytes.AsSpan(s.VirtualAddress, s.SizeOfRawData));
            }
            sections.Add(new(s.Name, s.VirtualAddress, s.VirtualSize,
                (s.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0,
                (s.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0));
        }
        file.Position = 0;
        return new(bytes, sections, Convert.ToHexString(SHA256.HashData(file)), headers.CoffHeader.TimeDateStamp);
    }

    public bool IsCode(long rva) => Sections.Any(s => s.Executable && rva >= s.Rva && rva < (long)s.Rva + s.Size);
    public bool IsData(long rva) => Sections.Any(s => s.Writable && !s.Executable && rva >= s.Rva && rva < (long)s.Rva + s.Size);
    public IEnumerable<ImageSection> Code => Sections.Where(s => s.Executable);
    public IEnumerable<ImageSection> Constants => Sections.Where(s => !s.Executable && !s.Writable && s.Name == ".rdata");

    public long RipTarget(int instruction, int displacementOffset, int length) =>
        instruction + (long)length + BitConverter.ToInt32(Bytes, instruction + displacementOffset);
}
