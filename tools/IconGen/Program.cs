using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Copies the original icon resources, without loading or executing the game's code.
string game = args.ElementAtOrDefault(0) ?? @"C:\Program Files\Epic Games\TheLastOfUsPartI\tlou-i.exe";
string output = args.ElementAtOrDefault(1) ?? Path.Combine(Directory.GetCurrentDirectory(), "assets", "game.ico");
IntPtr module = Win.LoadLibraryEx(game, IntPtr.Zero, 0x00000002);
if (module == IntPtr.Zero) throw new IOException("Oyun kaynakları açılamadı.");
try
{
    byte[]? group = null;
    Win.ResourceCallback callback = (m, t, n, _) =>
    {
        group = ReadResource(m, t, n);
        return false;
    };
    Win.EnumResourceNames(module, (IntPtr)14, callback, IntPtr.Zero);
    if (group == null) throw new IOException("Oyunun orijinal ikonu bulunamadı.");
    using var reader = new BinaryReader(new MemoryStream(group));
    reader.ReadUInt16();
    reader.ReadUInt16();
    int count = reader.ReadUInt16();
    var icons = new List<(byte[] Header, byte[] Image)>();
    for (int i = 0; i < count; i++)
    {
        var header = reader.ReadBytes(8);
        reader.ReadUInt32();
        ushort id = reader.ReadUInt16();
        icons.Add((header, ReadResource(module, (IntPtr)3, (IntPtr)id)));
    }
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    using (var writer = new BinaryWriter(File.Create(output)))
    {
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)count);
        int offset = 6 + 16 * count;
        foreach (var (header, image) in icons)
        {
            writer.Write(header);
            writer.Write(image.Length);
            writer.Write(offset);
            offset += image.Length;
        }
        foreach (var (_, image) in icons) writer.Write(image);
    }
    using var icon = new Icon(output, 256, 256);
    using var bitmap = icon.ToBitmap();
    bitmap.Save(Path.ChangeExtension(output, ".png"), ImageFormat.Png);
    Console.WriteLine($"Orijinal oyun ikonu: {output} ({count} boyut)");
}
finally { Win.FreeLibrary(module); }

static byte[] ReadResource(IntPtr module, IntPtr type, IntPtr name)
{
    IntPtr resource = Win.FindResource(module, name, type);
    if (resource == IntPtr.Zero) throw new IOException("İkon kaynağı bulunamadı.");
    uint size = Win.SizeofResource(module, resource);
    IntPtr bytes = Win.LockResource(Win.LoadResource(module, resource));
    if (bytes == IntPtr.Zero || size == 0) throw new IOException("İkon kaynağı okunamadı.");
    var result = new byte[checked((int)size)];
    Marshal.Copy(bytes, result, 0, result.Length);
    return result;
}

internal static class Win
{
    internal delegate bool ResourceCallback(IntPtr module, IntPtr type, IntPtr name, IntPtr param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumResourceNames(IntPtr module, IntPtr type, ResourceCallback callback, IntPtr param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")]
    internal static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    internal static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    internal static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")]
    internal static extern bool FreeLibrary(IntPtr module);
}
