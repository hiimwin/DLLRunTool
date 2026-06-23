# Xoa tat ca release cu, tao release v1 duy nhat voi zip sach hien tai.
# Yeu cau: gh auth login (1 lan) hoac set GH_TOKEN
# Usage: .\scripts\reset-releases-to-v1.ps1

param(
    [string]$Version = "1.0.0",
    [string]$Tag = "v1",
    [string]$Notes = "Win_Trung Microservices Control Panel - phien ban dau tien (zip khong lo URL update, paths rong, VI/EN)."
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$zip = Join-Path $root "publish\Win_Trung-MicroservicesControlPanel.zip"
$repo = "hiimwin/DLLRunTool"

function Resolve-Gh {
    $installed = "$env:ProgramFiles\GitHub CLI\gh.exe"
    if (Test-Path $installed) { return $installed }
    $cmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $portable = Join-Path $env:TEMP "gh-cli\bin\gh.exe"
    if (Test-Path $portable) { return $portable }
    throw "Chua co gh CLI. Cai: winget install GitHub.cli  roi chay: gh auth login"
}

if (-not (Test-Path $zip)) {
    throw "Chua co zip. Chay: .\publish.ps1 -Version `"$Version`""
}

$gh = Resolve-Gh
& $gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Chua dang nhap GitHub. Chay: gh auth login   (hoac set GH_TOKEN)"
}

$oldTags = @("v1.2.3", "v1.2.2", "v1.2.1", "v1.2.0", "v1.1.0")
foreach ($t in $oldTags) {
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "SilentlyContinue"
    & $gh release view $t --repo $repo 2>$null | Out-Null
    $exists = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = $prevEap
    if ($exists) {
        Write-Host "==> Delete release $t" -ForegroundColor Yellow
        & $gh release delete $t --repo $repo --yes --cleanup-tag
        if ($LASTEXITCODE -ne 0) { throw "Failed to delete release $t" }
    }
}

$prevEap = $ErrorActionPreference
$ErrorActionPreference = "SilentlyContinue"
& $gh release view $Tag --repo $repo 2>$null | Out-Null
$tagExists = ($LASTEXITCODE -eq 0)
$ErrorActionPreference = $prevEap

if ($tagExists) {
    Write-Host "==> Replace zip on existing $Tag" -ForegroundColor Cyan
    & $gh release upload $Tag $zip --repo $repo --clobber
    & $gh release edit $Tag --repo $repo --title $Tag --notes $Notes
} else {
    Write-Host "==> Create release $Tag" -ForegroundColor Cyan
    & $gh release create $Tag $zip --repo $repo --title $Tag --notes $Notes --latest
}

$url = "https://github.com/hiimwin/DLLRunTool/releases/download/$Tag/Win_Trung-MicroservicesControlPanel.zip"
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Tag         : $Tag"
Write-Host "  App version : $Version"
Write-Host "  downloadUrl : $url"
