<#
.SYNOPSIS
    StormGuide smoke-test: build the plugin, then tail the r2modman BepInEx
    log for ~30 seconds, surfacing only [StormGuide] lines.

.DESCRIPTION
    The script does NOT launch the game. r2modman owns the launch flow per
    the project's AGENTS.md. Use this script before/while you start the
    game manually to confirm the plugin is loading and not throwing.

.PARAMETER Seconds
    How long to tail the log (default: 30).

.PARAMETER NoBuild
    Skip the dotnet build step. Useful when the deployed DLL is already current
    (e.g. after a successful build in another session).

.EXAMPLE
    pwsh tools/SmokeRun.ps1
    pwsh tools/SmokeRun.ps1 -Seconds 60
    pwsh tools/SmokeRun.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    [int]$Seconds = 30,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$proj     = Join-Path $repoRoot 'StormGuide\StormGuide.csproj'

# Resolve a dotnet *with an SDK*. The runtime-only install at
# C:\Program Files\dotnet\dotnet.exe will satisfy Get-Command but fails any
# `dotnet build` with "No .NET SDKs were found.", so we explicitly require an
# adjacent sdk\ folder before accepting a candidate.
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
    # Search candidates in order; the first SDK-bearing dotnet wins.
    $candidateExes = @()
    $candidateExes += (Join-Path $env:USERPROFILE 'scoop\shims\dotnet.exe')
    $candidateExes += (Join-Path $env:USERPROFILE 'scoop\apps\dotnet-sdk\current\dotnet.exe')
    # Glob versioned scoop installs (e.g. dotnet-sdk\10.0.203\dotnet.exe).
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
if ($dotnet) {
    $dotnetDir = Split-Path $dotnet -Parent
    $env:Path = "$dotnetDir;$env:Path"
    # DOTNET_ROOT pins the SDK lookup so the runtime-only Program Files
    # install never wins inside child processes.
    $env:DOTNET_ROOT = $dotnetDir
}

if (-not $NoBuild) {
    Write-Host "==> Building $proj" -ForegroundColor Cyan
    if (-not $dotnet) {
        Write-Host '== dotnet SDK not found (probed scoop shims, scoop versioned, Program Files, LocalAppData).' -ForegroundColor Red
        Write-Host '   Re-run with -NoBuild if the deployed DLL is already current.' -ForegroundColor Yellow
        exit 1
    }
    Write-Host "    using $dotnet" -ForegroundColor DarkGray
    & $dotnet build $proj -c Release -nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host '== build failed; aborting smoke-run.' -ForegroundColor Red
        exit $LASTEXITCODE
    }
}
else {
    Write-Host '==> -NoBuild set; using whatever DLL is already deployed.' -ForegroundColor Cyan
}

$logPath = Join-Path $env:APPDATA `
    'r2modmanPlus-local\AgainstTheStorm\profiles\Default\BepInEx\LogOutput.log'

if (-not (Test-Path $logPath)) {
    Write-Host "== no log at $logPath" -ForegroundColor Yellow
    Write-Host '   start the game once via r2modman so BepInEx creates it,' -ForegroundColor Yellow
    Write-Host '   then re-run this script.' -ForegroundColor Yellow
    exit 0
}

Write-Host ''
Write-Host "==> Tailing $logPath" -ForegroundColor Cyan
Write-Host "    filter: lines containing [StormGuide], duration: $Seconds s" -ForegroundColor Cyan
Write-Host '    (start the game in r2modman now if it isn''t running)'
Write-Host ''

# Get-Content -Wait blocks indefinitely; we wrap it in a runspace and stop
# after $Seconds. Using -Tail 0 means we only see lines written from now on.
$job = Start-Job -ScriptBlock {
    param($path)
    Get-Content -Path $path -Tail 0 -Wait |
        Where-Object { $_ -match '\[StormGuide\]|StormGuide' }
} -ArgumentList $logPath

try {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        Receive-Job -Job $job | ForEach-Object { Write-Host $_ }
        Start-Sleep -Milliseconds 250
    }
}
finally {
    Stop-Job -Job $job -ErrorAction SilentlyContinue | Out-Null
    Receive-Job -Job $job | ForEach-Object { Write-Host $_ }
    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue | Out-Null
}

Write-Host ''
Write-Host '==> Smoke-run done.' -ForegroundColor Cyan
