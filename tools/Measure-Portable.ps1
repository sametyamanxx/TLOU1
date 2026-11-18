param([int]$Seconds = 10)
$ErrorActionPreference = 'Stop'
$exe = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\portable\TLOUP1\TLOUP1.exe'))
if (Get-Process TLOUP1 -ErrorAction SilentlyContinue) { throw 'Ölçümden önce açık TLOUP1 penceresini kapat.' }
$cache = Join-Path "$env:LOCALAPPDATA\Temp\opencode" ('TLOUP1-measure-' + [Guid]::NewGuid().ToString('N'))
function Start-Local([string]$Arguments) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $exe
    $info.Arguments = $Arguments
    $info.UseShellExecute = $false
    $info.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $cache
    return [Diagnostics.Process]::Start($info)
}
$app = Start-Local ''
try {
    [void]$app.WaitForInputIdle(10000)
    $samples = New-Object 'Collections.Generic.List[object]'
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $app.Refresh()
        if ($app.HasExited) { throw 'Ölçüm sırasında uygulama kapandı.' }
        $samples.Add([pscustomobject]@{
            Time = $clock.Elapsed.TotalSeconds
            CpuMs = $app.TotalProcessorTime.TotalMilliseconds
            WorkingMB = $app.WorkingSet64 / 1MB
            PrivateMB = $app.PrivateMemorySize64 / 1MB
        })
        [void]$app.WaitForExit(100)
    } while ($clock.Elapsed.TotalSeconds -lt $Seconds)
    $steady = $samples | Where-Object Time -ge ([Math]::Max(0,$Seconds - 5))
    $first = $steady | Select-Object -First 1
    $last = $steady | Select-Object -Last 1
    [pscustomobject]@{
        IdleCpuOneCorePercent = [Math]::Round(($last.CpuMs - $first.CpuMs) / (($last.Time - $first.Time) * 1000) * 100,3)
        EndWorkingSetMB = [Math]::Round($last.WorkingMB,1)
        EndPrivateMB = [Math]::Round($last.PrivateMB,1)
        PeakSampledWorkingSetMB = [Math]::Round(($samples | Measure-Object WorkingMB -Maximum).Maximum,1)
    } | ConvertTo-Json -Compress
}
finally {
    if (-not $app.HasExited) {
        $close = Start-Local '--close-and-wait'
        try { if (-not $close.WaitForExit(20000) -or $close.ExitCode -ne 0) { throw 'Ölçüm uygulaması güvenli kapanamadı.' } }
        finally { $close.Dispose() }
        [void]$app.WaitForExit(10000)
    }
    $app.Dispose()
    if (Test-Path -LiteralPath $cache) { Remove-Item -LiteralPath $cache -Recurse -Force }
}
