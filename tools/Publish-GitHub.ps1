# GitHub'a yükleme yardımcısı.
# Kullanım:
#   powershell -ExecutionPolicy Bypass -File tools\Publish-GitHub.ps1 -RepoUrl https://github.com/KULLANICI/TLOUP1.git
#   powershell -ExecutionPolicy Bypass -File tools\Publish-GitHub.ps1 -RepoUrl ... -Tag v1
param(
    [Parameter(Mandatory = $true)][string]$RepoUrl,
    [string]$Branch = 'main',
    [string]$Tag = '',
    [string]$Message = 'v1: Ilk surum'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $git = (Get-Command git -ErrorAction SilentlyContinue).Source
    if (-not $git) {
        $candidate = Join-Path $env:ProgramFiles 'Git\cmd\git.exe'
        if (Test-Path $candidate) { $git = $candidate }
        else { throw 'Git bulunamadı. Önce: winget install Git.Git' }
    }

    if (-not (Test-Path (Join-Path $root '.git'))) {
        & $git init
        & $git branch -M $Branch
    }

    & $git add -A
    & $git -c user.name='TLOUP1' -c user.email='tloup1@users.noreply.github.com' `
        commit -m $Message 2>$null
    if ($LASTEXITCODE -ne 0) { Write-Host 'Değişiklik yok veya commit zaten mevcut; devam ediliyor.' }

    $remotes = & $git remote
    if ($remotes -contains 'origin') {
        & $git remote set-url origin $RepoUrl
    } else {
        & $git remote add origin $RepoUrl
    }

    & $git push -u origin $Branch
    if ($LASTEXITCODE -ne 0) { throw 'Push başarısız. GitHub kimlik doğrulaması gerekli olabilir.' }

    if ($Tag) {
        & $git tag $Tag 2>$null
        & $git push origin $Tag
        if ($LASTEXITCODE -ne 0) { throw 'Etiket push başarısız.' }
        Write-Host "Etiket gönderildi: $Tag — GitHub Actions release oluşturacak."
    }

    Write-Output "TAMAM: $RepoUrl"
}
finally { Pop-Location }
