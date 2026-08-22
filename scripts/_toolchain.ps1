#!/usr/bin/env pwsh
# Locates the .NET and Flutter SDKs this project needs and puts them on PATH for the
# calling session. Dot-source it: `. "$PSScriptRoot/_toolchain.ps1"`
#
# Why this exists: `dotnet` on PATH is frequently NOT the SDK this project needs. A machine
# with .NET 8 installed system-wide and .NET 10 installed per-user resolves to the 8, which
# cannot parse the XML `.slnx` solution format and fails with
#     MSB4068: The element <Solution> is unrecognized
# That reads like a corrupt solution file but is purely SDK resolution, and it is a genuinely
# confusing hour to lose. Flutter is often not on PATH at all.
#
# Resolving here means the run scripts work on a fresh machine with no PATH surgery and no
# admin rights.

$ErrorActionPreference = 'Stop'

# The project targets net10.0 and uses the .slnx solution format, which needs SDK 10 or newer.
$script:RequiredDotnetMajor = 10

function Find-DotnetRoot {
    <#
        .SYNOPSIS
        Returns the directory of a dotnet installation carrying an SDK new enough for this
        project, or $null.
    #>
    $candidates = @()
    if ($env:DOTNET_ROOT) { $candidates += $env:DOTNET_ROOT }
    $candidates += @(
        (Join-Path $env:USERPROFILE '.dotnet'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'),
        'C:\Program Files\dotnet'
    )
    # Whatever PATH currently resolves to, considered last: it is the thing most likely to be wrong.
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += (Split-Path -Parent $onPath.Source) }

    foreach ($dir in ($candidates | Where-Object { $_ } | Select-Object -Unique)) {
        $exe = Join-Path $dir 'dotnet.exe'
        if (-not (Test-Path $exe)) { continue }
        try { $sdks = & $exe --list-sdks 2>$null } catch { continue }
        foreach ($line in $sdks) {
            # e.g. "10.0.400 [C:\Users\me\.dotnet\sdk]"
            if ($line -match '^(\d+)\.') {
                if ([int]$Matches[1] -ge $script:RequiredDotnetMajor) { return $dir }
            }
        }
    }
    return $null
}

function Find-FlutterBin {
    <#
        .SYNOPSIS
        Returns the Flutter SDK's bin directory, or $null.
    #>
    $onPath = Get-Command flutter -ErrorAction SilentlyContinue
    if ($onPath) { return (Split-Path -Parent $onPath.Source) }

    $candidates = @(
        (Join-Path $env:USERPROFILE 'flutter\bin'),
        (Join-Path $env:USERPROFILE 'dev\flutter\bin'),
        (Join-Path $env:LOCALAPPDATA 'flutter\bin'),
        'C:\src\flutter\bin',
        'C:\flutter\bin',
        'C:\tools\flutter\bin'
    )
    foreach ($dir in $candidates) {
        if (Test-Path (Join-Path $dir 'flutter.bat')) { return $dir }
    }
    return $null
}

function Initialize-Toolchain {
    <#
        .SYNOPSIS
        Puts the resolved SDKs on PATH for this session.

        .PARAMETER RequireFlutter
        Fail if Flutter is missing. Off by default so API-only scripts still run on a machine
        with no Flutter install.
    #>
    param([switch]$RequireFlutter)

    $dotnetRoot = Find-DotnetRoot
    if (-not $dotnetRoot) {
        throw @"
No .NET SDK $($script:RequiredDotnetMajor).x or newer found.

This project targets net10.0 and uses the .slnx solution format; older SDKs fail with
"MSB4068: The element <Solution> is unrecognized", which looks like a broken solution file
but is really the wrong SDK.

Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and re-run.
Checked: DOTNET_ROOT, ~/.dotnet, %LOCALAPPDATA%\Microsoft\dotnet, Program Files, and PATH.
"@
    }

    $env:DOTNET_ROOT = $dotnetRoot
    if ($env:PATH -notlike "*$dotnetRoot*") { $env:PATH = "$dotnetRoot;$env:PATH" }

    $flutterBin = Find-FlutterBin
    if ($flutterBin) {
        if ($env:PATH -notlike "*$flutterBin*") { $env:PATH = "$flutterBin;$env:PATH" }
    } elseif ($RequireFlutter) {
        throw @"
Flutter SDK not found.

Install from https://docs.flutter.dev/get-started/install/windows and re-run.
Checked: PATH, ~/flutter/bin, ~/dev/flutter/bin, %LOCALAPPDATA%\flutter\bin,
C:\src\flutter\bin, C:\flutter\bin, C:\tools\flutter\bin.
"@
    }

    # Return full executable paths, and call them explicitly rather than relying on a bare
    # `dotnet`. PowerShell caches command lookup within a session, so prepending to PATH does
    # not reliably re-resolve a name already resolved to a different install — which produces
    # exactly the MSB4068 this whole file exists to prevent.
    $flutterExe = $null
    if ($flutterBin) { $flutterExe = Join-Path $flutterBin 'flutter.bat' }

    return [pscustomobject]@{
        DotnetRoot = $dotnetRoot
        FlutterBin = $flutterBin
        Dotnet     = (Join-Path $dotnetRoot 'dotnet.exe')
        Flutter    = $flutterExe
    }
}
