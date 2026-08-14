#!/usr/bin/env pwsh
# Run the ASP.NET Core API locally on a fixed HTTP port (5080) so the Flutter app's
# default base URL matches. Swagger UI at http://localhost:5080/swagger.
# Pass extra args to override (e.g. --urls http://localhost:1234).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:ASPNETCORE_ENVIRONMENT = 'Development'
if ($args.Count -eq 0) {
    dotnet run --project "$root/src/UnderworldEmpire.Api" --urls http://localhost:5080
} else {
    dotnet run --project "$root/src/UnderworldEmpire.Api" @args
}
