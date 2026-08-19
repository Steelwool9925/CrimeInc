# `crime` content pack

The MVP theme pack. Populated from M1 onward with the data-driven content schema:

- `pack.json` — theme name, currency labels, palette, asset map ✅ *live since M1*
- `jobs.json` — job catalog ✅ *live since M2*
- `items.json` — item catalog ✅ *live since M3*
- `properties.json` — property catalog (M3) — not yet present
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
```

Validation reports **every** problem at once rather than stopping at the first, so a content edit
needs one fix pass rather than one restart per error. Unknown fields are ignored, so a newer pack
stays loadable by an older engine.

### Balance notes

All numbers here are tunable without touching code. Two properties are load-bearing for the
**CP2** checkpoint and are pinned by tests in `JobCatalogLoaderTests`:

- At least one job sits at `levelRequirement: 1` with an `energyCost` within the starting energy
  maximum, so a brand-new player is never shown an entirely locked list.
- The cheapest level-1 job can be run enough times on one full starting energy bar to cross a
  mastery rank, so "watch the mastery bar tick up" is observable in a single sitting.

The current tuning makes ten runs of *Shake Down a Corner Store* — exactly one full starting energy
bar — award 100 XP and 10 completions, which is both the level-2 threshold and the first mastery
rank. That is deliberate: it makes the whole CP2 loop reachable without waiting on regen.

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

