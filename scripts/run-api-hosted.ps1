#!/usr/bin/env pwsh
# Run the ASP.NET Core API bound to 0.0.0.0:5080 instead of localhost, so it's reachable from
# other machines on the network -- and, with a tunnel in front of it (see
# docs/hosting-a-friend.md), from the open internet.
#
# This alone does NOT expose the server to the internet -- it only stops it from being
# loopback-only. Reaching it from another machine on the same network needs this PC's LAN IP
# (printed below) and, on Windows, an inbound firewall allow rule for the port. Reaching it from
# anywhere else needs a tunnel; run one alongside this script and hand out the tunnel's HTTPS URL,
# never this script's own http:// LAN address, to anyone connecting over the internet -- see
# docs/hosting-a-friend.md for the full walkthrough and why that distinction matters now that
# account passwords are involved.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Resolve the SDK rather than trusting PATH -- see scripts/_toolchain.ps1.
. "$PSScriptRoot/_toolchain.ps1"
$tc = Initialize-Toolchain

# Best-effort LAN IP for the "give this address to a friend on your network" case. Picks the
# first non-loopback IPv4 address; on a machine with several network adapters this may not be
# the one you want -- `ipconfig` remains the source of truth if this guess is wrong.
$lanIp = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.InterfaceAlias -notmatch 'Loopback' -and $_.IPAddress -notlike '169.254.*' } |
    Select-Object -First 1 -ExpandProperty IPAddress)

Write-Host "Starting the API bound to 0.0.0.0:5080 (reachable from the network, not just this PC)." -ForegroundColor Cyan
if ($lanIp) {
    Write-Host "  Same-network devices: http://${lanIp}:5080" -ForegroundColor Cyan
}
Write-Host "  For a friend connecting over the internet: start a tunnel pointed at localhost:5080" -ForegroundColor Cyan
Write-Host "  and give them the tunnel's HTTPS URL, not the address above. See docs/hosting-a-friend.md." -ForegroundColor Cyan
Write-Host ""

$env:ASPNETCORE_ENVIRONMENT = 'Development'
& $tc.Dotnet run --project "$root/src/UnderworldEmpire.Api" --launch-profile hosted
