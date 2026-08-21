#!/usr/bin/env pwsh
# Run the Flutter app. Defaults to Chrome (web); pass -d <device> to override.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Resolve the SDK rather than trusting PATH — see scripts/_toolchain.ps1.
. "$PSScriptRoot/_toolchain.ps1"
$tc = Initialize-Toolchain -RequireFlutter

Push-Location "$root/app"
try {
    if ($args.Count -eq 0) { & $tc.Flutter run -d chrome } else { & $tc.Flutter run @args }
} finally { Pop-Location }
