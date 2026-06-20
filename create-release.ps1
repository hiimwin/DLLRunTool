# Tạo GitHub Release v1.1.2 và upload zip
# Yêu cầu: gh auth login (chạy 1 lần)
# Usage: .\create-release.ps1

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$zip = Join-Path $root "publish\Win_Trung-MicroservicesControlPanel.zip"
$version = "1.1.2"

if (-not (Test-Path $zip)) {
    Write-Host "Chưa có zip. Chạy: .\publish.ps1 -Version `"$version`"" -ForegroundColor Yellow
    exit 1
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) {
    $portable = Join-Path $env:TEMP "gh-cli\bin\gh.exe"
    if (Test-Path $portable) { $gh = $portable } else { throw "Cần cài GitHub CLI: https://cli.github.com/ hoặc chạy publish.ps1 trước." }
}

& $gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Chưa đăng nhập GitHub. Chạy: gh auth login" -ForegroundColor Yellow
    exit 1
}

& $gh release create "v$version" $zip `
    --repo "hiimwin/DLLRunTool" `
    --title "v$version" `
    --notes "Sửa lọc log theo service, buffer không giới hạn dòng, net9 resolver"

Write-Host "Done. Release: https://github.com/hiimwin/DLLRunTool/releases/tag/v$version" -ForegroundColor Green
