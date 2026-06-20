# Tao ban copy source sach (khong .git) — tuy chon.
param(
    [string]$SourceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")),
    [string]$TargetDir = "",
    [switch]$InitGit
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $TargetDir = Join-Path (Split-Path $SourceRoot -Parent) "DLLRunTool-clean"
}

$excludeDirs = @("bin", "obj", "publish", ".git", ".vs", "backups", "defaults")
$excludeFiles = @("paths.local.json", "global.*.json", "service-locks.json", "run-settings.json", "backup-*.json")

Write-Host "Source: $SourceRoot"
Write-Host "Target: $TargetDir"

if (Test-Path $TargetDir) {
    Write-Host "Thu muc dich da ton tai. Xoa hoac chon -TargetDir khac." -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Path $TargetDir | Out-Null

function ShouldSkip([string]$relativePath) {
    $parts = $relativePath -split "[\\/]"
    foreach ($p in $parts) {
        if ($excludeDirs -contains $p) { return $true }
        if ($p -like "*.WebView2") { return $true }
    }
    $name = Split-Path $relativePath -Leaf
    foreach ($pat in $excludeFiles) {
        if ($name -like $pat) { return $true }
    }
    return $false
}

Get-ChildItem -Path $SourceRoot -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($SourceRoot.Length).TrimStart("\", "/")
    if (ShouldSkip $rel) { return }

    $dest = Join-Path $TargetDir $rel
    $destDir = Split-Path $dest -Parent
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }
    Copy-Item $_.FullName $dest -Force
}

Write-Host "Da copy source sach -> $TargetDir" -ForegroundColor Green

if ($InitGit) {
    Push-Location $TargetDir
    git init
    git add -A
    Write-Host ""
    Write-Host "Da git init + git add. Ban tu commit va push:" -ForegroundColor Yellow
    Write-Host '  git commit -m "Initial commit: DLLRunTool v1.2.0"'
    Write-Host '  git remote add origin https://github.com/<user>/<repo>.git'
    Write-Host '  git branch -M main'
    Write-Host '  git push -u origin main'
    Pop-Location
} else {
    Write-Host ""
    Write-Host "Buoc tiep theo (ban tu lam):" -ForegroundColor Yellow
    Write-Host "  cd `"$TargetDir`""
    Write-Host "  git init"
    Write-Host "  git add -A"
    Write-Host '  git commit -m "Initial commit"'
    Write-Host "  git remote add origin <url-repo-moi>"
    Write-Host "  git push -u origin main"
}
