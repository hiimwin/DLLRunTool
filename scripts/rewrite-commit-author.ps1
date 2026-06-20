# Stage + rewrite single commit with win.trungle.dev@gmail.com (no Cursor co-author)
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

& git add -A

$env:GIT_AUTHOR_NAME = "Trung Le Quang"
$env:GIT_AUTHOR_EMAIL = "win.trungle.dev@gmail.com"
$env:GIT_COMMITTER_NAME = "Trung Le Quang"
$env:GIT_COMMITTER_EMAIL = "win.trungle.dev@gmail.com"

$tree = (& git write-tree).Trim()
$msgFile = Join-Path $env:TEMP "dllruntool-commit-msg.txt"
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText(
    $msgFile,
    "Initial commit: DLLRunTool v1.2.0`n`nSecure config: secrets in appsettings.secrets.json, safe backup/apply, global DB in .secrets file.`n",
    $utf8NoBom)

$new = (& git commit-tree $tree -F $msgFile).Trim()
if (-not $new -or $new.Length -ne 40) { throw "commit-tree failed: '$new'" }

& git reset --hard $new
Remove-Item $msgFile -Force -ErrorAction SilentlyContinue
& git log -1 --format=fuller
