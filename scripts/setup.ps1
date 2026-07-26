#!/usr/bin/env pwsh
<#
.SYNOPSIS
    One-shot development environment setup for Camledian Photobooth.

.DESCRIPTION
    Checks for the required tools (.NET SDK, Node.js/npm), restores the .NET solution, installs the
    Cloudflare Worker's npm dependencies, and creates cloud/.dev.vars from its example template.
    Run this once after cloning the repo.
#>
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "== Camledian Photobooth: setup ==" -ForegroundColor Cyan

function Test-CommandAvailable {
    param([string]$Name, [string]$InstallHint)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        Write-Warning "$Name not found on PATH. $InstallHint"
        return $false
    }
    return $true
}

$dotnetOk = Test-CommandAvailable -Name "dotnet" -InstallHint "Install the .NET 10 SDK: https://dotnet.microsoft.com/download"
$nodeOk = Test-CommandAvailable -Name "node" -InstallHint "Install Node.js 20+: https://nodejs.org"
$npmOk = Test-CommandAvailable -Name "npm" -InstallHint "Installed together with Node.js."

if ($dotnetOk) {
    Write-Host "dotnet: $(dotnet --version)"
    Write-Host "Restoring .NET solution..." -ForegroundColor Cyan
    dotnet restore (Join-Path $repoRoot "Camledian.Photobooth.slnx")
}

if ($nodeOk) {
    Write-Host "node: $(node --version)"
}

if ($npmOk) {
    Write-Host "Installing Cloudflare Worker dependencies..." -ForegroundColor Cyan
    Push-Location (Join-Path $repoRoot "cloud")
    try {
        npm install

        $devVarsPath = ".dev.vars"
        if (-not (Test-Path $devVarsPath)) {
            Copy-Item ".dev.vars.example" $devVarsPath
            Write-Host "Created cloud/.dev.vars from the example template — edit it before running the backend for real."
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host ""
Write-Host "Optional next steps:"
Write-Host "  - Download AI models:      ./scripts/download-models.ps1"
Write-Host "  - Build everything:        ./scripts/build.ps1"
Write-Host "  - Run tests:               ./scripts/test.ps1"
Write-Host "  - Run the Windows app:     ./scripts/run.ps1   (Windows only)"
Write-Host "  - Run the Cloud backend:   ./scripts/dev-cloud.ps1"
Write-Host ""
Write-Host "Setup complete." -ForegroundColor Green
