# Enhancements — polish on the shipped MVP

Raw list of enhancement requests against the shipped core loop (M0–M4, `main`). Unlike
`roadmap.md` (new systems cut from MVP scope — combat, accounts, etc.), everything here is a
change to mechanics that already exist and already ship. Nothing below is sized, sequenced, or
signed off yet — this is the intake list; fixes get planned from here.

---

## E1 — Starting health is too high; start at 10

**Current:** `PlayerDefaults.HealthMax = 100` (`src/UnderworldEmpire.Domain/PlayerDefaults.cs`).
New players start with every meter full, so a new player starts at 100 health. Health
regenerates at 1 point/minute (`MeterRates.Health`).

**Requested change:** drop starting/max health to **10**.

**Touches to watch when this is planned:** M3R job-risk failure costs (0–1 health per failed
job) and the R1 combat design's health-loss ranges (`healthLossWinner` 2–6, `healthLossLoser`
6–15 in `roadmap.md`) were sized against a much bigger pool. A drop to 10 makes health a much
tighter resource and probably needs those ranges revisited whenever this lands.

---

## E2 — Health and Energy max should scale up with level

**Current:** `HealthMax` and `EnergyMax` (`PlayerDefaults.cs`) are flat constants for every
player regardless of level — only `attack`/`defense` grow, via skill points. A level 50 player
has the same meter caps as a brand-new one.

**Requested change:** both `HealthMax` and `EnergyMax` should increase as the player levels up.

**Open question for planning:** the growth formula isn't specified yet — flat amount per level,
or a curve like `Progression.LevelCurve` already uses for XP. Needs a number before
implementation.

---

## E3 — All items become consumable; Brass Knuckles has a 50/50 removal chance

**Current:** items are permanent equips. A player buys an item once (`POST /items/{id}/buy`),
then `equip`/`unequip`s it freely — there's no consumption or loss concept anywhere in the
domain model or `items.json` schema (see `content/crime/items.json`).

**Requested change:** for now, all items should be consumable rather than permanent. Brass
Knuckles specifically: a 50/50 chance of being "removed" once bought.

**Open question for planning:** this is a new mechanic, not a tuning number — needs its own
design pass before implementation:
- What removes an item — the purchase itself, each equip, or each use (e.g. each job attempt)?
- Does "consumable" mean single-use (gone after one job) or the described 50/50-roll-on-buy
  applied to every item, with Brass Knuckles just being the first one named?
- What does the player see when an item is removed (message, inventory update, refund)?

---

## E4 — Energy regen is too slow for a first-time (~15 minute) session

**Current:** `MeterRates.Energy` = 1 point per 5 minutes, `EnergyMax = 10`. A new player can burn
through the full 10-energy bar in a few minutes of tapping through jobs, then has nothing to do
for a long stretch — a full refill from empty takes 50 minutes, well past a first 15-minute
session.

**Requested change:** speed up energy regen (or otherwise keep a first-time player supplied with
things to do) so a ~15 minute first session doesn't run dry.

**Open question for planning:** several levers could satisfy this — faster regen rate, a bigger
starting/max energy pool, or a one-time new-player grace period/buff. Needs a concrete choice.

---

## E5 — Certain missions require specific items and quantities, matched to mission type

**Current:** a job's `requirements` block (`content/crime/jobs.json`) is stat-only —
`attack`/`defense` thresholds checked against `Loadout.TotalAttack`/`TotalDefense`
(`JobService.Attempt`). Nothing checks item ownership, and jobs have no "type" tag today (only
`area`, e.g. `downtown`/`docks`/`uptown`).

**Requested change:** some jobs should also require holding a specific item, and a specific
count of it, matched to what kind of job it is (e.g. a car-theft job wants a vehicle item; an
armed job wants a weapon).

**Open questions for planning:**
- What defines "mission type" for matching — a new content field per job, or inferred from
  existing item `slot` (weapon/armor/vehicle)?
- Is the item **spent** on attempt (a quantity consumed) or just **required to be held/equipped**
  (a gate, like the stat requirements)? This depends directly on E3 — if items become
  consumable, "a specific number of items" reads as a per-attempt consumption count; if they
  stay permanent, it reads as an ownership/equip gate.

---

## E6 — Job success chance always starts at a 35% base, improved by mastery, skills, items, and level

**Current:** each job sets its own `baseSuccessChance` in `jobs.json` — ranges from 55% (*Rob an
Armored Transport*) to 75% (*Boost a Parked Car*) across the six MVP jobs — and only moves from
there via `successChancePerSurplusPoint × (effective stat surplus)` (`Job.SuccessChanceFor`).
"Effective stat" is `Loadout.TotalAttack`/`TotalDefense`: base stat (raised by skill points) plus
equipped gear, so skills and items already move the odds today, just blended into one number.
**Mastery rank is tracked** (`PlayerJobMastery`) **but never feeds into the odds formula at
all**, and **level isn't a direct input either** — it only gates which jobs are visible/attemptable.

**Requested change:** every job's base success chance becomes a flat **35%**, then mastery,
skills, items, and level each move it up from there.

**Open questions for planning:**
- How much weight does each factor get (mastery rank, level, and stat-surplus — which today
  conflates skills and items into one number)? Do skills and items need separating so each is
  a distinct, callable-out contribution, per the request?
- Does this replace `successChancePerSurplusPoint` or add new terms alongside it?
- Does the existing 95% `risk.maxSuccessChance` ceiling stay as-is?
- This is a balance pass across every job in `jobs.json`, not a single constant — closer in
  shape to the T3R.2 risk pass than to E1/E2/E4.

---

## E7 — Items must improve the player only once

**Current:** `Loadout` already dedupes per equip slot — only one weapon/armor/vehicle
contributes at a time (`Loadout.For` throws if two items occupy the same slot) — and buying
multiple copies of the same item just raises `PlayerItem.Quantity`; extra copies confer no extra
stat bonus today. So the case this guards against doesn't fully exist yet under the current
permanent-item model.

**Requested change:** whatever an item's stat improvement is, it must only ever apply to the
player once.

**Note:** this reads as a constraint *on* E3 (items becoming consumable), not a standalone
mechanic — once items can be bought repeatedly / re-triggered, this is the rule that stops a
repurchased or reapplied item from stacking its bonus. Plan it together with E3 rather than
separately.

---

## Cross-cutting note

E1, E2, and E4 are balance-number changes to existing constants. E3, E5, E6, and E7 are new or
substantially redesigned mechanics — item consumption, item-gated job requirements, a reworked
odds formula, and a one-time-improvement rule — and are tangled together: **E5's consumption
question, E7's stacking rule, and E3's trigger point are one design conversation**, and **E6
touches every job in the content pack**, not a single number. These four need a design pass
before sizing, not just a number to change.
