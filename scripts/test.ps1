#!/usr/bin/env pwsh
# Build + run all test suites (.NET + Flutter). Exits non-zero on any failure.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Resolve the SDKs rather than trusting PATH — see scripts/_toolchain.ps1.
. "$PSScriptRoot/_toolchain.ps1"
$tc = Initialize-Toolchain -RequireFlutter

Write-Host '== dotnet build ==' -ForegroundColor Cyan
& $tc.Dotnet build "$root/UnderworldEmpire.slnx"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '== dotnet test ==' -ForegroundColor Cyan
& $tc.Dotnet test "$root/UnderworldEmpire.slnx" --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '== flutter test ==' -ForegroundColor Cyan
Push-Location "$root/app"
try { & $tc.Flutter test } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'All test suites passed.' -ForegroundColor Green
