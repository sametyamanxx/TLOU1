$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet test TLOUTrainer.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Testler başarısız.' }
    dotnet restore src/TLOUTrainer/TLOUTrainer.csproj -r win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Çalışma paketleri hazırlanamadı.' }

    # Include the runtime licenses in the executable, not as loose distribution files.
    $assets = Get-Content 'src/TLOUTrainer/obj/project.assets.json' -Raw | ConvertFrom-Json
    $notices = New-Object Text.StringBuilder
    foreach ($pack in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
        $library = $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies |
            Where-Object { $_.name -eq $pack } | Select-Object -First 1
        if ($null -eq $library) { throw "Çalışma paketi bulunamadı: $pack" }
        $packageFolder = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
        $version = $library.version.Trim('[',']').Split(',')[0].Trim()
        $package = Join-Path $packageFolder ($pack.ToLowerInvariant() + '/' + $version)
        $licenses = Get-ChildItem -LiteralPath $package -File |
            Where-Object { $_.Name -in @('LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') }
        if (-not $licenses) { throw "Lisans metni bulunamadı: $pack" }
        foreach ($file in $licenses) {
            [void]$notices.AppendLine("$pack / $($file.Name)")
            [void]$notices.AppendLine([IO.File]::ReadAllText($file.FullName))
        }
    }
    [void][IO.Directory]::CreateDirectory((Join-Path $root '.build'))
    [IO.File]::WriteAllText((Join-Path $root '.build\RuntimeNotices.txt'), $notices.ToString(),
        (New-Object Text.UTF8Encoding $false))

    $stage = Join-Path "$env:LOCALAPPDATA\Temp\opencode" ('TLOUP1-single-' + [Guid]::NewGuid().ToString('N'))
    dotnet publish src/TLOUTrainer/TLOUTrainer.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Tek EXE yayın başarısız.' }
    $built = Get-ChildItem -LiteralPath $stage -File -Recurse
    if ($built.Count -ne 1 -or $built.Name -ne 'TLOUP1.exe') { throw 'Yayın tek EXE değil.' }
    $destination = Join-Path $root 'portable\TLOUP1'
    $exe = Join-Path $destination 'TLOUP1.exe'
    if (Test-Path -LiteralPath $exe) {
        $close = Start-Process -FilePath $exe -ArgumentList '--close-and-wait' -PassThru -Wait
        if ($close.ExitCode -ne 0) { throw 'Açık uygulama güvenli kapatılamadı; eski dosyalar korundu.' }
    }

    # Also exercise the recovery process from the actual single-file binary, using
    # synthetic cmd.exe targets rather than a running game.
    $previousTestExe = $env:TLOUP1_TEST_EXE
    $previousCache = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
    $testCache = Join-Path "$env:LOCALAPPDATA\Temp\opencode" ('TLOUP1-recovery-test-' + [Guid]::NewGuid().ToString('N'))
    try {
        $env:TLOUP1_TEST_EXE = Join-Path $stage 'TLOUP1.exe'
        $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $testCache
        dotnet test TLOUTrainer.sln -c Release --no-build --filter 'FullyQualifiedName~RecoveryTests'
        if ($LASTEXITCODE -ne 0) { throw 'Tek EXE kurtarma testi başarısız.' }
    }
    finally {
        $env:TLOUP1_TEST_EXE = $previousTestExe
        $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $previousCache
        if (Test-Path -LiteralPath $testCache) { Remove-Item -LiteralPath $testCache -Recurse -Force }
    }

    # Validate the standalone bundle before replacing or removing old dependencies.
    # Skip UI smoke test in CI (headless) — unit + recovery tests already ran.
    if (-not $env:GITHUB_ACTIONS) {
        & "$PSScriptRoot\Test-Portable.ps1" -Folder $stage
    } else {
        Write-Host "CI ortamı: Test-Portable (UI duman testi) atlanıyor."
    }
    [void][IO.Directory]::CreateDirectory($destination)
    Copy-Item -LiteralPath (Join-Path $stage 'TLOUP1.exe') -Destination $exe -Force
    foreach ($item in (Get-ChildItem -LiteralPath $destination -Force)) {
        if ($item.Name -ne 'TLOUP1.exe') { Remove-Item -LiteralPath $item.FullName -Recurse -Force }
    }
    Remove-Item -LiteralPath (Join-Path $stage 'TLOUP1.exe')
    Remove-Item -LiteralPath $stage
    Write-Output "PORTABLE EXE: $exe"
    Write-Output "FILES: $((Get-ChildItem -LiteralPath $destination -Recurse -File).Count)"
    Write-Output "SHA256: $((Get-FileHash $exe -Algorithm SHA256).Hash)"
}
finally { Pop-Location }
