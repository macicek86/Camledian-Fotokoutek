#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the Windows Photobooth app.

.DESCRIPTION
    Windows-only: the WPF app can be *built* anywhere (see build.ps1) but actually running it needs
    the real Windows Desktop runtime (window, camera, printing APIs, etc).

.PARAMETER Mode
    "Development" (windowed, debug affordances) or "Kiosk" (fullscreen). See spec §20.
#>
param(
    [ValidateSet("Development", "Kiosk")]
    [string]$Mode = "Development",
    [string]$Configuration = "Debug"
)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

$isWindowsOs = ($PSVersionTable.PSVersion.Major -lt 6) -or $IsWindows
if (-not $isWindowsOs) {
    Write-Warning "Camledian.Photobooth.App is a WPF application: it only *runs* on Windows (camera, printing, and the window itself all need the Windows Desktop runtime). Use ./scripts/build.ps1 to verify it compiles here instead."
    exit 1
}

$appProject = Join-Path $repoRoot "src/Camledian.Photobooth.App/Camledian.Photobooth.App.csproj"
$env:CAMLEDIAN_Environment = $Mode

Write-Host "Running Camledian Photobooth in $Mode mode..." -ForegroundColor Cyan
dotnet run --project $appProject -c $Configuration
