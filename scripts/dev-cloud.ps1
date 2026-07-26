#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the Cloudflare Worker backend locally (D1 + R2 emulated, via wrangler dev --local).

.DESCRIPTION
    Applies local D1 migrations and starts `wrangler dev`. The API is then reachable at
    http://localhost:8787 — landing page at "/", API under "/api/photobooth/...", the dev pairing
    UI at "/admin/pair?key=...", and the stats UI at "/admin/stats?key=..." (key = ADMIN_API_KEY
    from cloud/.dev.vars).
#>
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Push-Location (Join-Path $repoRoot "cloud")
try {
    if (-not (Test-Path ".dev.vars")) {
        Copy-Item ".dev.vars.example" ".dev.vars"
        Write-Host "Created cloud/.dev.vars from the example template."
    }

    Write-Host "Applying local D1 migrations..." -ForegroundColor Cyan
    npm run db:migrate:local

    Write-Host ""
    Write-Host "Starting Cloudflare Worker dev server on http://localhost:8787 ..." -ForegroundColor Cyan
    npm run dev
}
finally {
    Pop-Location
}
