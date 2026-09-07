# CP4 core-loop smoke: job -> level -> buy/equip -> property -> collect, plus the
# manifest-driven art route. Exercises the real HTTP surface against a live host.
#
# Usage: start the API (./scripts/run-api.ps1) in one terminal, then run this in another.
$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5080'
$fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { Write-Host "  PASS  $name  $detail" }
    else { Write-Host "  FAIL  $name  $detail"; $script:fail++ }
}

# Wait for the host to come up.
$up = $false
foreach ($i in 1..40) {
    try { Invoke-RestMethod "$base/health" -TimeoutSec 2 | Out-Null; $up = $true; break }
    catch { Start-Sleep -Milliseconds 750 }
}
if (-not $up) { Write-Host "API never became healthy"; exit 1 }
Write-Host "`n=== content pack + art (T4.1) ==="

$pack = Invoke-RestMethod "$base/content/pack"
Check "pack loads" ($pack.id -eq 'crime') "id=$($pack.id) name='$($pack.name)'"
Check "palette is served" ($null -ne $pack.palette.primary) "primary=$($pack.palette.primary) accent=$($pack.palette.accent)"

foreach ($key in @('logo','icon')) {
    $r = Invoke-WebRequest "$base/content/assets/$key" -TimeoutSec 10
    $sig = ($r.Content[0..7] | ForEach-Object { $_ }) -join ','
    Check "asset '$key' serves" ($r.StatusCode -eq 200 -and $r.Headers['Content-Type'] -match 'image/png') `
        "$($r.RawContentLength) bytes, $($r.Headers['Content-Type'])"
    Check "asset '$key' is a real PNG" ($sig -eq '137,80,78,71,13,10,26,10') "signature ok"
}
try { Invoke-WebRequest "$base/content/assets/banner" -TimeoutSec 5 | Out-Null; Check "undeclared key 404s" $false "got 200" }
catch { Check "undeclared key 404s" ($_.Exception.Response.StatusCode.value__ -eq 404) "404" }

Write-Host "`n=== session + starting state ==="
$s = Invoke-RestMethod "$base/session" -Method Post -ContentType 'application/json'
$tok = $s.deviceToken
$h = @{ Authorization = "Bearer $tok" }
$me = Invoke-RestMethod "$base/me" -Headers $h
Check "guest created" ($tok.Length -gt 10) "level=$($me.level) cash=$($me.cash) atk=$($me.attack) def=$($me.defense)"
Check "starts penniless at 1/1" ($me.cash -eq 0 -and $me.attack -eq 1 -and $me.defense -eq 1) "balance pass held"
Check "meters carry regen timing" ($null -ne $me.energy.regenSecondsPerPoint) `
    "energy $($me.energy.current)/$($me.energy.max) next=$($me.energy.secondsUntilNextPoint)s"

Write-Host "`n=== jobs: grind the opener ==="
$jobs = Invoke-RestMethod "$base/jobs" -Headers $h
$open = @($jobs.jobs | Where-Object { $_.canAttempt })
Check "exactly one job open at level 1" ($open.Count -eq 1) "open='$($open[0].id)'"
$jobId = $open[0].id

$done = 0; $failed = 0; $levelUps = 0
foreach ($i in 1..40) {
    $me = Invoke-RestMethod "$base/me" -Headers $h
    if ($me.energy.current -lt 1) { break }
    $r = Invoke-RestMethod "$base/jobs/$jobId/do" -Method Post -ContentType 'application/json' -Headers $h
    if ($r.result.succeeded) { $done++ } else { $failed++ }
    if ($r.levelUp) { $levelUps++ }
}
$me = Invoke-RestMethod "$base/me" -Headers $h
Check "job attempts resolved" (($done + $failed) -gt 0) "$done succeeded, $failed failed"
Check "cash and xp moved" ($me.cash -gt 0 -and $me.xp -gt 0) "cash=$($me.cash) xp=$($me.xp) level=$($me.level)"
Check "energy was spent" ($me.energy.current -lt $me.energy.max) "energy=$($me.energy.current)/$($me.energy.max)"

Write-Host "`n=== store: buy + equip ==="
$inv = Invoke-RestMethod "$base/inventory" -Headers $h
$before = $inv.loadout.totalAttack
# Specifically the cheapest affordable WEAPON: equipping a jacket raises defense, not attack,
# so picking by price alone would assert the wrong stat and fail for the right reason.
$weapon = @($inv.items | Where-Object { $_.canBuy -and $_.slot -eq 'weapon' } | Sort-Object price | Select-Object -First 1)[0]
if ($weapon) {
    Invoke-RestMethod "$base/items/$($weapon.id)/buy" -Method Post -ContentType 'application/json' -Headers $h | Out-Null
    $eq = Invoke-RestMethod "$base/items/$($weapon.id)/equip" -Method Post -ContentType 'application/json' -Headers $h
    Check "bought and equipped '$($weapon.id)'" ($eq.loadout.totalAttack -gt $before) `
        "totalAttack $before -> $($eq.loadout.totalAttack) (base $($eq.loadout.baseAttack) + items $($eq.loadout.itemAttack))"
    Check "gear moves effective attack" ($eq.loadout.itemAttack -eq $weapon.attack) `
        "itemAttack=$($eq.loadout.itemAttack) matches the item's +$($weapon.attack)"
} else {
    Check "an affordable weapon exists" $false "cash=$($me.cash)"
}

Write-Host "`n=== properties: gating ==="
$props = Invoke-RestMethod "$base/properties" -Headers $h
$cheapest = @($props.properties | Sort-Object price | Select-Object -First 1)[0]
$affordable = @($props.properties | Where-Object { $_.canBuy })
Check "property catalog loads" ($props.properties.Count -gt 0) `
    "$($props.properties.Count) properties, cheapest '$($cheapest.id)' at $($cheapest.price)"
# One energy bar cannot reach 500, and energy regens at 90 seconds a point, so the correct
# behaviour here is a refusal naming cash. The buy/collect path itself is covered by
# PropertyEndpointTests, PropertyIncomeTests and PropertyConcurrencyTests.
$me = Invoke-RestMethod "$base/me" -Headers $h
Check "unaffordable property is gated on cash" `
    ($affordable.Count -eq 0 -and $cheapest.buyUnavailableReason -eq 'cash') `
    "cash=$($me.cash) vs price=$($cheapest.price), reason='$($cheapest.buyUnavailableReason)'"

Write-Host "`n=== result ==="
if ($fail -eq 0) { Write-Host "ALL SMOKE CHECKS PASSED" } else { Write-Host "$fail CHECK(S) FAILED" }
exit $fail
