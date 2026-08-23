#!/usr/bin/env pwsh
# Apply EF Core migrations to the local database, creating the schema.
#
# Why this exists: `docker compose up` gives you a *database*, not a *schema*. Nothing applies
# migrations at startup — Program.cs deliberately does not call Migrate(), so a running host
# never silently rewrites a database it was pointed at. That means a fresh clone, or a recreated
# Docker volume, leaves you with an empty database and this on the first real request:
#
#     42P01: relation "Players" does not exist        (surfaces as a 500 on POST /session)
#
# Confusingly, GET /health/db still reports 200 "reachable" in that state, because it only
# checks connectivity. Run this script and the 500 goes away.
#
# Pass extra args straight through to `dotnet ef database update`
# (e.g. `./scripts/migrate.ps1 --connection "..."`, or a target migration name to roll back to).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Resolve the SDK rather than trusting PATH — see scripts/_toolchain.ps1.
. "$PSScriptRoot/_toolchain.ps1"
$tc = Initialize-Toolchain

Push-Location $root
try {
    # dotnet-ef is a local tool pinned in .config/dotnet-tools.json, so a fresh clone needs a
    # restore before `dotnet ef` resolves. Restoring every time is cheap once it is cached.
    & $tc.Dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed ($LASTEXITCODE)." }

    & $tc.Dotnet ef database update `
        --project "$root/src/UnderworldEmpire.Infrastructure" `
        --startup-project "$root/src/UnderworldEmpire.Api" `
        @args
    if ($LASTEXITCODE -ne 0) {
        throw @"
Migration failed ($LASTEXITCODE).

Most common cause: Postgres is not running, or is not the container this project expects.
The compose service publishes on host port 5433 (NOT the default 5432) precisely so it does not
collide with a natively-installed PostgreSQL. Check with:

    docker compose ps
    docker compose up -d --wait

If a native PostgreSQL is listening on 5432, that is fine and unrelated — this project does not
use it. A "role \"underworld\" does not exist" error means something resolved to that native
server instead of the container.
"@
    }

    Write-Host ""
    Write-Host "Schema is up to date." -ForegroundColor Green
} finally { Pop-Location }
