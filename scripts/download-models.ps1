#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Downloads the ONNX AI models used for background removal (spec §22/§24/§25).

.DESCRIPTION
    Models are intentionally NOT committed to git (they are tens/hundreds of MB and change
    independently of the code that uses them). Run this once after cloning, and again whenever
    AiSettings.PreviewModelPath / FinalModelPath change to point at different models.

    Two models are downloaded, matching the app's two-quality-tier design (spec §24/§25 — preview
    can't wait, final quality can):

      - u2netp.onnx (~4.7 MB) — small/fast "portable" U-2-Net variant, used for the live preview
        loop. Good enough for a person standing close to the camera; weaker on fine detail (hair,
        motion blur) than the full model.
      - u2net.onnx (~176 MB) — the full U-2-Net, used once after capture for the final render.
        Same 320x320 input as the "p" variant, just a deeper/more accurate network.

    Both are licensed Apache 2.0 (https://github.com/xuebinqin/U-2-Net); the .onnx exports used here
    are the ones published on the rembg project's GitHub releases.
#>
param(
    [string]$ModelsDirectory = (Join-Path $PSScriptRoot "..\src\Camledian.Photobooth.App\data\models"),
    [switch]$Force,
    [switch]$SkipFinalModel
)

$ErrorActionPreference = "Stop"

$models = @(
    @{
        Name = "u2netp.onnx"
        Url  = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx"
        Sha256 = "309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8"
        Required = $true
    },
    @{
        Name = "u2net.onnx"
        Url  = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2net.onnx"
        Sha256 = "8d10d2f3bb75ae3b6d527c77944fc5e7dcd94b29809d47a739a7a728a912b491"
        Required = $false
    }
)

New-Item -ItemType Directory -Path $ModelsDirectory -Force | Out-Null
Write-Host "Models directory: $ModelsDirectory"

foreach ($model in $models) {
    if ($SkipFinalModel -and -not $model.Required) {
        Write-Host "Skipping $($model.Name) (-SkipFinalModel) — the app falls back to the preview model for final renders too."
        continue
    }

    $destination = Join-Path $ModelsDirectory $model.Name

    if ((Test-Path $destination) -and -not $Force) {
        Write-Host "✓ $($model.Name) already present, skipping (use -Force to re-download)."
        continue
    }

    Write-Host "Downloading $($model.Name) ..."
    Invoke-WebRequest -Uri $model.Url -OutFile $destination -UseBasicParsing

    $actualHash = (Get-FileHash -Path $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedHash = $model.Sha256.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        Remove-Item $destination -Force
        throw "Hash mismatch for $($model.Name): expected $expectedHash, got $actualHash. Aborting — file removed."
    }

    Write-Host "✓ $($model.Name) downloaded and verified ($($actualHash))."
}

Write-Host ""
Write-Host "Done. AiSettings.PreviewModelPath / FinalModelPath (Admin > AI / Hybrid) already point at data/models/u2netp.onnx and data/models/u2net.onnx by default."
Write-Host "The app resolves those against the built exe's folder, so rebuild (or ./scripts/run.ps1) once — the csproj copies data/models/ into the output directory."
