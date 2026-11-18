using System.Runtime.InteropServices;
using TLOUTrainer.Native;

namespace TLOUTrainer.Core;

/// <summary>Small, verified wrapper around the cross-process memory calls.</summary>
public sealed class RemoteMemory : IDisposable
{
    public IntPtr Handle { get; }
    private readonly bool _ownsHandle;
    private bool _disposed;

    public RemoteMemory(IntPtr handle, bool ownsHandle = true)
    {
        if (handle == IntPtr.Zero) throw new ArgumentException("Geçersiz işlem tanıtıcısı.", nameof(handle));
        Handle = handle;
        _ownsHandle = ownsHandle;
    }

    public static RemoteMemory Open(int pid)
    {
        IntPtr handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryInformation | NativeMethods.ProcessVmOperation |
            NativeMethods.ProcessVmRead | NativeMethods.ProcessVmWrite, false, pid);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException(
                "Oyun işlemine erişilemedi. TLOUP1'i yönetici olarak çalıştırmayı dene.");
        return new RemoteMemory(handle);
    }

    public byte[] Read(IntPtr address, int size)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var buffer = new byte[size];
        if (!NativeMethods.ReadProcessMemory(Handle, address, buffer, size, out IntPtr read) || read.ToInt64() != size)
            throw new InvalidOperationException(
                $"Bellek okunamadı (0x{address.ToInt64():X}, hata {Marshal.GetLastWin32Error()}).");
        return buffer;
    }

    public void Write(IntPtr address, byte[] bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!NativeMethods.VirtualProtectEx(Handle, address, bytes.Length, NativeMethods.PageExecuteReadWrite, out uint old))
            throw new InvalidOperationException($"Bellek koruması değiştirilemedi (hata {Marshal.GetLastWin32Error()}).");
        try
        {
            if (!NativeMethods.WriteProcessMemory(Handle, address, bytes, bytes.Length, out IntPtr written) ||
                written.ToInt64() != bytes.Length)
                throw new InvalidOperationException($"Bellek yazılamadı (hata {Marshal.GetLastWin32Error()}).");
            if (!NativeMethods.FlushInstructionCache(Handle, address, bytes.Length))
                throw new InvalidOperationException("İşlemci kod önbelleği yenilenemedi.");
        }
        finally
        {
            NativeMethods.VirtualProtectEx(Handle, address, bytes.Length, old, out _);
        }
    }

    /// <summary>
    /// Reserves a page within ±2 GB of the image so every disp32 rewrite can reach it.
    /// </summary>
    public IntPtr AllocateNear(long imageBase, int imageSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long start = imageBase & ~0xFFFFL;
        long end = start + imageSize;
        for (int step = 1; step <= 0x4000; step++)
        {
            long below = start - step * 0x10000L;
            if (below > 0x10000)
            {
                var p = NativeMethods.VirtualAllocEx(Handle, (IntPtr)below, 0x1000,
                    NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageReadWrite);
                if (p != IntPtr.Zero) return p;
            }
            long above = end + step * 0x10000L;
            var q = NativeMethods.VirtualAllocEx(Handle, (IntPtr)above, 0x1000,
                NativeMethods.MemCommit | NativeMethods.MemReserve, NativeMethods.PageReadWrite);
            if (q != IntPtr.Zero) return q;
        }
        throw new InvalidOperationException("Oyunun yakınında özel bellek ayrılamadı.");
    }

    public void Free(IntPtr address)
    {
        if (_disposed || address == IntPtr.Zero) return;
        NativeMethods.VirtualFreeEx(Handle, address, 0, NativeMethods.MemRelease);
    }

    public void FreeChecked(IntPtr address)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!NativeMethods.VirtualFreeEx(Handle, address, 0, NativeMethods.MemRelease))
            throw new InvalidOperationException("Oyunda ayrılan özel bellek serbest bırakılamadı.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHandle) NativeMethods.CloseHandle(Handle);
    }
}
