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

.EXAMPLE
    pwsh tools/SmokeRun.ps1
    pwsh tools/SmokeRun.ps1 -Seconds 60
#>
[CmdletBinding()]
param(
    [int]$Seconds = 30
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$proj     = Join-Path $repoRoot 'StormGuide\StormGuide.csproj'

# Hint dotnet via scoop's shim if it isn't already on PATH.
$dotnetShim = Join-Path $env:USERPROFILE 'scoop\apps\dotnet-sdk\current'
if (Test-Path $dotnetShim) { $env:Path = "$dotnetShim;$env:Path" }

Write-Host "==> Building $proj" -ForegroundColor Cyan
dotnet build $proj -c Release -nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host '== build failed; aborting smoke-run.' -ForegroundColor Red
    exit $LASTEXITCODE
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
