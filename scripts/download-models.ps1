#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Downloads the ONNX AI models used for background removal (spec §22).

.DESCRIPTION
    Models are intentionally NOT committed to git (they are tens of MB and change independently of
    the code that uses them). Run this once after cloning, and again whenever AiSettings.ModelPath
    changes to point at a different model.

    Default model: U-2-Net "p" (portable) — a small (~4.7 MB) salient-object-segmentation network
    that works well for "cut the person out of frame" on CPU, requires no GPU, and is licensed under
    Apache 2.0 (https://github.com/xuebinqin/U-2-Net). The .onnx export used here is the one
    published by the rembg project's GitHub releases.
#>
param(
    [string]$ModelsDirectory = (Join-Path $PSScriptRoot "..\src\Camledian.Photobooth.App\data\models"),
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$models = @(
    @{
        Name = "u2netp.onnx"
        Url  = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx"
        Sha256 = "309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8"
    }
)

New-Item -ItemType Directory -Path $ModelsDirectory -Force | Out-Null
Write-Host "Models directory: $ModelsDirectory"

foreach ($model in $models) {
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
Write-Host "Done. Set AiSettings.ModelPath (Admin > AI / Hybrid) to point at the downloaded model if it lives outside the default models/ folder."
