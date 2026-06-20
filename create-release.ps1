# Tạo GitHub Release và upload zip
# Yêu cầu: gh auth login (chạy 1 lần)
# Usage: .\create-release.ps1 [-Version "1.2.0"]

param(
    [string]$Version = "1.2.0"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$zip = Join-Path $root "publish\Win_Trung-MicroservicesControlPanel.zip"
$notes = "v$Version — Bảo mật config: secrets tách khỏi appsettings.json; global DB password file .secrets riêng; backup/apply an toàn."

if (-not (Test-Path $zip)) {
    Write-Host "Chưa có zip. Chạy: .\publish.ps1 -Version `"$Version`"" -ForegroundColor Yellow
    exit 1
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    $portable = Join-Path $env:TEMP "gh-cli\bin\gh.exe"
    if (Test-Path $portable) { $gh = $portable } else {
        Write-Host "Chưa cài gh CLI. Tạo release thủ công:" -ForegroundColor Yellow
        Write-Host "  1. https://github.com/hiimwin/DLLRunTool/releases/new"
        Write-Host "  2. Tag: v$Version"
        Write-Host "  3. Upload: $zip"
        exit 1
    }
}

& $gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Chưa đăng nhập GitHub. Chạy: gh auth login" -ForegroundColor Yellow
    exit 1
}

& $gh release create "v$Version" $zip `
    --repo "hiimwin/DLLRunTool" `
    --title "v$Version" `
    --notes $notes

$downloadUrl = "https://github.com/hiimwin/DLLRunTool/releases/download/v$Version/Win_Trung-MicroservicesControlPanel.zip"
Write-Host "Done. Release: https://github.com/hiimwin/DLLRunTool/releases/tag/v$Version" -ForegroundColor Green
Write-Host "downloadUrl: $downloadUrl"
