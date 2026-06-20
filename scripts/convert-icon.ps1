# Build multi-size app.ico from PNG for WinForms ApplicationIcon.
param(
    [string]$PngPath = "",
    [string]$IcoPath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $PngPath) { $PngPath = Join-Path $root "Assets\app-icon.png" }
if (-not $IcoPath) { $IcoPath = Join-Path $root "DLLRunTool\app.ico" }

if (-not (Test-Path $PngPath)) { throw "Missing PNG: $PngPath" }

Add-Type -AssemblyName System.Drawing

function New-SizedBitmap([System.Drawing.Image]$source, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
}

$source = [System.Drawing.Image]::FromFile($PngPath)
$sizes = @(16, 32, 48, 64, 128, 256)
$bitmaps = New-Object System.Collections.Generic.List[System.Drawing.Bitmap]
foreach ($s in $sizes) {
    $bitmaps.Add((New-SizedBitmap $source $s))
}
$source.Dispose()

# ICO container: ICONDIR + ICONDIRENTRY[] + image data
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms

$bw.Write([UInt16]0) # reserved
$bw.Write([UInt16]1) # type = icon
$bw.Write([UInt16]$bitmaps.Count)

$offset = 6 + (16 * $bitmaps.Count)
$imageDataList = New-Object System.Collections.Generic.List[byte[]]

foreach ($bmp in $bitmaps) {
    $pngMs = New-Object System.IO.MemoryStream
    $bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $data = $pngMs.ToArray()
    $pngMs.Dispose()
    $bmp.Dispose()
    $imageDataList.Add($data)

    $w = if ($data.Length -gt 0) { [Math]::Min(255, $sizes[$imageDataList.Count - 1]) } else { 0 }
    $bw.Write([byte]$w)
    $bw.Write([byte]$w)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}

foreach ($data in $imageDataList) {
    $bw.Write($data)
}

$bw.Flush()
[System.IO.File]::WriteAllBytes($IcoPath, $ms.ToArray())
$bw.Close()
$ms.Close()

Write-Host "Created $IcoPath" -ForegroundColor Green
