# Rewrite unpushed commits — remove Cursor Co-authored-by trailer (run before push)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$Git = "C:\Program Files\Git\cmd\git.exe"
if (-not (Test-Path $Git)) { $Git = "git" }

function Invoke-Git([string[]]$GitArgs) {
    $out = & $Git @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs -join ' ') failed: $out" }
    return ($out | Out-String).Trim()
}

function New-CleanCommit([string]$Tree, [string]$Parent, [string]$Body) {
    $msgFile = [IO.Path]::GetTempFileName()
    try {
        $utf8 = New-Object System.Text.UTF8Encoding $false
        [IO.File]::WriteAllText($msgFile, $Body.TrimEnd() + "`n", $utf8)
        return Invoke-Git @("commit-tree", $Tree, "-p", $Parent, "-F", $msgFile)
    } finally {
        Remove-Item $msgFile -Force -ErrorAction SilentlyContinue
    }
}

$upstream = Invoke-Git @("rev-parse", '@{upstream}')
$head = Invoke-Git @("rev-parse", "HEAD")

if ($upstream -eq $head) {
    Write-Host "Nothing to rewrite (HEAD = upstream)." -ForegroundColor Green
    exit 0
}

$commits = (Invoke-Git @("rev-list", "--reverse", "$upstream..HEAD")).Split(@("`n", "`r`n"), [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() } | Where-Object { $_ }
if ($commits.Count -eq 0) { exit 0 }

$parent = $upstream
foreach ($sha in $commits) {
    $tree = Invoke-Git @("show", "-s", "--format=%T", $sha)
    $raw = Invoke-Git @("log", "-1", "--format=%B", $sha)
    $lines = $raw -split "`n" | Where-Object {
        $_ -notmatch '^\s*Co-authored-by:\s*Cursor\s*<cursoragent@cursor\.com>\s*$'
    }
    $body = ($lines -join "`n").TrimEnd()
    if ([string]::IsNullOrWhiteSpace($body)) { throw "Empty message after strip for $sha" }
    $parent = New-CleanCommit $tree $parent $body
    Write-Host "Rewrote $sha -> $parent" -ForegroundColor DarkGray
}

Invoke-Git @("reset", "--hard", $parent) | Out-Null
Write-Host "Clean history: $upstream..HEAD ($($commits.Count) commit(s))" -ForegroundColor Green

$check = Invoke-Git @("log", '@{upstream}..HEAD', '--format=%B')
if ($check -match '(?i)co-authored-by:.*cursor') {
    throw "Cursor trailer still present - chay script trong PowerShell ngoai Cursor (Win+X -> Terminal)."
}
