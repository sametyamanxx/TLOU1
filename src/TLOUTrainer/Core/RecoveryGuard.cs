using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace TLOUTrainer.Core;

public sealed record RecoveryField(long Address, byte[] Original, byte[] Patched);
public sealed record RecoveryPlan(int GamePid, long GameStartTicks, long Allocation, RecoveryField[] Fields);
internal sealed record GuardPatch(long Address, byte[] Bytes, byte[]? Expected);

/// <summary>
/// An event-driven helper in the same EXE. The plan is acknowledged before the first
/// patch. No journal file, service, scheduled task, polling loop or injected DLL.
/// </summary>
internal sealed class RecoveryGuard : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly StreamWriter _writer;
    private readonly StreamReader _reader;
    private readonly Process _helper;
    private bool _completed;

    public RecoveryGuard(RecoveryPlan plan)
    {
        string name = "TLOUP1-" + Guid.NewGuid().ToString("N");
        _pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var parent = Process.GetCurrentProcess();
        start.ArgumentList.Add("--recovery");
        start.ArgumentList.Add(name);
        start.ArgumentList.Add(parent.Id.ToString());
        start.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString());
        try
        {
            _helper = Process.Start(start) ?? throw new InvalidOperationException("Kurtarma koruması başlatılamadı.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
            _writer = new PipeWriter(_pipe);
            _reader = new StreamReader(_pipe, Encoding.UTF8, false, 4096, true);
            _writer.WriteLine(JsonSerializer.Serialize(plan));
            if (_reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult() != "READY")
                throw new InvalidOperationException("Kurtarma planı doğrulanamadı; özellikler açılmayacak.");
        }
        catch
        {
            _pipe.Dispose();
            throw;
        }
    }

    public bool IsAlive
    {
        get { try { return !_helper.HasExited && _pipe.IsConnected; } catch { return false; } }
    }

    public void Complete()
    {
        if (_completed) return;
        _writer.WriteLine("CLEAN");
        if (_reader.ReadLine() != "CLEANED")
            throw new IOException("Kurtarma koruması orijinal kodu doğrulayamadı.");
        _completed = true;
    }

    public void Apply(IReadOnlyList<Patch> patches)
    {
        if (!IsAlive) throw new InvalidOperationException("Kurtarma koruması çalışmıyor.");
        _writer.WriteLine(JsonSerializer.Serialize(patches.Select(p =>
            new GuardPatch(p.Address.ToInt64(), p.Bytes, p.Expected)).ToArray()));
        string? response = _reader.ReadLine();
        if (response != "OK")
            throw new InvalidOperationException(response?.Replace("ERROR:", "") ?? "Kurtarma bağlantısı kapandı.");
    }

    public void Dispose()
    {
        _reader.Dispose();
        _writer.Dispose();
        _pipe.Dispose();
        // Do not terminate the helper: it still owns recovery if cleanup failed.
        _helper.Dispose();
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[2], out int parentPid) ||
            !long.TryParse(args[3], out long parentStart)) return 2;
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            if (parent.StartTime.ToUniversalTime().Ticks != parentStart) return 2;
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(10000);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            using var writer = new PipeWriter(pipe);
            var plan = JsonSerializer.Deserialize<RecoveryPlan>(await reader.ReadLineAsync() ?? "");
            if (plan == null || plan.Fields.Length is 0 or > 1024 || plan.Allocation == 0) return 2;
            using var game = Process.GetProcessById(plan.GamePid);
            if (game.StartTime.ToUniversalTime().Ticks != plan.GameStartTicks) return 2;
            using var memory = RemoteMemory.Open(game.Id);
            foreach (var field in plan.Fields)
                if (!memory.Read((IntPtr)field.Address, field.Original.Length).AsSpan().SequenceEqual(field.Original))
                    return 2;
            await writer.WriteLineAsync("READY");
            var parentExit = parent.WaitForExitAsync();
            var gameExit = game.WaitForExitAsync();
            bool cleanRequested = false;
            while (true)
            {
                using var stopRead = new CancellationTokenSource();
                var message = reader.ReadLineAsync(stopRead.Token).AsTask();
                var finished = await Task.WhenAny(message, parentExit, gameExit);
                if (finished != message)
                {
                    stopRead.Cancel();
                    try { await message; }
                    catch (Exception e) when (e is OperationCanceledException or IOException) { }
                    if (finished == gameExit) return 0;
                    break;
                }
                string? command;
                try { command = await message; } catch (IOException) { command = null; }
                if (command == null)
                {
                    await Task.WhenAny(parentExit, gameExit);
                    if (game.HasExited) return 0;
                    break;
                }
                if (command == "CLEAN")
                {
                    cleanRequested = true;
                    break;
                }
                string response;
                try
                {
                    var requested = JsonSerializer.Deserialize<GuardPatch[]>(command)
                        ?? throw new InvalidOperationException("Geçersiz yama isteği.");
                    foreach (var patch in requested)
                    {
                        bool flag = patch.Address >= plan.Allocation && patch.Address < plan.Allocation + 4096 &&
                                    patch.Bytes.Length == 1 && patch.Bytes[0] <= 1;
                        bool code = plan.Fields.Any(f =>
                        {
                            long offset = patch.Address - f.Address;
                            return offset >= 0 && offset + patch.Bytes.Length <= f.Original.Length &&
                                   (patch.Bytes.AsSpan().SequenceEqual(f.Original.AsSpan((int)offset, patch.Bytes.Length)) ||
                                    patch.Bytes.AsSpan().SequenceEqual(f.Patched.AsSpan((int)offset, patch.Bytes.Length)));
                        });
                        if (!flag && !code) throw new InvalidOperationException("Yama kurtarma planının dışında.");
                    }
                    // All suspension and mutation happen in this helper. Killing the UI
                    // cannot strand suspension counts half way through a patch batch.
                    PatchWriter.Apply(memory, () => new ThreadFreeze(game),
                        requested.Select(p => new Patch((IntPtr)p.Address, p.Bytes, p.Expected)).ToArray(), _ => { });
                    response = "OK";
                }
                catch (Exception e)
                {
                    response = "ERROR:" + e.Message.Replace('\n', ' ').Replace('\r', ' ');
                }
                // If the UI disappeared during the batch, still run recovery below.
                try { await writer.WriteLineAsync(response); }
                catch (IOException) { break; }
            }
            // A transient thread-enumeration race is retried, not silently called success.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Restore(memory, plan.Fields, () => new ThreadFreeze(game));
                    memory.FreeChecked((IntPtr)plan.Allocation);
                    if (cleanRequested)
                    {
                        try { await writer.WriteLineAsync("CLEANED"); }
                        catch (IOException) { } // Cleanup already succeeded.
                    }
                    return 0;
                }
                catch when (attempt < 2)
                {
                    if (game.HasExited) return 0;
                    await Task.Delay(100);
                }
            }
            return 3;
        }
        catch { return 3; }
    }

    internal static void Restore(RemoteMemory memory, IReadOnlyList<RecoveryField> fields, Func<IDisposable> freeze)
    {
        // Closing with both features off needs no game-thread suspension or code writes.
        if (fields.All(f => memory.Read((IntPtr)f.Address, f.Original.Length).AsSpan().SequenceEqual(f.Original)))
            return;
        using (freeze())
        {
            var changed = new List<RecoveryField>();
            // Validate the entire plan before changing anything; never overwrite another tool.
            foreach (var field in fields)
            {
                var current = memory.Read((IntPtr)field.Address, field.Original.Length);
                if (!current.AsSpan().SequenceEqual(field.Original) &&
                    !current.AsSpan().SequenceEqual(field.Patched))
                    throw new InvalidOperationException("Kurtarma noktasını başka bir araç değiştirmiş.");
                if (!current.AsSpan().SequenceEqual(field.Original)) changed.Add(field);
            }
            foreach (var field in changed)
            {
                memory.Write((IntPtr)field.Address, field.Original);
                if (!memory.Read((IntPtr)field.Address, field.Original.Length).AsSpan().SequenceEqual(field.Original))
                    throw new InvalidOperationException("Orijinal kod geri yüklenemedi.");
            }
        }
    }

    private sealed class PipeWriter : StreamWriter
    {
        public PipeWriter(Stream stream) : base(stream, new UTF8Encoding(false), 4096, true) => AutoFlush = true;

        protected override void Dispose(bool disposing)
        {
            // A dead UI closes its pipe; flushing on disposal must not override
            // a successful restore/free or prevent the remaining handles closing.
            try { base.Dispose(disposing); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
