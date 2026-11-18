using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using TLOUTrainer.Core;
using Xunit;

namespace Trainer.Tests;

public sealed class RecoveryTests
{
    private static readonly Func<IDisposable> NoFreeze = () => new MemoryStream();

    [Fact]
    public void AlreadyOriginalCodeNeedsNoSuspension()
    {
        using var process = Process.GetCurrentProcess();
        using var memory = RemoteMemory.Open(process.Id);
        var page = memory.AllocateNear(process.MainModule!.BaseAddress.ToInt64(), 4096);
        try
        {
            memory.Write(page, [0]);
            RecoveryGuard.Restore(memory, [new(page.ToInt64(), [0], [1])],
                () => throw new InvalidOperationException("Unnecessary suspension"));
        }
        finally { memory.Free(page); }
    }

    [Fact]
    public void RestoreRecognizesOriginalAndPatchedAndPreservesUnrelatedBytes()
    {
        using var process = Process.GetCurrentProcess();
        using var memory = RemoteMemory.Open(process.Id);
        var page = memory.AllocateNear(process.MainModule!.BaseAddress.ToInt64(), 4096);
        try
        {
            memory.Write(page, [7, 1, 2, 9]);
            RecoveryField[] fields = [new((page + 1).ToInt64(), [0, 0], [1, 2])];
            RecoveryGuard.Restore(memory, fields, NoFreeze);
            Assert.Equal(new byte[] { 7, 0, 0, 9 }, memory.Read(page, 4));
            RecoveryGuard.Restore(memory, fields, NoFreeze); // idempotent
            Assert.Equal(new byte[] { 7, 0, 0, 9 }, memory.Read(page, 4));
        }
        finally { memory.Free(page); }
    }

    [Fact]
    public void ForeignModificationAbortsBeforeAnyWrites()
    {
        using var process = Process.GetCurrentProcess();
        using var memory = RemoteMemory.Open(process.Id);
        var page = memory.AllocateNear(process.MainModule!.BaseAddress.ToInt64(), 4096);
        try
        {
            memory.Write(page, [1, 8]);
            RecoveryField[] fields =
            [
                new(page.ToInt64(), [0], [1]),
                new((page + 1).ToInt64(), [0], [2])
            ];
            Assert.Throws<InvalidOperationException>(() => RecoveryGuard.Restore(memory, fields, NoFreeze));
            Assert.Equal(new byte[] { 1, 8 }, memory.Read(page, 2));
        }
        finally { memory.Free(page); }
    }

    [Theory]
    [InlineData("abrupt")]
    [InlineData("normal")]
    [InlineData("during-command")]
    public async Task RealHelperRestoresAndFreesWithoutJournalFiles(string mode)
    {
        // Separate synthetic target: never touches a running game.
        string root = FindRoot();
        string exe = Environment.GetEnvironmentVariable("TLOUP1_TEST_EXE") ??
            Path.Combine(root, "src", "TLOUTrainer", "bin", "Release", "net8.0-windows", "TLOUP1.exe");
        string name = "TLOUP1-test-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var target = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            ArgumentList = { "/d", "/q", "/c", "set /p TLOUP1_TEST=" }
        })!;
        using var fakeParent = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            ArgumentList = { "/d", "/q", "/c", "set /p TLOUP1_TEST=" }
        })!;
        using var memory = RemoteMemory.Open(target.Id);
        var targetBase = await WaitForModuleAsync(target);
        var page = memory.AllocateNear(targetBase.ToInt64(), 4096);
        // Sentinel outside the allocation that recovery must release.
        var sentinel = memory.AllocateNear(page.ToInt64(), 4096);
        Process? helper = null;
        try
        {
            memory.Write(sentinel, [0x41, 0xFF, 0xD2]);
            var plan = new RecoveryPlan(target.Id, target.StartTime.ToUniversalTime().Ticks,
                page.ToInt64(), [new(sentinel.ToInt64(), [0x41, 0xFF, 0xD2], [0x90, 0x90, 0x90])]);
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in new[] { "--recovery", name, fakeParent.Id.ToString(),
                         fakeParent.StartTime.ToUniversalTime().Ticks.ToString() })
                start.ArgumentList.Add(arg);
            helper = Process.Start(start)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await pipe.WaitForConnectionAsync(timeout.Token);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(plan));
            Assert.Equal("READY", await reader.ReadLineAsync(timeout.Token));
            GuardPatch[] enable = [new(sentinel.ToInt64(), [0x90, 0x90, 0x90], [0x41, 0xFF, 0xD2])];
            await writer.WriteLineAsync(JsonSerializer.Serialize(enable));
            if (mode == "during-command")
            {
                fakeParent.Kill();
                pipe.Dispose(); // Mirrors the UI's pipe handles closing on process termination.
            }
            else
            {
                Assert.Equal("OK", await reader.ReadLineAsync(timeout.Token));
                Assert.Equal(new byte[] { 0x90, 0x90, 0x90 }, memory.Read(sentinel, 3));
                // A toggle-off must restore the exact original bytes.
                GuardPatch[] disable = [new(sentinel.ToInt64(), [0x41, 0xFF, 0xD2], [0x90, 0x90, 0x90])];
                await writer.WriteLineAsync(JsonSerializer.Serialize(disable));
                Assert.Equal("OK", await reader.ReadLineAsync(timeout.Token));
                Assert.Equal(new byte[] { 0x41, 0xFF, 0xD2 }, memory.Read(sentinel, 3));
                await writer.WriteLineAsync(JsonSerializer.Serialize(enable));
                Assert.Equal("OK", await reader.ReadLineAsync(timeout.Token));
                if (mode == "normal")
                {
                    await writer.WriteLineAsync("CLEAN");
                    Assert.Equal("CLEANED", await reader.ReadLineAsync(timeout.Token));
                }
                else fakeParent.Kill();
            }
            await helper.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, helper.ExitCode);
            Assert.Equal(new byte[] { 0x41, 0xFF, 0xD2 }, memory.Read(sentinel, 3));
            Assert.Throws<InvalidOperationException>(() => memory.Read(page, 1));
        }
        finally
        {
            memory.Free(page);
            memory.Free(sentinel);
            if (helper is { HasExited: false }) helper.Kill();
            helper?.Dispose();
            if (!fakeParent.HasExited) fakeParent.Kill();
            if (!target.HasExited) target.Kill();
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TLOUTrainer.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException();
    }

    private static async Task<IntPtr> WaitForModuleAsync(Process process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException("Sentetik hedef erken kapandı.");
            try
            {
                process.Refresh();
                if (process.MainModule is { } module) return module.BaseAddress;
            }
            catch (System.ComponentModel.Win32Exception) { }
            await Task.Delay(10);
        }
        throw new TimeoutException("Sentetik hedef modülü yüklenmedi.");
    }
}
