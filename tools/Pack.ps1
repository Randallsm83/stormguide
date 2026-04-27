<#
.SYNOPSIS
    Build StormGuide and produce a Thunderstore-shaped zip suitable for upload
    to the AgainstTheStorm community: manifest.json + icon.png + the plugin
    DLL inside a `plugins/StormGuide/` folder.

.DESCRIPTION
    The output zip is written to `tools/dist/StormGuide-<version>.zip`. If no
    icon.png is present in the repo, a tiny placeholder is generated in-memory
    so the layout still validates.

.PARAMETER Version
    Override the manifest's `version_number`. Defaults to the csproj version.
#>
[CmdletBinding()]
param(
    [string]$Version = ""
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$proj     = Join-Path $repoRoot 'StormGuide\StormGuide.csproj'
$dist     = Join-Path $repoRoot 'tools\dist'

# Hint dotnet via scoop's shim if it isn't already on PATH.
$dotnetShim = Join-Path $env:USERPROFILE 'scoop\apps\dotnet-sdk\current'
if (Test-Path $dotnetShim) { $env:Path = "$dotnetShim;$env:Path" }

Write-Host "==> Building $proj" -ForegroundColor Cyan
dotnet build $proj -c Release -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Pull version from csproj if not overridden.
if (-not $Version) {
    [xml]$csproj = Get-Content $proj
    $Version = ($csproj.Project.PropertyGroup.Version | Select-Object -First 1)
}
if (-not $Version) { $Version = '0.0.1' }

$dll = Join-Path $repoRoot 'StormGuide\bin\Release\StormGuide.dll'
if (-not (Test-Path $dll)) { throw "DLL not found: $dll" }

# Stage layout: <staging>/manifest.json, icon.png, plugins/StormGuide/StormGuide.dll
$staging = Join-Path $env:TEMP "StormGuide-pack-$(Get-Random)"
New-Item -ItemType Directory -Path $staging | Out-Null
$pluginDir = Join-Path $staging 'plugins\StormGuide'
New-Item -ItemType Directory -Path $pluginDir | Out-Null
Copy-Item $dll $pluginDir

$manifest = [ordered]@{
    name           = 'StormGuide'
    version_number = $Version
    website_url    = 'https://github.com/example/stormguide'
    description    = 'Decision-time game guide overlay for Against the Storm.'
    dependencies   = @('BepInEx-BepInExPack-5.4.2100')
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $staging 'manifest.json') -Encoding UTF8

# icon.png: copy if present, otherwise emit a 1x1 transparent placeholder.
$icon = Join-Path $repoRoot 'tools\icon.png'
if (Test-Path $icon) {
    Copy-Item $icon (Join-Path $staging 'icon.png')
} else {
    $stub = [byte[]]@(
        0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
        0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
        0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
        0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,
        0x89,0x00,0x00,0x00,0x0D,0x49,0x44,0x41,
        0x54,0x08,0x99,0x63,0x00,0x01,0x00,0x00,
        0x05,0x00,0x01,0x0D,0x0A,0x2D,0xB4,0x00,
        0x00,0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,
        0x42,0x60,0x82
    )
    [IO.File]::WriteAllBytes((Join-Path $staging 'icon.png'), $stub)
}

# Compose the zip.
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
$out = Join-Path $dist "StormGuide-$Version.zip"
if (Test-Path $out) { Remove-Item $out -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $out -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force

Write-Host ""
Write-Host "==> Wrote $out" -ForegroundColor Cyan
Write-Host "    Upload to thunderstore.io/c/against-the-storm/" -ForegroundColor Cyan
