# `crime` content pack

The MVP theme pack. Populated from M1 onward with the data-driven content schema:

- `pack.json` — theme name, currency labels, palette, asset map ✅ *live since M1*
- `jobs.json` — job catalog ✅ *live since M2*
- `items.json` — item catalog ✅ *live since M3*
- `properties.json` — property catalog ✅ *live since M3*
- `strings.json` — localizable UI text — not yet present

The engine references abstract ids; this pack supplies concrete content + art paths. Swapping
assets or text here must **not** require code changes.

## `jobs.json`

Loaded and validated at **startup** by `ContentPackLoader.LoadJobs`, alongside `pack.json`. An
invalid catalog stops the host rather than surfacing as a 500 on first use, so a content mistake
fails loudly and immediately. **Editing this file requires an API restart to take effect.**

```
packId   must equal the pack folder name ("crime")
version  schema version of this file
mastery  pack-wide mastery rules, applied to every job
  completionsPerRank  completions needed per mastery rank (>= 1)
  maxRank             rank ceiling (0 disables mastery entirely)
risk     pack-wide risk rules, applied to every job — required, not optional
  maxSuccessChance         ceiling on any job's success chance (0..1); no job is ever certain
  consolationCashFraction  fraction of the rolled cash paid on a failed attempt (0..1)
  consolationXpFraction    fraction of the job's xp granted on a failed attempt (0..1)
jobs[]   the catalog, in display order — the engine does not sort
  id                content id, unique within the pack; the route value for POST /jobs/{id}/do
  name              player-facing name
  description       player-facing flavour text
  area              presentational grouping key only ("downtown", "docks", "uptown")
  energyCost        energy consumed per attempt (>= 1; a job is never free)
  levelRequirement  minimum player level (>= 1)
  payout
    cashMin/cashMax inclusive cash range, rolled server-side per attempt
    xp              fixed experience per completion (not rolled)
  requirements      minimum EFFECTIVE stats to attempt — base plus equipped gear
    attack          minimum effective attack (>= 0; 0 means ungated)
    defense         minimum effective defense (>= 0)
  risk              per-job risk tuning — required, not optional
    baseSuccessChance             chance for a player exactly at the requirements (0..1)
    successChancePerSurplusPoint  added per point of surplus stat (>= 0)
    healthLossMin/healthLossMax   inclusive health lost on a failure, rolled per attempt
```

**`risk` and `requirements` are required blocks.** A job missing either is a named load error, not a
job that quietly reads as zeros — zeros would mean "ungated and certain", which is precisely the
mistake a content author would not notice. This is the one place the pack is stricter than the
domain: `Job` itself defaults to certain-and-ungated so hand-built definitions in tests cannot fall
into the failure path by accident.

The success chance for a given player is:

```
surplus = max(0, effectiveAttack  - requirements.attack)
        + max(0, effectiveDefense - requirements.defense)

chance  = baseSuccessChance + successChancePerSurplusPoint * surplus
          clamped to [baseSuccessChance, min(1, risk.maxSuccessChance)]
```

Each stat's surplus floors at **zero independently**. Summed algebraically, a large attack overshoot
would cancel a defense shortfall and the defense requirement would stop meaning anything.

Validation reports **every** problem at once rather than stopping at the first, so a content edit
needs one fix pass rather than one restart per error. Unknown fields are ignored, so a newer pack
stays loadable by an older engine.

### Balance notes

All numbers here are tunable without touching code. Two properties are load-bearing for the
**CP2** checkpoint and are pinned by tests in `JobCatalogLoaderTests`:

- At least one job sits at `levelRequirement: 1` with an `energyCost` within the starting energy
  maximum, so a brand-new player is never shown an entirely locked list.
- The cheapest level-1 job can be run enough times on one full starting energy bar to cross a
  mastery rank *even allowing for failures*, so "watch the mastery bar tick up" is observable in a
  single sitting.

One full starting energy bar buys **ten** runs of *Shake Down a Corner Store*. Since M3R those runs
can fail, so what a bar yields is now a distribution rather than a guarantee:

| | before M3R | M3R | since the 1/1 start |
|---|---|---|---|
| starting stats | 5 / 5 | 5 / 5 | **1 / 1** |
| starting cash | 100 | 100 | **0** |
| first job success | always | 0.90 | **0.65** |
| completions in one bar | exactly 10 | ~9 expected | ~6.5 expected |
| mastery rank at | 10 completions | 8 completions | **6** completions |
| XP in one bar | exactly 100 | ~91 expected | ~68 expected |

