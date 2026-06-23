# Build, commit, push, GitHub Release — end-to-end (author: Trung Le Quang, no sensitive files)
param(
    [string]$Version = "",
    [string]$ReleaseNotes = "",
    [string]$Repo = "hiimwin/DLLRunTool",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$AuthorName = "Trung Le Quang"
$AuthorEmail = "TrungLQ@utop.io"

$gh = "$env:ProgramFiles\GitHub CLI\gh.exe"
if (-not (Test-Path $gh)) { $gh = "gh" }

function Test-SensitiveTrackedFiles {
    $bad = git ls-files | Where-Object {
        $_ -match '(?i)(^|/)paths\.local\.json$' -or
        $_ -match '(?i)global\..*\.secrets\.json$' -or
        $_ -match '(?i)global\..*\.be\.json$' -or
        $_ -match '(?i)appsettings\.secrets\.json$' -or
        $_ -match '(?i)backup-.*\.json$' -or
        $_ -match '(?i)(^|/)backups/' -or
        $_ -match '(?i)(^|/)defaults/' -or
        $_ -match '(?i)service-locks\.json$' -or
        $_ -match '(?i)run-settings\.json$' -or
        $_ -match '\.(pfx|pem)$' -or
        $_ -match '(?i)credentials'
    }
    if ($bad) {
        throw "Sensitive files tracked in git (remove before release):`n$($bad -join "`n")"
    }
}

function Test-NoCoAuthorCursor {
    $range = "@{upstream}..HEAD"
    $exists = git rev-parse --verify $range 2>$null
    if (-not $exists) { return }
    $msgs = git log $range --format="%B"
    if ($msgs -match '(?i)co-authored-by:.*cursor') {
        throw "Commit message contains Cursor co-author — rewrite before push."
    }
}

function Commit-Clean([string]$Message) {
    $parent = (git rev-parse HEAD).Trim()
    $tree = (git write-tree).Trim()
    $msgFile = Join-Path $env:TEMP "dllruntool-release-msg.txt"
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($msgFile, $Message.TrimEnd() + "`n", $utf8NoBom)
    $new = (git commit-tree $tree -p $parent -F $msgFile).Trim()
    Remove-Item $msgFile -Force -ErrorAction SilentlyContinue
    if ($new.Length -ne 40) { throw "commit-tree failed: '$new'" }
    git reset --hard $new | Out-Null
}

    & $gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { return }

    if ($env:GITHUB_TOKEN) {
        $env:GITHUB_TOKEN | & $gh auth login --with-token
        if ($LASTEXITCODE -ne 0) { throw "gh auth login --with-token failed" }
        return
    }

    throw @"
Chua dang nhap GitHub CLI. Chay mot lan (browser, tai khoan hiimwin):
  & `"$gh`" auth login -h github.com -p https -w
Hoac dat bien moi truong GITHUB_TOKEN (PAT co quyen repo) roi chay lai script.
"@
}

Write-Host "==> Security check (tracked files)..." -ForegroundColor Cyan
Test-SensitiveTrackedFiles
Test-NoCoAuthorCursor

$project = Join-Path $root "DLLRunTool\DLLRunTool.csproj"
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$xml = Get-Content $project
    foreach ($pg in $xml.Project.PropertyGroup) {
        if ($pg.Version) { $Version = [string]$pg.Version; break }
    }
}
if ([string]::IsNullOrWhiteSpace($Version)) { throw "Version required" }

$tag = "v$Version"
$downloadUrl = "https://github.com/$Repo/releases/download/$tag/Win_Trung-MicroservicesControlPanel.zip"
if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    $ReleaseNotes = "Build v$Version"
}

if (-not $SkipBuild) {
    Write-Host "==> Build & zip v$Version..." -ForegroundColor Cyan
    & (Join-Path $root "publish.ps1") -Version $Version -DownloadUrl $downloadUrl -ReleaseNotes $ReleaseNotes
}

$zip = Join-Path $root "publish\Win_Trung-MicroservicesControlPanel.zip"
if (-not (Test-Path $zip)) { throw "Missing zip: $zip" }

$env:GIT_AUTHOR_NAME = $AuthorName
$env:GIT_AUTHOR_EMAIL = $AuthorEmail
$env:GIT_COMMITTER_NAME = $AuthorName
$env:GIT_COMMITTER_EMAIL = $AuthorEmail

$dirty = git status --porcelain
if ($dirty) {
    Write-Host "==> Commit source changes..." -ForegroundColor Cyan
    git add update-manifest.json DLLRunTool/ scripts/ .gitignore
    $stillDirty = git diff --cached --quiet; if ($LASTEXITCODE -ne 0) {
        Commit-Clean "Release $tag`: $ReleaseNotes"
    }
}

Write-Host "==> Push main ($AuthorName <$AuthorEmail>)..." -ForegroundColor Cyan
git push origin main
if ($LASTEXITCODE -ne 0) { throw "git push failed" }

Write-Host "==> GitHub Release $tag..." -ForegroundColor Cyan
Ensure-GhAuth

$releaseExists = & $gh release view $tag --repo $Repo 2>$null
if ($LASTEXITCODE -eq 0) {
    & $gh release upload $tag $zip --repo $Repo --clobber
    & $gh release edit $tag --repo $Repo --notes $ReleaseNotes
} else {
    & $gh release create $tag $zip --repo $Repo --title $tag --notes $ReleaseNotes
}
if ($LASTEXITCODE -ne 0) { throw "gh release failed" }

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Release: https://github.com/$Repo/releases/tag/$tag"
Write-Host "  ZIP    : $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 2)) MB)"
Write-Host "  Author : $AuthorName <$AuthorEmail>"
