#!/usr/bin/env pwsh
<#
    .SYNOPSIS
    Installs the Android SDK pieces needed to build and run this app on Android.

    .DESCRIPTION
    Android Studio is NOT required — this installs the standalone command-line tools and only
    the packages the project actually needs, which is a few hundred MB rather than several GB.

    Idempotent: re-running skips anything already installed. Everything lands under one
    directory (default %LOCALAPPDATA%\Android\Sdk), so undoing it is deleting that folder and
    the ANDROID_HOME variable.

    .PARAMETER WithEmulator
    Also install the emulator and a system image so `flutter run -d emulator` works. This is a
    much larger download (~1.5 GB) and is unnecessary if you deploy to a physical device.

    .PARAMETER SdkRoot
    Override the install location.

    .EXAMPLE
    ./scripts/setup-android.ps1
    ./scripts/setup-android.ps1 -WithEmulator
#>
param(
    [switch]$WithEmulator,
    [string]$SdkRoot = (Join-Path $env:LOCALAPPDATA 'Android\Sdk')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is dramatically slower with a progress bar
$root = Split-Path -Parent $PSScriptRoot

# Versions the project needs. Flutter's own defaults (FlutterExtension.kt) are compileSdk 36 /
# targetSdk 36 / minSdk 24, so the platform and build-tools track 36.
$AndroidApi   = 36
$BuildTools   = '36.0.0'
$CmdlineTools = 'https://dl.google.com/android/repository/commandlinetools-win-13114758_latest.zip'

function Step($m) { Write-Host "`n== $m" -ForegroundColor Cyan }
function Ok  ($m) { Write-Host "   $m" -ForegroundColor Green }
function Note($m) { Write-Host "   $m" -ForegroundColor DarkGray }

Write-Host "Android setup for Underworld Empire" -ForegroundColor Cyan
Note "SDK root: $SdkRoot"

# --- 1. JDK ---------------------------------------------------------------
Step "Java"
$javaHome = $env:JAVA_HOME
if (-not $javaHome) { $javaHome = [Environment]::GetEnvironmentVariable('JAVA_HOME','Machine') }
if (-not $javaHome) { $javaHome = [Environment]::GetEnvironmentVariable('JAVA_HOME','User') }
if (-not $javaHome -or -not (Test-Path (Join-Path $javaHome 'bin\java.exe'))) {
    throw @"
No JDK found (JAVA_HOME is unset or wrong).

The Android SDK tools and Gradle both need one. Install Temurin 17 or 21 from
https://adoptium.net/ and set JAVA_HOME to its folder, then re-run.
"@
}
$env:JAVA_HOME = $javaHome
Ok "JAVA_HOME = $javaHome"

# --- 2. command-line tools ------------------------------------------------
Step "Command-line tools"
$latest = Join-Path $SdkRoot 'cmdline-tools\latest'
$sdkmanager = Join-Path $latest 'bin\sdkmanager.bat'

if (Test-Path $sdkmanager) {
    Ok "already installed"
} else {
    $zip = Join-Path $env:TEMP 'android-cmdline-tools.zip'
    if ((Test-Path $zip) -and ((Get-Item $zip).Length -gt 50MB)) {
        Note "reusing cached download"
    } else {
        Note "downloading (~136 MB)..."
        Invoke-WebRequest -Uri $CmdlineTools -OutFile $zip -UseBasicParsing -TimeoutSec 1800
    }

    # Guard against a proxy or captive portal handing back an HTML error page.
    $sig = [System.IO.File]::ReadAllBytes($zip)[0..3]
    if (($sig -join ',') -ne '80,75,3,4') {
        Remove-Item $zip -Force
        throw "Downloaded file is not a zip. Check your network/proxy and re-run."
    }

    $staging = Join-Path $env:TEMP ('android-cmdline-' + [guid]::NewGuid().ToString('N'))
    Expand-Archive -Path $zip -DestinationPath $staging -Force

    # The archive unpacks to `cmdline-tools/...`, but sdkmanager insists on living at
    # `<sdk>/cmdline-tools/latest/`. Without this move it fails with a confusing
    # "Could not determine SDK root" and no hint about the layout.
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $latest) | Out-Null
    Move-Item -Path (Join-Path $staging 'cmdline-tools') -Destination $latest -Force
    Remove-Item $staging -Recurse -Force -EA SilentlyContinue

    if (-not (Test-Path $sdkmanager)) { throw "sdkmanager not found at $sdkmanager after extract" }
    Ok "installed to $latest"
}

