#!/usr/bin/env pwsh
<#
    .SYNOPSIS
    Checks everything needed to build, test and run Underworld Empire, and says how to fix
    whatever is missing.

    .DESCRIPTION
    Run this first on a new machine. It never changes anything: it only reports.

    Exit code is the number of BLOCKING problems, so CI can gate on it. Optional components
    (Android, Chrome) are reported but do not count as blocking, because the API, the test
    suites and the web build are all fine without them.
#>
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$blocking = 0
$optional = 0

function Ok      ($m, $d) { Write-Host "  [ok]   $m" -ForegroundColor Green; if ($d) { Write-Host "         $d" -ForegroundColor DarkGray } }
function Bad     ($m, $d) { Write-Host "  [MISS] $m" -ForegroundColor Red;   if ($d) { Write-Host "         $d" -ForegroundColor Yellow }; $script:blocking++ }
function Warn    ($m, $d) { Write-Host "  [warn] $m" -ForegroundColor Yellow; if ($d) { Write-Host "         $d" -ForegroundColor DarkGray }; $script:optional++ }

Write-Host "`nUnderworld Empire - environment check`n" -ForegroundColor Cyan

# --- SDKs -----------------------------------------------------------------
Write-Host "SDKs"
. "$PSScriptRoot/_toolchain.ps1"

$dotnetRoot = Find-DotnetRoot
if ($dotnetRoot) {
    $v = & (Join-Path $dotnetRoot 'dotnet.exe') --version 2>$null
    Ok ".NET SDK $v" $dotnetRoot
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath -and (Split-Path -Parent $onPath.Source) -ne $dotnetRoot) {
        Warn "'dotnet' on PATH is a different install" "PATH gives $((& dotnet --version 2>`$null)); scripts/ resolve the right one automatically, so this is harmless unless you call dotnet by hand."
    }
} else {
    Bad ".NET SDK 10+ not found" "Install: https://dotnet.microsoft.com/download/dotnet/10.0"
}

$flutterBin = Find-FlutterBin
if ($flutterBin) {
    $fv = (& (Join-Path $flutterBin 'flutter.bat') --version 2>$null | Select-Object -First 1)
    Ok "Flutter" "$flutterBin - $fv"
} else {
    Bad "Flutter SDK not found" "Install: https://docs.flutter.dev/get-started/install/windows"
}

# --- Database -------------------------------------------------------------
Write-Host "`nDatabase"
$docker = Get-Command docker -ErrorAction SilentlyContinue
if (-not $docker) {
    Bad "Docker not found" "Needed for Postgres. Install Docker Desktop, or point ConnectionStrings__Default at your own Postgres."
} else {
    Ok "Docker" ((& docker --version 2>$null))
    $pg = & docker ps --filter "name=underworld-postgres" --format "{{.Names}} {{.Status}}" 2>$null
    if ($pg) {
        Ok "Postgres container up" $pg
        # The API and the tests use separate databases on the same server.
        $dbs = & docker exec underworld-postgres psql -U underworld -tAc "select datname from pg_database where datname in ('underworld','underworld_test')" 2>$null
        if ($dbs -match 'underworld') { Ok "databases present" (($dbs | Where-Object { $_ }) -join ', ') }
        else { Warn "databases not created yet" "They are created on first run / first test pass." }

        # Connectivity is NOT schema. An empty database answers GET /health/db with 200
        # "reachable" and then 500s the first real request with
        #     42P01: relation "Players" does not exist
        # which reads as an application bug and is not one. Check the schema explicitly so that
        # failure is diagnosed here instead of from a stack trace.
        # NB: queried via information_schema with single-quoted string literals only. The
        # obvious to_regclass('public."Players"') form does NOT survive: PowerShell strips
        # double quotes when handing arguments to a native executable, so psql receives an
        # unquoted identifier, folds it to lowercase, and reports the table missing on a
        # database that has it. The identifier below is a string literal, not an identifier,
        # so it needs no double quotes and cannot be mangled.
        $hasPlayers = & docker exec underworld-postgres psql -U underworld -d underworld -tAc "select count(*) from information_schema.tables where table_schema='public' and table_name='Players'" 2>$null
        if (($hasPlayers | Where-Object { $_ }) -join '' -match '1') {
            # Identifier quotes here DO need escaping for the same reason; \" survives.
            $applied = & docker exec underworld-postgres psql -U underworld -d underworld -tAc 'select count(*) from \"__EFMigrationsHistory\"' 2>$null
            Ok "schema created" "$((($applied | Where-Object { $_ }) -join '').Trim()) migration(s) applied"
        } else {
            Bad "schema not created - the database is empty" "Run:  ./scripts/migrate.ps1     (compose gives you a database, not a schema)"
        }
    } else {
        Bad "Postgres container not running" "Start it:  docker compose up -d postgres"
    }
}

# --- Optional targets -----------------------------------------------------
Write-Host "`nOptional build targets"
if ($flutterBin) {
    $doctor = & (Join-Path $flutterBin 'flutter.bat') doctor 2>&1 | Out-String
    if ($doctor -match 'Android toolchain[^\r\n]*\r?\n?.*?\[.\]' -and $doctor -notmatch '\[.\] Android toolchain') {
        Ok "Android toolchain"
    } elseif ($doctor -match 'Unable to locate Android SDK|\[X\] Android toolchain') {
        Warn "Android SDK not installed" "Only needed for 'flutter run -d <emulator>' and APK builds. Web and all tests work without it."
    } else {
        Ok "Android toolchain"
    }
    if ($doctor -match '\[X\] Chrome|Cannot find Chrome') {
        Warn "Chrome not found" "Needed for 'flutter run -d chrome'. 'flutter build web' still works."
    } else {
        Ok "Chrome (for web runs)"
    }
}

# --- Repo state -----------------------------------------------------------
Write-Host "`nRepository"
if (Test-Path (Join-Path $root 'content/crime/assets/logo.png')) {
    Ok "pack art present" "content/crime/assets/"
} else {
    Warn "pack art missing" "Regenerate:  python scripts/generate-pack-art.py"
}

$py = Get-Command python -ErrorAction SilentlyContinue
if ($py) { Ok "Python (for the art generator)" ((& python --version 2>&1)) }
else { Warn "Python not found" "Only needed to regenerate pack art; the committed PNGs are fine without it." }

# --- Credential hygiene ---------------------------------------------------
Write-Host "`nCredential hygiene"
$cfgFiles = @("$env:USERPROFILE\.gitconfig", (Join-Path $root '.git\config'))
$leaked = @()
foreach ($f in $cfgFiles) {
    if (Test-Path $f) {
        $c = Get-Content $f -Raw
        if ($c -match 'github_pat_|ghp_|gho_|ghs_') { $leaked += $f }
    }
}
if ($leaked.Count -gt 0) {
    Bad "an access token is stored in plaintext git config" ("Found in: " + ($leaked -join ', ') + " - revoke it and use a credential helper.")
} else {
    Ok "no tokens in git config" "remotes are clean"
}

# --- Verdict --------------------------------------------------------------
Write-Host ""
if ($blocking -eq 0) {
    Write-Host "Ready. Next:  ./scripts/migrate.ps1   then   ./scripts/test.ps1   then   ./scripts/run-api.ps1" -ForegroundColor Green
    if ($optional -gt 0) { Write-Host "($optional optional component(s) missing - see [warn] above)" -ForegroundColor DarkGray }
} else {
    Write-Host "$blocking blocking problem(s) above must be fixed first." -ForegroundColor Red
}
exit $blocking
