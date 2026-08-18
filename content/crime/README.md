# `crime` content pack

The MVP theme pack. Populated from M1 onward with the data-driven content schema:

- `pack.json` — theme name, currency labels, palette, asset map ✅ *live since M1*
- `jobs.json` — job catalog ✅ *live since M2*
- `properties.json` — property catalog (M3) — not yet present
- `items.json` — item catalog (M3) — not yet present
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
