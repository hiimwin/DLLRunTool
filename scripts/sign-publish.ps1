# Authenticode sign published binaries (giam SmartScreen khi co chung chi CA).
# Usage:
#   .\scripts\sign-publish.ps1 -PublishDir "..\publish\Win_Trung-MicroservicesControlPanel-build"
#   $env:MCCP_SIGN_PFX = "C:\certs\codesign.pfx"
#   $env:MCCP_SIGN_PASSWORD = "..."   # hoac SecureString / Windows Credential

param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,
    [string]$PfxPath = "",
    [string]$Password = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($PfxPath)) { $PfxPath = $env:MCCP_SIGN_PFX }
if ([string]::IsNullOrWhiteSpace($Password)) { $Password = $env:MCCP_SIGN_PASSWORD }

if ([string]::IsNullOrWhiteSpace($PfxPath) -or -not (Test-Path $PfxPath)) {
    Write-Host "==> Bo qua code signing (chua co MCCP_SIGN_PFX / -PfxPath)." -ForegroundColor DarkYellow
    return
}

$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if (-not $signtool) {
    $kits = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe",
        "${env:ProgramFiles}\Windows Kits\10\bin\*\x64\signtool.exe"
    )
    foreach ($pat in $kits) {
        $found = Get-Item $pat -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { $signtool = $found; break }
    }
}
if (-not $signtool) {
    throw "Khong tim thay signtool.exe. Cai Windows SDK (Signing Tools)."
}

$targets = @(
    (Join-Path $PublishDir "DLLRunTool.exe")
) | Where-Object { Test-Path $_ }

if ($targets.Count -eq 0) {
    throw "Khong co file can ky trong $PublishDir"
}

Write-Host "==> Code signing ($($targets.Count) file)..." -ForegroundColor Cyan
Write-Host "    Cert: $PfxPath" -ForegroundColor DarkGray

foreach ($file in $targets) {
    & $signtool sign `
        /fd SHA256 `
        /tr $TimestampUrl `
        /td SHA256 `
        /f $PfxPath `
        /p $Password `
        $file
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for $file (exit $LASTEXITCODE)"
    }
    Write-Host "    Signed: $(Split-Path $file -Leaf)" -ForegroundColor Green
}

Write-Host "==> Verify signature..." -ForegroundColor Cyan
& $signtool verify /pa /v $targets[0]
if ($LASTEXITCODE -ne 0) {
    throw "Signature verify failed"
}
