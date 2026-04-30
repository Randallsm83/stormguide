<#
.SYNOPSIS
    Regenerate the trimmed game catalog under StormGuide/Resources/catalog/
    by running tools/CatalogTrim against a JSONLoader export.

.DESCRIPTION
    Mirrors the SDK-resolution logic in tools/SmokeRun.ps1: a runtime-only
    dotnet at C:\Program Files\dotnet\dotnet.exe satisfies Get-Command but
    fails any `dotnet run` with "No .NET SDKs were found.", so we explicitly
    require an adjacent sdk\ folder before accepting a candidate. Wins for
    being runnable from any working directory without remembering the env
    vars.

    Forwards any extra arguments straight through to `dotnet run` (after
    `--`), so flags like `-ExportPath` or `-OutPath` defined by
    tools/CatalogTrim still work. Run the project itself with `--help` to
    see them.

.EXAMPLE
    pwsh tools/RegenCatalog.ps1
    pwsh tools/RegenCatalog.ps1 -- --export "$env:USERPROFILE\AppData\LocalLow\Eremite Games\Against the Storm\JSONLoader\Exported"
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ProjectArgs
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$proj     = Join-Path $repoRoot 'tools\CatalogTrim'

# Resolve a dotnet *with an SDK*. Mirrors tools/SmokeRun.ps1.
function Test-DotnetHasSdk {
    param([string]$Exe)
    if (-not (Test-Path $Exe)) { return $false }
    return (Test-Path (Join-Path (Split-Path $Exe -Parent) 'sdk'))
}

$dotnet = $null
$existing = Get-Command dotnet -ErrorAction SilentlyContinue
if ($existing -and (Test-DotnetHasSdk -Exe $existing.Source)) {
    $dotnet = $existing.Source
}
if (-not $dotnet) {
    $candidateExes = @()
    $candidateExes += (Join-Path $env:USERPROFILE 'scoop\shims\dotnet.exe')
    $candidateExes += (Join-Path $env:USERPROFILE 'scoop\apps\dotnet-sdk\current\dotnet.exe')
    $versioned = Get-ChildItem `
        -Path (Join-Path $env:USERPROFILE 'scoop\apps\dotnet-sdk') `
        -Filter 'dotnet.exe' -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -ExpandProperty FullName
    $candidateExes += $versioned
    $candidateExes += 'C:\Program Files\dotnet\dotnet.exe'
    $candidateExes += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    foreach ($exe in $candidateExes) {
        if (Test-DotnetHasSdk -Exe $exe) { $dotnet = $exe; break }
    }
}

if (-not $dotnet) {
    Write-Host '== dotnet SDK not found (probed scoop shims, scoop versioned, Program Files, LocalAppData).' -ForegroundColor Red
    Write-Host '   Install via `scoop install dotnet-sdk` or download from https://aka.ms/dotnet/download.' -ForegroundColor Yellow
    exit 1
}

$dotnetDir = Split-Path $dotnet -Parent
$env:Path = "$dotnetDir;$env:Path"
$env:DOTNET_ROOT = $dotnetDir

Write-Host "==> Running CatalogTrim" -ForegroundColor Cyan
Write-Host "    project: $proj"        -ForegroundColor DarkGray
Write-Host "    using:   $dotnet"      -ForegroundColor DarkGray

if ($ProjectArgs -and $ProjectArgs.Count -gt 0) {
    & $dotnet run --project $proj -- @ProjectArgs
} else {
    & $dotnet run --project $proj
}
exit $LASTEXITCODE
