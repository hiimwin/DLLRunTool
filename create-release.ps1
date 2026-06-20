# Tao hoac ghi de GitHub Release va upload zip
# Yeu cau: gh auth login (chay 1 lan)
# Usage:
#   .\create-release.ps1 -Version "1.2.3"
#   .\create-release.ps1 -Version "1.2.3" -Replace

param(
    [string]$Version = "1.2.3",
    [switch]$Replace,
    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$zip = Join-Path $root "publish\Win_Trung-MicroservicesControlPanel.zip"
if ([string]::IsNullOrWhiteSpace($Notes)) {
    $Notes = "v$Version — Zip khong lo URL update; README user; paths mac dinh rong."
}

if (-not (Test-Path $zip)) {
    Write-Host "Chua co zip. Chay: .\publish.ps1 -Version `"$Version`"" -ForegroundColor Yellow
    exit 1
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    $portable = Join-Path $env:TEMP "gh-cli\bin\gh.exe"
    if (Test-Path $portable) { $gh = $portable } else {
        Write-Host "Chua cai gh CLI. Ghi de release thu cong:" -ForegroundColor Yellow
        Write-Host "  1. https://github.com/hiimwin/DLLRunTool/releases/tag/v$Version"
        Write-Host "  2. Xoa asset zip cu, upload moi: $zip"
        exit 1
    }
}

& $gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Chua dang nhap GitHub. Chay: gh auth login" -ForegroundColor Yellow
    exit 1
}

$repo = "hiimwin/DLLRunTool"
$tag = "v$Version"

if ($Replace) {
    & $gh release upload $tag $zip --repo $repo --clobber
    if ($LASTEXITCODE -ne 0) { throw "gh release upload failed" }
    & $gh release edit $tag --repo $repo --notes $Notes
} else {
    & $gh release create $tag $zip --repo $repo --title $tag --notes $Notes
}

$downloadUrl = "https://github.com/hiimwin/DLLRunTool/releases/download/$tag/Win_Trung-MicroservicesControlPanel.zip"
Write-Host "Done. Release: https://github.com/hiimwin/DLLRunTool/releases/tag/$tag" -ForegroundColor Green
Write-Host "downloadUrl: $downloadUrl"
