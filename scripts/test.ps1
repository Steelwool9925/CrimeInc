#!/usr/bin/env pwsh
# Build + run all test suites (.NET + Flutter). Exits non-zero on any failure.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Write-Host '== dotnet build ==' -ForegroundColor Cyan
dotnet build "$root/UnderworldEmpire.slnx"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '== dotnet test ==' -ForegroundColor Cyan
dotnet test "$root/UnderworldEmpire.slnx" --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '== flutter test ==' -ForegroundColor Cyan
Push-Location "$root/app"
try { flutter test } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'All test suites passed.' -ForegroundColor Green