`completionsPerRank` has dropped twice, each time to keep the first mastery rank reachable in one
sitting. M3R took it 10 → 8 when attempts became failable; the 1/1 start took it 8 → 6, because ten
attempts at 0.65 expect 6.5 completions.

**Reaching level 2 in one bar is not guaranteed** and was not preserved. Level 2 costs 100 XP
(`LevelCurve`) and a bar now expects ~68, so it takes roughly two bars. That is the intended cost of
a harder opening; the mastery half of CP2 is the half that was protected, because it is the half the
checkpoint asks the tester to *watch*.

**A new player owns nothing.** Starting cash is 0, so the first *Brass Knuckles* (75) is roughly one
energy bar of successful work away — about 97 cash expected at the job's 15 floor. `ItemCatalogLoaderTests`
pins that reachability, so a later price rise cannot quietly put the first item out of reach of a
first sitting.

### Risk balance

| job | lvl | req atk/def | base | per surplus pt | health loss |
|---|---|---|---|---|---|
| shake-down-corner-store | 1 | 1 / 1 | 0.65 | 0.010 | 0–1 |
| boost-a-parked-car | 1 | 5 / 1 | 0.75 | 0.010 | 0–3 |
| run-numbers-for-the-bookie | 2 | 6 / 5 | 0.72 | 0.010 | 0–4 |
| hijack-a-delivery-truck | 3 | 9 / 7 | 0.65 | 0.008 | 2–8 |
| torch-a-rival-warehouse | 5 | 14 / 10 | 0.60 | 0.006 | 4–14 |
| rob-an-armored-transport | 8 | 22 / 16 | 0.55 | 0.005 | 8–25 |

Tuned against a fresh player at **attack 1 / defense 1**, where a skill point is worth +1:

- *Shake Down* is the **only job a new player can attempt**. They sit exactly on its 1/1 requirement,
  so there is no surplus and the chance is the job's floor: **0.65**.
- *Boost a Parked Car* is the same level but wants 5 attack, so it refuses with `"stats"` — the
  second job is an aspiration, not an alternative. Every other job is gated on level as well.
  Its defense requirement is **1, not 3**, so that a weapon alone can open it: Brass Knuckles add
  attack and no defense, and a 3-defense gate would have made the first unlock impossible without
  also buying armour at 120.
- *Run Numbers* (level 2, 6 attack) is the next unlock and needs both a level and some gear.

**Only the first job is open at the start, and it is deliberately not a sure thing.** Every route out
of that opening — a skill point, a weapon, a level — visibly moves either the odds on *Shake Down* or
the gate on something better.

The **CP3R demo** is unchanged in shape: *Brass Knuckles* (level 1, 75 cash, +4 attack) takes a
starting player from 1 attack to 5 and opens *Boost a Parked Car*. That is a job unlocked by
equipping rather than levelling, and it is why M3R was scheduled after M3 — the requirement reads
*effective* stats, so it depends on the `Loadout` aggregation T3.1 introduced.

Health loss scales with the job's ambition: the level-1 jobs can cost a point or three, while a
failed armored-transport robbery costs 8–25 of a 100-point bar. The loss is clamped to current
health, so it floors at 0 rather than throwing — `Meter.SpendAt` rejects an overspend, and must
never be handed a raw roll.

Job ids are compared **ordinally**. They are content keys, not display text, so casing is
significant and `Boost-A-Parked-Car` is a different id from `boost-a-parked-car`.

## `items.json`

Loaded and validated at **startup** by `ContentPackLoader.LoadItems`, on the same terms as
`jobs.json`: every problem is reported at once, an invalid catalog stops the host, and **editing
this file requires an API restart to take effect.**

```
packId   must equal the pack folder name ("crime")
version  schema version of this file
items[]  the catalog, in display order — the engine does not sort
  id                content id, unique within the pack; the route value and the inventory key.
                    Max 64 characters — it is stored as varchar(64) in PlayerItems.
  name              player-facing name
  description       player-facing flavour text
  slot              which loadout slot it fills: weapon | armor | vehicle
  levelRequirement  minimum player level to buy *or* equip (>= 1)
  price             cash cost of one copy (>= 0), fixed — never rolled
  stats
    attack          attack added while equipped (>= 0)
    defense         defense added while equipped (>= 0)
```

