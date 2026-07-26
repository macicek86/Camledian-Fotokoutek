#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the .NET test suite and the Cloudflare Worker test suite.
#>
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "== Running .NET tests ==" -ForegroundColor Cyan
dotnet test (Join-Path $repoRoot "tests/Camledian.Photobooth.Tests/Camledian.Photobooth.Tests.csproj")

Write-Host ""
Write-Host "== Running Cloudflare Worker tests ==" -ForegroundColor Cyan
Push-Location (Join-Path $repoRoot "cloud")
try {
    npm test
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "All tests passed." -ForegroundColor Green
