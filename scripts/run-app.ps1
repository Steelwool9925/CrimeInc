#!/usr/bin/env pwsh
# Run the Flutter app. Defaults to Chrome (web); pass -d <device> to override.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location "$root/app"
try {
    if ($args.Count -eq 0) { flutter run -d chrome } else { flutter run @args }
} finally { Pop-Location }