`slot` is the one field here that is **not** a content key: it names a value in the engine's
`ItemSlot` enum, so it is matched case-insensitively (`weapon` and `Weapon` are the same slot) and
a name outside the list above is a startup error rather than a silent default. Item **ids**, like
job ids, are compared ordinally.

Stat bonuses must be non-negative. A negative bonus would make the loadout total non-monotonic —
equipping more gear could quietly weaken a player — so a debuff item would need its own concept
rather than a negative number here.

### Balance notes

A player may equip **one item per slot**, so the catalog needs at least one item in every slot or
the inventory UI renders a dead section. Prices are absolute cash, to be read against
`jobs.json` payouts and `PlayerDefaults.StartingCash` (100).

Two properties are load-bearing for the **CP3** checkpoint and are pinned by tests in
`ItemCatalogLoaderTests`:

- Some `levelRequirement: 1` item costs no more than the starting cash **and** grants a stat
  bonus, so "buy + equip a weapon, watch Attack rise" is reachable without grinding first. The
  current tuning makes that *Brass Knuckles* (75 cash, +4 attack) against a starting 100.
- No item is free, so filling a slot always means engaging with the economy.

## `properties.json`

Loaded and validated at **startup** by `ContentPackLoader.LoadProperties`, on the same terms as
`jobs.json` and `items.json`: every problem is reported at once, an invalid catalog stops the host,
and **editing this file requires an API restart to take effect.**

```
packId       must equal the pack folder name ("crime")
version      schema version of this file
properties[] the catalog, in display order — the engine does not sort
  id                content id, unique within the pack; the route value and the holdings key.
                    Max 64 characters — it is stored as varchar(64) in PlayerProperties.
  name              player-facing name
  description       player-facing flavour text
  levelRequirement  minimum player level to buy (>= 1)
  price             cash cost of one unit (>= 0), fixed — never rolled
  income
    perHour         cash one unit earns per hour of elapsed time (1 .. 1,000,000)
    capMinutes      minutes of income one unit holds before accrual stops (1 .. 43,200 = 30 days)
```

Income is **accrued lazily**, exactly as the resource meters regenerate: nothing runs on a timer, and
what a property has earned is derived from elapsed time whenever somebody asks. Buying another unit
raises both the rate and the ceiling, so owning more never shortens how long you may stay away.

`capMinutes` is the reason to come back. Past that window a property stops earning, so one left alone
for a week pays exactly what one left alone for its window pays. It is expressed in **minutes** rather
than hours so an early-game property can have a window short enough to watch fill.

`perHour` and `capMinutes` are each validated on their own **and as a product**: cap cash is
`perHour × capMinutes ÷ 60` floored to whole cash, so `30/hour` over `1 minute` is half a coin and is
rejected at startup. Both fields are individually legal there — only the product catches it, and a
property that charges for a purchase and then never pays is worse than a failed boot.

Both also carry **upper** bounds, which exist for the same reason the lower ones do. A month is the
longest window for which "come back before it fills" still means anything, and a million an hour is
some 280× the top property here — so the only thing the ceilings reject is a typo. They also keep the
`perHour × capMinutes` product away from wrapping a 64-bit integer: unbounded, `2^62` an hour over
nine minutes comes back round as a *positive* number, which sails past the floors-to-zero guard and
loads as a property with an absurd cap.

Property **ids**, like job and item ids, are compared **ordinally**.

### Balance notes

Prices are absolute cash, to be read against `jobs.json` payouts. Properties are the game's second
income source: a job pays now for energy spent, a property pays slowly for cash spent up front.

Two properties are load-bearing for the **CP3** checkpoint and are pinned by tests in
`PropertyCatalogLoaderTests`:

- The cheapest `levelRequirement: 1` property costs no more than roughly two full energy bars of the
  starting job, so "buy a property" is a short grind rather than a project. The current tuning makes
  that *Street Corner* at 500.
- That same starter earns at least a coin a minute and fills its window in no more than 30 minutes,
  so "watch income accrue to the cap, then collect" is observable inside one test session rather than
  overnight. *Street Corner* earns 4 a minute and fills in 15.

Every property is also pinned to repay its own price within 200 full collections, so none of them is
a trap rather than a choice.
