namespace TLOUTrainer.Core;

/// <summary>One field to overwrite. <paramref name="Expected"/> is checked before writing.</summary>
public readonly record struct Patch(IntPtr Address, byte[] Bytes, byte[]? Expected = null);

/// <summary>
/// Applies a batch of writes so that it is either fully applied or fully rolled back.
/// The caller freezes the target's threads around the batch.
/// </summary>
public static class PatchWriter
{
    public static void Apply(RemoteMemory memory, Func<IDisposable> freeze, IReadOnlyList<Patch> patches,
        Action<string> log)
    {
        var applied = new List<(IntPtr Address, byte[] Previous)>();
        using (freeze())
        {
            try
            {
                foreach (var patch in patches)
                {
                    var previous = memory.Read(patch.Address, patch.Bytes.Length);
                    if (patch.Expected != null && !previous.AsSpan().SequenceEqual(patch.Expected))
                        throw new InvalidOperationException(
                            $"Beklenmeyen bellek içeriği (0x{patch.Address.ToInt64():X}); yama güvenli değil.");
                    applied.Add((patch.Address, previous));
                    memory.Write(patch.Address, patch.Bytes);
                }
                foreach (var patch in patches)
                {
                    if (!memory.Read(patch.Address, patch.Bytes.Length).AsSpan().SequenceEqual(patch.Bytes))
                        throw new InvalidOperationException("Bellek yazımı doğrulanamadı.");
                }
            }
            catch
            {
                // Keep the target frozen until rollback is complete.
                foreach (var (address, previous) in applied.AsEnumerable().Reverse())
                {
                    try { memory.Write(address, previous); }
                    catch (Exception e) { log($"Geri alma başarısız (0x{address.ToInt64():X}): {e.Message}"); }
                }
                throw;
            }
        }
    }
}
