# Embed obfuscated update manifest URL into UpdateEndpointStore.cs before release build.
param(
    [string]$ConfigPath = "",
    [string]$StorePath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $ConfigPath) { $ConfigPath = Join-Path $root "DLLRunTool\update-check.config.json" }
if (-not $StorePath) { $StorePath = Join-Path $root "DLLRunTool\Services\UpdateEndpointStore.cs" }

if (-not (Test-Path $ConfigPath)) { throw "Missing $ConfigPath" }
if (-not (Test-Path $StorePath)) { throw "Missing $StorePath" }

$config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$url = [string]$config.manifestUrl
if ([string]::IsNullOrWhiteSpace($url)) { throw "manifestUrl is empty in $ConfigPath" }

Add-Type -AssemblyName System.Security
$utf8 = [System.Text.Encoding]::UTF8
$keyBytes = [System.Security.Cryptography.SHA256]::Create().ComputeHash($utf8.GetBytes("AKC.Products.MCP.UpdateEndpoint.v1"))[0..15]
$data = $utf8.GetBytes($url)
for ($i = 0; $i -lt $data.Length; $i++) {
    $data[$i] = $data[$i] -bxor $keyBytes[$i % $keyBytes.Length]
}
$payload = [Convert]::ToBase64String($data)

$content = Get-Content $StorePath -Raw
$pattern = 'private const string Payload = ".*?";'
$replacement = "private const string Payload = `"$payload`";"
if ($content -notmatch $pattern) { throw "Payload marker not found in UpdateEndpointStore.cs" }
$content = [regex]::Replace($content, $pattern, $replacement, 1)
Set-Content -Path $StorePath -Value $content -Encoding UTF8 -NoNewline
Write-Host "Embedded update endpoint ($($url.Length) chars -> payload $($payload.Length) chars)" -ForegroundColor Cyan
