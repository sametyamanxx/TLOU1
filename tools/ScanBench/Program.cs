using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("ScanBench <TLOUP1.dll> <game.exe>");
string assemblyPath = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    string path = Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
var imageType = assembly.GetType("TLOUTrainer.Core.GameImage", true)!;
var analyzerType = assembly.GetType("TLOUTrainer.Core.Analyzer", true)!;
for (int run = 0; run < 3; run++)
{
    var result = Scan(imageType, analyzerType, args[1]);
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
    using var process = Process.GetCurrentProcess();
    process.Refresh();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Run = run + 1, result.ElapsedMs, result.AllocatedMB, result.AmmoSites, result.HealthSites,
        result.Fingerprint, PrivateMB = Math.Round(process.PrivateMemorySize64 / 1048576d, 1)
    }));
}

[MethodImpl(MethodImplOptions.NoInlining)]
static (double ElapsedMs, double AllocatedMB, int AmmoSites, int HealthSites, string Fingerprint)
    Scan(Type imageType, Type analyzerType, string game)
{
    long before = GC.GetTotalAllocatedBytes(true);
    var clock = Stopwatch.StartNew();
    var image = imageType.GetMethod("Load")!.Invoke(null, [game]);
    var result = analyzerType.GetMethod("Analyze")!.Invoke(null, [image])!;
    clock.Stop();
    object Feature(string name) => result.GetType().GetProperty(name)!.GetValue(result)!;
    int Sites(object feature) => ((ICollection)feature.GetType().GetProperty("Sites")!.GetValue(feature)!).Count;
    return (Math.Round(clock.Elapsed.TotalMilliseconds, 1),
        Math.Round((GC.GetTotalAllocatedBytes(true) - before) / 1048576d, 1),
        Sites(Feature("Ammo")), Sites(Feature("Health")),
        (string)result.GetType().GetProperty("Fingerprint")!.GetValue(result)!);
}
