<#
.SYNOPSIS
    Capture a series of full-screen screenshots into tools/screenshots/ for the
    Thunderstore page. Intended to be run while StormGuide is visible in-game;
    each capture pauses N seconds so you can manually switch tabs (Ctrl+1..8)
    between shots.

.DESCRIPTION
    Uses System.Windows.Forms for the bitmap copy. Writes one PNG per
    requested shot into tools/screenshots/ (created if missing). Filenames
    are zero-padded sequence numbers prefixed with the optional -Prefix.

.PARAMETER Count
    How many shots to take. Defaults to 8 (one per tab).

.PARAMETER DelaySeconds
    Seconds between shots so you can switch tabs. Defaults to 4.

.PARAMETER Prefix
    Filename prefix. Defaults to "stormguide".

.EXAMPLE
    pwsh -File tools/Capture.ps1 -Count 9 -DelaySeconds 5 -Prefix release
#>
[CmdletBinding()]
param(
    [int]$Count = 8,
    [int]$DelaySeconds = 4,
    [string]$Prefix = "stormguide"
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dest     = Join-Path $repoRoot 'tools\screenshots'
if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest | Out-Null }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Write-Host "Capturing $Count shot(s) at $DelaySeconds s intervals into $dest" -ForegroundColor Cyan
Write-Host "  Switch tabs in StormGuide between shots (Ctrl+1..8)." -ForegroundColor Yellow
Write-Host "  Starting in 3 seconds..."
Start-Sleep -Seconds 3

for ($i = 1; $i -le $Count; $i++) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp    = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g      = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $g.Dispose()
    $name = "{0}-{1:D2}.png" -f $Prefix, $i
    $path = Join-Path $dest $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  [$i/$Count] $name" -ForegroundColor Green
    if ($i -lt $Count) { Start-Sleep -Seconds $DelaySeconds }
}

Write-Host "" 
Write-Host "==> Done. $Count shot(s) saved to $dest" -ForegroundColor Cyan
