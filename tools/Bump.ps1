<#
.SYNOPSIS
    Bump StormGuide's version in csproj and roll the CHANGELOG.md `Unreleased`
    section into a dated heading. Optionally stages both files.

.DESCRIPTION
    csproj <Version> and CHANGELOG.md `## [Unreleased]` must move together so
    a tagged release is consistent. This script is the only sanctioned path
    once it lands; do not edit either by hand.

.PARAMETER Version
    SemVer version (e.g. 0.1.0, 1.0.0). Required.

.PARAMETER Stage
    `git add` the modified files after editing.

.EXAMPLE
    pwsh tools/Bump.ps1 -Version 0.1.0
    pwsh tools/Bump.ps1 -Version 1.0.0 -Stage
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$Stage
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "Version must be SemVer x.y.z (got '$Version')."
}

$repoRoot  = Split-Path -Parent $PSScriptRoot
$proj      = Join-Path $repoRoot 'StormGuide\StormGuide.csproj'
$changelog = Join-Path $repoRoot 'CHANGELOG.md'

if (-not (Test-Path $proj))      { Write-Error "Missing $proj" }
if (-not (Test-Path $changelog)) { Write-Error "Missing $changelog" }

# 1. csproj <Version>
$xml = [xml](Get-Content $proj -Raw)
$pg  = $xml.Project.PropertyGroup | Where-Object { $_.Version }
if (-not $pg) { Write-Error "No <Version> element found in $proj" }
$current = $pg.Version
Write-Host "csproj : $current  ->  $Version" -ForegroundColor Cyan
$pg.Version = $Version
$xml.Save($proj)

# 2. CHANGELOG: roll [Unreleased] body into a dated [<version>] section,
# leaving an empty [Unreleased] heading on top.
$content = Get-Content $changelog -Raw
$today   = (Get-Date).ToString('yyyy-MM-dd')

$pattern = '(?ms)^## \[Unreleased\][^\n]*\r?\n(.*?)(?=^## \[|\z)'
$m = [regex]::Match($content, $pattern)
if (-not $m.Success) { Write-Error "Couldn't find ## [Unreleased] section in $changelog" }
$body = $m.Groups[1].Value.Trim()
if (-not $body) {
    Write-Warning "Unreleased section is empty; the [$Version] entry will be empty too."
}

$newSection = "## [Unreleased]`n`n## [$Version] - $today`n`n$body`n"
$updated    = [regex]::Replace($content, $pattern, $newSection, 1)

# 3. Add a comparison link at the bottom if one isn't already there.
$linkLine = "[$Version]: https://github.com/Randallsm83/stormguide/releases/tag/v$Version"
if ($updated -notmatch [regex]::Escape("[$Version]: ")) {
    if ($updated.TrimEnd().EndsWith('---')) {
        $updated = $updated.TrimEnd() + "`n$linkLine`n"
    } else {
        $updated = $updated.TrimEnd() + "`n$linkLine`n"
    }
}

# Refresh the Unreleased compare link so it points at the new tag.
$updated = [regex]::Replace(
    $updated,
    '(?m)^\[Unreleased\]:\s*https://github\.com/Randallsm83/stormguide/compare/.+$',
    "[Unreleased]: https://github.com/Randallsm83/stormguide/compare/v$Version...HEAD"
)

Set-Content -Path $changelog -Value $updated -NoNewline -Encoding UTF8
Write-Host "CHANGELOG : Unreleased  ->  [$Version] $today" -ForegroundColor Cyan

if ($Stage) {
    git -C $repoRoot add 'StormGuide/StormGuide.csproj' 'CHANGELOG.md'
    Write-Host "Staged csproj + CHANGELOG.md" -ForegroundColor Green
}

Write-Host ""
Write-Host "Next:" -ForegroundColor Yellow
Write-Host "  git -C `"$repoRoot`" commit -m `"chore: bump to $Version`""
Write-Host "  git -C `"$repoRoot`" tag v$Version"
Write-Host "  git -C `"$repoRoot`" push --follow-tags"
Write-Host "  pwsh tools/Pack.ps1 -Publish        # build + upload zip to the release"
