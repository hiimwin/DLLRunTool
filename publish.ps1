# Build & publish Win_Trung Microservices Control Panel
# Usage: .\publish.ps1 [-Version "1.2.0"] [-DownloadUrl "https://..."] [-ReleaseNotes "..."]

param(
    [string]$Version = "",
    [string]$DownloadUrl = "",
    [string]$ReleaseNotes = ""
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$project = Join-Path $root "DLLRunTool\DLLRunTool.csproj"
$outDir = Join-Path $root "publish\Win_Trung-MicroservicesControlPanel-build"
$zipPath = Join-Path $root "publish\Win_Trung-MicroservicesControlPanel.zip"
$manifestPath = Join-Path $root "update-manifest.json"

function Get-ProjectVersion([string]$csprojPath) {
    [xml]$xml = Get-Content $csprojPath
    foreach ($pg in $xml.Project.PropertyGroup) {
        if ($pg.Version) { return [string]$pg.Version }
    }
    return "1.0.0"
}

function Set-ProjectVersion([string]$csprojPath, [string]$newVersion) {
    [xml]$xml = Get-Content $csprojPath
    $updated = $false
    foreach ($pg in $xml.Project.PropertyGroup) {
        if ($null -ne $pg.Version) {
            $pg.Version = $newVersion
            $updated = $true
        }
        if ($null -ne $pg.AssemblyVersion) { $pg.AssemblyVersion = "$newVersion.0" }
        if ($null -ne $pg.FileVersion) { $pg.FileVersion = "$newVersion.0" }
    }
    if (-not $updated) { throw "Không tìm thấy <Version> trong csproj" }
    $xml.Save($csprojPath)
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion $project
} else {
    Set-ProjectVersion $project $Version
    Write-Host "==> Version set to $Version" -ForegroundColor Cyan
}

Write-Host "==> Restore & publish (Release, win-x64, self-contained) v$Version..." -ForegroundColor Cyan
dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -o $outDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Copy-Item (Join-Path $root "README.md") (Join-Path $outDir "README.md") -Force

$webviewProfile = Join-Path $outDir "DLLRunTool.exe.WebView2"
if (Test-Path $webviewProfile) {
    Remove-Item $webviewProfile -Recurse -Force
}

Write-Host "==> Create zip..." -ForegroundColor Cyan
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path $outDir -DestinationPath $zipPath -CompressionLevel Optimal

$existingManifest = @{}
if (Test-Path $manifestPath) {
    try { $existingManifest = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable } catch { }
}

if ([string]::IsNullOrWhiteSpace($DownloadUrl) -and $existingManifest.downloadUrl) {
    $DownloadUrl = [string]$existingManifest.downloadUrl
}
if ([string]::IsNullOrWhiteSpace($ReleaseNotes) -and $existingManifest.releaseNotes) {
    $ReleaseNotes = [string]$existingManifest.releaseNotes
}
if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    $ReleaseNotes = "Build v$Version"
}

$manifest = [ordered]@{
    version      = $Version
    releasedAt   = (Get-Date -Format "yyyy-MM-dd")
    downloadUrl  = $DownloadUrl
    releaseNotes = $ReleaseNotes
}

$manifestJson = ($manifest | ConvertTo-Json -Depth 3)
Set-Content -Path $manifestPath -Value $manifestJson -Encoding UTF8
Write-Host "==> Updated $manifestPath" -ForegroundColor Cyan
Write-Host "    Upload file này lên URL trong update-check.config.json sau mỗi release." -ForegroundColor Yellow

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
$exe = Join-Path $outDir "DLLRunTool.exe"

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  EXE : $exe"
Write-Host "  ZIP : $zipPath ($zipSize MB)"
Write-Host "  Ver : $Version"
