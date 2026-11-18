using System.Diagnostics;
using TLOUTrainer.Core;
using Xunit;

namespace Trainer.Tests;

public class PatchWriterTests
{
    private static readonly Func<IDisposable> NoFreeze = () => new MemoryStream();

    [Fact]
    public void SingleBytePatchIsComparedAsOneByte()
    {
        using var process = Process.GetCurrentProcess();
        using var memory = RemoteMemory.Open(process.Id);
        var page = memory.AllocateNear(process.MainModule!.BaseAddress.ToInt64(), 0x1000);
        try
        {
            memory.Write(page, [0, 0, 0, 0]);
            // Expected is one byte while the page holds four: must not read past it.
            PatchWriter.Apply(memory, NoFreeze, [new Patch(page, [1], [0])], _ => { });
            Assert.Equal(new byte[] { 1, 0, 0, 0 }, memory.Read(page, 4));

            PatchWriter.Apply(memory, NoFreeze, [new Patch(page, [0], [1])], _ => { });
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, memory.Read(page, 4));
        }
        finally
        {
            memory.Free(page);
        }
    }

    [Fact]
    public void FailedExpectationRollsBackEarlierWrites()
    {
        using var process = Process.GetCurrentProcess();
        using var memory = RemoteMemory.Open(process.Id);
        var page = memory.AllocateNear(process.MainModule!.BaseAddress.ToInt64(), 0x1000);
        try
        {
            memory.Write(page, [7, 0, 0, 0]);
            var log = new List<string>();
            Patch[] patches =
            [
                new(page, [1]),            // would succeed
                new(page + 1, [2], [9])    // page+1 is 0, not 9: must abort the batch
            ];
            var error = Assert.Throws<InvalidOperationException>(() =>
                PatchWriter.Apply(memory, NoFreeze, patches, log.Add));
            Assert.Contains("Beklenmeyen bellek içeriği", error.Message);
            Assert.Contains((page + 1).ToInt64().ToString("X"), error.Message);
            Assert.Equal(new byte[] { 7, 0, 0, 0 }, memory.Read(page, 4));
            Assert.Empty(log);
        }
        finally
        {
            memory.Free(page);
        }
    }

    [Fact]
    public void CodeDisplacementPatchKeepsInstructionLength()
    {
        using var process = Process.GetCurrentProcess();
        using var memory = RemoteMemory.Open(process.Id);
        var page = memory.AllocateNear(process.MainModule!.BaseAddress.ToInt64(), 0x1000);
        try
        {
            // `38 0D disp32` — cmp byte ptr [rip+disp32], cl : six bytes, disp at +2.
            memory.Write(page, [0x38, 0x0D, 0x11, 0x22, 0x33, 0x44]);
            var original = memory.Read(page, 6);
            var replacement = BitConverter.GetBytes(0x1234 - 6);
            PatchWriter.Apply(memory, NoFreeze,
                [new Patch(page + 2, replacement, original.AsSpan(2, 4).ToArray())], _ => { });
            var patched = memory.Read(page, 6);
            Assert.Equal(original[..2], patched[..2]);
            Assert.Equal(replacement, patched[2..]);
        }
        finally
        {
            memory.Free(page);
        }
    }
}
