#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the .NET solution and type-checks the Cloudflare Worker.

.DESCRIPTION
    The .NET build works on any OS thanks to EnableWindowsTargeting (see Directory.Build.props) —
    it fully compiles the WPF app, it just can't run it outside Windows. Full run/deploy
    verification of the Windows app happens in GitHub Actions on windows-latest.
#>
param(
    [string]$Configuration = "Debug"
)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "== Building .NET solution ($Configuration) ==" -ForegroundColor Cyan
dotnet build (Join-Path $repoRoot "Camledian.Photobooth.slnx") -c $Configuration

Write-Host ""
Write-Host "== Type-checking Cloudflare Worker ==" -ForegroundColor Cyan
Push-Location (Join-Path $repoRoot "cloud")
try {
    npm run build
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
