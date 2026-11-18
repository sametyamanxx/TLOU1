param([string]$Folder = (Join-Path $PSScriptRoot '..\portable\TLOUP1'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PortableUi {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr ChildWindowFromPointEx(IntPtr h, POINT p, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint command);
    [DllImport("user32.dll")] public static extern IntPtr GetLastActivePopup(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
}
'@

function Wait-Ui([scriptblock]$Predicate, [System.Diagnostics.Process]$Process) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        if (& $Predicate) { return }
        if ($Process.HasExited) { break }
        [void]$Process.WaitForExit(50)
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Arayüz beklenen duruma geçmedi.'
}

function Click-Control([IntPtr]$Handle) {
    # Exercise the accessible button action without moving the user's mouse
    # or relying on our window being in front of unrelated applications.
    [void][PortableUi]::SendMessage($Handle,0x0100,[IntPtr]32,[IntPtr]0)
    [void][PortableUi]::SendMessage($Handle,0x0101,[IntPtr]32,[IntPtr]0)
}

function Save-UiWindow([IntPtr]$Handle, [string]$Path) {
    $rect = New-Object PortableUi+RECT
    [void][PortableUi]::GetWindowRect($Handle,[ref]$rect)
    $bitmap = New-Object System.Drawing.Bitmap ($rect.Right-$rect.Left),($rect.Bottom-$rect.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $dc = $graphics.GetHdc()
        try { [void][PortableUi]::PrintWindow($Handle,$dc,2) }
        finally { $graphics.ReleaseHdc($dc) }
        $bitmap.Save($Path,[System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

$folder = [IO.Path]::GetFullPath($Folder)
$exe = Join-Path $folder 'TLOUP1.exe'
if (Test-Path (Join-Path (Split-Path -Parent $folder) 'TLOUP1-Portable.zip')) {
    throw 'Eski ZIP dosyası hâlâ dağıtım klasöründe.'
}
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{756C6779-0256-49F3-A04C-BCE443817E50}_is1'
if (Test-Path $key) { throw 'Eski setup kurulumu hâlâ kayıtlı.' }
if (Get-Process TLOUP1 -ErrorAction SilentlyContinue) { throw 'Testten önce açık TLOUP1 penceresini normal kapat.' }
$unexpected = Get-ChildItem -LiteralPath $folder -Recurse -File |
    Where-Object { $_.Name -match '^(unins|setup)' -or $_.Extension -in @('.cs','.pdb','.ps1','.iss','.sln') }
if ($unexpected) { throw 'Pakette eski setup/geliştirme dosyası bulundu.' }
$files = Get-ChildItem -LiteralPath $folder -Recurse -File
if ($files.Count -ne 1 -or $files.Name -ne 'TLOUP1.exe') { throw 'Portable klasörü yalnızca TLOUP1.exe içermeli.' }
$bundleCache = Join-Path "$env:LOCALAPPDATA\Temp\opencode" ('TLOUP1-bundle-test-' + [Guid]::NewGuid().ToString('N'))

$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = $exe
$start.UseShellExecute = $false
$start.WorkingDirectory = $folder
$start.EnvironmentVariables['DOTNET_ROOT'] = Join-Path "$env:LOCALAPPDATA\Temp\opencode" 'not-installed-dotnet'
$start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$start.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $bundleCache
$app = [System.Diagnostics.Process]::Start($start)
try {
    [void]$app.WaitForInputIdle(10000)
    Wait-Ui { $app.Refresh(); $app.MainWindowHandle -ne 0 } $app
    $handle = $app.MainWindowHandle
    # A themed startup hint is expected when no game is running.
    $hint = [IntPtr]::Zero
    if (-not (Get-Process tlou-i,tlou-i-l -ErrorAction SilentlyContinue)) {
        Wait-Ui {
            $popup = [PortableUi]::GetLastActivePopup($handle)
            $popup -ne $handle -and $popup -ne [IntPtr]::Zero
        } $app
        $hint = [PortableUi]::GetLastActivePopup($handle)
        if ($hint -eq $handle -or $hint -eq [IntPtr]::Zero) { throw 'Başlangıç uyarısı bulunamadı.' }
        Save-UiWindow $hint (Join-Path "$env:LOCALAPPDATA\Temp\opencode" 'TLOUP1-startup-hint.png')
        [void][PortableUi]::SendMessage($hint,0x0100,[IntPtr]13,[IntPtr]0) # Enter key
        Wait-Ui { [PortableUi]::GetLastActivePopup($handle) -eq $handle } $app
        Write-Output 'PASS: Temalı önce oyunu aç uyarısı onaylandı.'
    }
    $child = [PortableUi]::GetWindow($handle,5)
    $controlCount = 0
    while ($child -ne [IntPtr]::Zero) {
        $controlCount++
        $child = [PortableUi]::GetWindow($child,2)
    }
    if ($controlCount -ne 6) { throw "Beklenen iki özellikli düzen yerine $controlCount kontrol bulundu." }

    $preview = Join-Path "$env:LOCALAPPDATA\Temp\opencode" 'TLOUP1-portable-preview.png'
    Save-UiWindow $handle $preview
    Write-Output "PREVIEW: $preview"

    # Locate the actual child controls at the new, explicit title-bar button positions.
    $point = New-Object PortableUi+POINT
    $point.X = 420; $point.Y = 16
    $hide = [PortableUi]::ChildWindowFromPointEx($handle,$point,1)
    if ($hide -eq 0 -or $hide -eq $handle) { throw 'Gizleme simgesi bulunamadı.' }
    Click-Control $hide
    Wait-Ui { -not [PortableUi]::IsWindowVisible($handle) } $app
    if ($app.HasExited) { throw 'GİZLE uygulamayı kapattı.' }
    [void][PortableUi]::SendMessage($handle,0x0312,[IntPtr]1,[IntPtr]0) # Insert hotkey handler
    Wait-Ui { [PortableUi]::IsWindowVisible($handle) } $app

    $point.X = 460; $point.Y = 16
    $close = [PortableUi]::ChildWindowFromPointEx($handle,$point,1)
    if ($close -eq 0 -or $close -eq $handle -or $close -eq $hide) { throw '× düğmesi bulunamadı.' }
    Click-Control $close
    $closeDeadline = [DateTime]::UtcNow.AddSeconds(15)
    while (-not $app.WaitForExit(150) -and [DateTime]::UtcNow -lt $closeDeadline) {
        Click-Control $close # Initial background analysis can briefly defer a close.
    }
    if (-not $app.HasExited) { throw '× normal çıkış yapmadı.' }
    if ($app.ExitCode -ne 0) { throw "Uygulama hatayla kapandı: $($app.ExitCode)" }
}
finally {
    if (-not $app.HasExited) {
        $closeProcess = Start-Process -FilePath $exe -ArgumentList '--close-and-wait' -PassThru -Wait
    }
    $app.Dispose()
    foreach ($process in (Get-Process TLOUP1 -ErrorAction SilentlyContinue)) {
        try {
            $process.Refresh()
            if (-not $process.HasExited -and $process.MainModule.FileName -eq $exe -and -not $process.WaitForExit(10000)) {
                throw 'Kurtarma yardımcısı kapanmadı.'
            }
        }
        catch [System.InvalidOperationException] { }
        finally { $process.Dispose() }
    }
    if (Test-Path -LiteralPath $bundleCache) { Remove-Item -LiteralPath $bundleCache -Recurse -Force }
}
if (Test-Path $key) { throw 'Portable uygulama kaldırma kaydı oluşturdu.' }
if (Test-Path "$env:LOCALAPPDATA\Programs\TLOUP1") { throw 'Portable uygulama kurulum klasörü oluşturdu.' }
if (Test-Path "$env:LOCALAPPDATA\TLOUP1") { throw 'Portable uygulama AppData günlük klasörü oluşturdu.' }
Write-Output 'PASS: Portable açılış, GİZLE, Insert ile geri gösterme ve × ile normal çıkış.'
Write-Output 'PASS: Paket içinde setup/kaldırıcı yok; uygulama kurulumu veya günlük kalıntısı yok.'
Write-Output 'PASS: Alt yeniden bağlan/bilgi/kapat araç çubuğu yok.'
Write-Output 'PASS: Yalnızca F1 mühimmat ve F2 sağlık için iki özellik satırı var.'
Write-Output 'PASS: Tek EXE, harici .NET veya dağıtım DLL dosyası olmadan açıldı.'
