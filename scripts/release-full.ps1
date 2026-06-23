# Build, commit, push, GitHub Release - end-to-end (author: Trung Le Quang, no sensitive files)
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

$Git = "C:\Program Files\Git\cmd\git.exe"
if (-not (Test-Path $Git)) { $Git = "git" }

$gh = "$env:ProgramFiles\GitHub CLI\gh.exe"
if (-not (Test-Path $gh)) { $gh = "gh" }

function Invoke-Git([string[]]$GitArgs) {
    $out = & $Git @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs -join ' ') failed: $out" }
    return ($out | Out-String).Trim()
}

function Test-SensitiveTrackedFiles {
    $files = Invoke-Git @("ls-files")
    $bad = $files -split "`n" | Where-Object {
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
    $upstream = Invoke-Git @("rev-parse", '@{upstream}')
    $head = Invoke-Git @("rev-parse", "HEAD")
    if ($upstream -eq $head) { return }
    $msgs = Invoke-Git @("log", ($upstream + ".." + $head), "--format=%B")
    if ($msgs -match '(?i)co-authored-by:.*cursor') {
        throw "Commit message contains Cursor co-author - rewrite before push."
    }
}

function Commit-Clean([string]$Message) {
    Invoke-Git @("add", "update-manifest.json", "DLLRunTool/", "scripts/", ".gitignore", "release.bat") | Out-Null
    $parent = Invoke-Git @("rev-parse", "HEAD")
    $tree = Invoke-Git @("write-tree")
    $msgFile = [IO.Path]::GetTempFileName()
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [IO.File]::WriteAllText($msgFile, $Message.TrimEnd() + "`n", $utf8NoBom)
    try {
        $new = Invoke-Git @("commit-tree", $tree, "-p", $parent, "-F", $msgFile)
    } finally {
        if (Test-Path -LiteralPath $msgFile) { Remove-Item -LiteralPath $msgFile -Force }
    }
    Invoke-Git @("reset", "--hard", $new) | Out-Null
}

function Ensure-GhAuth {
    & $gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { return }

    if ($env:GITHUB_TOKEN) {
        $env:GITHUB_TOKEN | & $gh auth login --with-token
        if ($LASTEXITCODE -ne 0) { throw "gh auth login --with-token failed" }
        return
    }

    throw "Chua dang nhap GitHub CLI. Chay: $gh auth login -h github.com -p https -w (tai khoan hiimwin). Hoac dat GITHUB_TOKEN."
}

Write-Host "==> Security check (tracked files)..." -ForegroundColor Cyan
Test-SensitiveTrackedFiles

Write-Host "==> Strip Cursor co-author from unpushed commits..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "rewrite-unpushed-clean.ps1")
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
    Write-Host "==> Build and zip v$Version..." -ForegroundColor Cyan
    & (Join-Path $root "publish.ps1") -Version $Version -DownloadUrl $downloadUrl -ReleaseNotes $ReleaseNotes
}

$zip = Join-Path $root "publish\Win_Trung-MicroservicesControlPanel.zip"
if (-not (Test-Path $zip)) { throw "Missing zip: $zip" }

$env:GIT_AUTHOR_NAME = $AuthorName
$env:GIT_AUTHOR_EMAIL = $AuthorEmail
$env:GIT_COMMITTER_NAME = $AuthorName
$env:GIT_COMMITTER_EMAIL = $AuthorEmail

$dirty = Invoke-Git @("status", "--porcelain")
if ($dirty) {
    Write-Host "==> Commit source changes..." -ForegroundColor Cyan
    Commit-Clean "Add release automation scripts (release-full.ps1, release.bat)."
    & (Join-Path $PSScriptRoot "rewrite-unpushed-clean.ps1")
    Test-NoCoAuthorCursor
}

Write-Host "==> Push main ($AuthorName / $AuthorEmail)..." -ForegroundColor Cyan
Invoke-Git @("push", "origin", "main") | Out-Null

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

$zipMb = [math]::Round((Get-Item $zip).Length / 1048576, 2)
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Release: https://github.com/$Repo/releases/tag/$tag"
Write-Host "  ZIP    : $zip ($zipMb MB)"
Write-Host "  Author : $AuthorName / $AuthorEmail"
