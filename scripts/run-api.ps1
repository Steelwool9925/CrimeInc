#!/usr/bin/env pwsh
# Run the ASP.NET Core API locally. Swagger UI at /swagger (from S0.2).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet run --project "$root/src/UnderworldEmpire.Api" @args
