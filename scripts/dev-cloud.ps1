#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the Cloudflare Worker backend locally (D1 + R2 emulated, via wrangler dev --local).

.DESCRIPTION
    Applies local D1 migrations and starts `wrangler dev`. The API is then reachable at
    http://localhost:8787 — landing page at "/", API under "/api/photobooth/...", and the admin UI
    (pairing/stats/photo gallery/accounts) at "/admin/login". First run: create an account with
      curl -X POST "http://localhost:8787/admin/setup?key=$env:ADMIN_API_KEY" `
        -H "content-type: application/json" -d '{"username":"admin","password":"..."}'
    (ADMIN_API_KEY comes from cloud/.dev.vars; password needs 8+ chars).
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