# --- 3. licences ----------------------------------------------------------
# BEFORE installing, not after. sdkmanager silently skips every package whose licence is
# unaccepted and still exits 0, so installing first produces a confident success message and an
# empty SDK. This ordering is the whole reason the step exists here.
#
# NOTE: this accepts Google's Android SDK licence agreements on your behalf — the same
# agreements `flutter doctor --android-licenses` shows interactively. Running this script
# constitutes accepting them.
Step "Licences"
$answers = Join-Path $env:TEMP 'android-sdk-accept.txt'
# One 'y' per LINE. A single long "yyyy..." string answers only the first prompt and then hits
# EOF, and sdkmanager reports "7 of 7 licenses not accepted" while still exiting 0.
Set-Content -Path $answers -Value (1..200 | ForEach-Object { 'y' }) -Encoding ascii

# Redirected through cmd rather than a PowerShell pipe: piping into a .bat does not forward
# stdin to the Java process it launches, so the prompts never see the input at all.
cmd /c "`"$sdkmanager`" --sdk_root=`"$SdkRoot`" --licenses < `"$answers`"" 2>&1 | Out-Null
Remove-Item $answers -Force -EA SilentlyContinue

if (-not (Test-Path (Join-Path $SdkRoot 'licenses'))) {
    throw "Licence acceptance did not take. Run interactively: & '$sdkmanager' --sdk_root='$SdkRoot' --licenses"
}
Ok ("accepted (" + (Get-ChildItem (Join-Path $SdkRoot 'licenses')).Count + " licence files)")

# --- 4. SDK packages ------------------------------------------------------
Step "SDK packages"
$packages = @('platform-tools', "platforms;android-$AndroidApi", "build-tools;$BuildTools")
if ($WithEmulator) {
    $packages += @('emulator', "system-images;android-$AndroidApi;google_apis;x86_64")
    Note "including emulator + system image (large)"
}
Note ("installing: " + ($packages -join ', '))

& $sdkmanager "--sdk_root=$SdkRoot" @packages 2>&1 | Select-Object -Last 4

# Exit code is not evidence: verify the artefacts landed on disk.
Step "Checking what actually installed"
$expected = @{
    'platform-tools'              = (Join-Path $SdkRoot 'platform-tools\adb.exe')
    "platforms/android-$AndroidApi" = (Join-Path $SdkRoot "platforms\android-$AndroidApi\android.jar")
    "build-tools/$BuildTools"     = (Join-Path $SdkRoot "build-tools\$BuildTools\aapt2.exe")
}
$missing = @()
foreach ($k in $expected.Keys) {
    if (Test-Path $expected[$k]) { Ok "$k" } else { Write-Host "   MISSING $k" -ForegroundColor Red; $missing += $k }
}
if ($missing.Count -gt 0) {
    throw ("These packages did not install: " + ($missing -join ', ') + "`n" +
           "Most often an unaccepted licence. Run manually to see the prompt:`n" +
           "  & '$sdkmanager' --sdk_root='$SdkRoot' --licenses")
}

# --- 5. wire it up --------------------------------------------------------
Step "Configuration"
[Environment]::SetEnvironmentVariable('ANDROID_HOME', $SdkRoot, 'User')
$env:ANDROID_HOME = $SdkRoot
Ok "ANDROID_HOME set for your user (new terminals pick it up automatically)"

$flutter = Join-Path $env:USERPROFILE 'flutter\bin\flutter.bat'
if (-not (Test-Path $flutter)) {
    . "$PSScriptRoot/_toolchain.ps1"
    $bin = Find-FlutterBin
    if ($bin) { $flutter = Join-Path $bin 'flutter.bat' }
}
if (Test-Path $flutter) {
    & $flutter config --android-sdk $SdkRoot | Out-Null
    Ok "flutter pointed at the SDK"
} else {
    Note "flutter not found; run 'flutter config --android-sdk $SdkRoot' yourself"
}

# --- 6. verify ------------------------------------------------------------
Step "Verifying"
if (Test-Path $flutter) {
    $doctor = & $flutter doctor 2>&1 | Out-String
    $androidLine = ($doctor -split "`n" | Where-Object { $_ -match 'Android tool' } | Select-Object -First 1)
    Write-Host "   $($androidLine.Trim())"
    if ($doctor -match '\[X\] Android toolchain') {
        Write-Host "`nAndroid toolchain still reports a problem. Full output:" -ForegroundColor Yellow
        Write-Host $doctor
        exit 1
    }
}

Write-Host "`nDone. Next:" -ForegroundColor Green
Write-Host "   ./scripts/run-app.ps1 -d <device>     # list devices with: flutter devices"
Write-Host "   cd app; flutter build apk --debug     # prove the Android build works"
if (-not $WithEmulator) {
    Write-Host "`n   No emulator was installed. Use a physical device with USB debugging, or re-run" -ForegroundColor DarkGray
    Write-Host "   with -WithEmulator to add one (~1.5 GB)." -ForegroundColor DarkGray
}
