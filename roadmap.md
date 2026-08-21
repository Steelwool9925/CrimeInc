# Roadmap — post-MVP increments for "Underworld Empire"

Everything in this file was **cut from the MVP plan** (`CrimeIncMVPPlan.md`) on 2026-08-21.
The MVP is now scoped to the core loop only: **do jobs → get cash → buy and equip gear →
level up**, plus properties as the passive-income side of that loop, and a final art/polish
milestone. Nothing here is scheduled, and nothing here is a prerequisite for any MVP
checkpoint.

Task ids (`T4.x`) and checkpoint names are kept **exactly as they were written** while this
was still milestone M4, so the design section below stays internally consistent. When any of
this is picked up it gets renumbered against whatever the plan looks like then.

---

# R1 — Combat, bounty & crew

**Status:** designed, signed off, **not built**. No `/fights`, `/opponents`, `/bounties` or
crew route exists in the repo, and nothing under `src/` or `app/lib/` mentions a fight, crew
or bounty. This starts from scratch.

**Why it was deferred:** it is the largest and riskiest block in the original plan (~213k
tokens, 6 sessions — more than jobs and items combined) and none of the core loop depends on
it. Items already earn their keep through job risk (M3R): a weapon raises job success odds,
so gear has a purpose without combat existing.

## Tasks

| # | Task | Band | ~tokens |
|---|------|------|--------|
| T4.1 | Crew domain: recruit, cap grows with level, gear/power contribution. TDD. | M | 25k |
| T4.2 | Combat power formula (attack/defense + equipped gear + crew + randomizer) as a **pure function**. Heavy TDD (deterministic seed). | L | 45k |
| T4.3 | Opponent pool: snapshots of real players + AI bot generator (guarantees targets at low population) | M | 30k |
| T4.4 | `POST /fights` attack endpoint (stamina cost, resolve, health loss, cash transfer, fight log). TDD win/lose/edge. | M | 35k |
| T4.5 | Bounty list logic + `GET /bounties` | M | 18k |
| T4.6 | Flutter: Opponents list, attack + result panel, fight log, Bounty list, Crew screen | L | 60k |

**🔎 Test Checkpoint CP-Combat:** Recruit crew → power rises. Open opponents → attack a
bot/player → stamina spent, clear win/lose result, cash transferred, your/their health
drops. Fight appears in log; beaten high-value targets show on the bounty list.

## Session plan (6 sessions, ~213k)

| Session | Contents | ~tok | How you test it |
|---|---|---|---|
| 1 | T4.2 combat power formula (pure fn) + heavy seeded tests | 45k | `dotnet test` → deterministic win/lose/edge-case combat tests green |
| 2 | T4.3 opponent pool + AI bot generator | 30k | Swagger `GET /opponents` → pool including bots |
| 3 | T4.4 attack endpoint (`POST /fights`) + tests | 35k | Swagger attack a bot → stamina spent, win/lose result, cash transfer, health↓, fight logged |
| 4 | T4.1 crew domain (recruit, cap, power), T4.5 bounty list | 43k | Swagger recruit crew → re-run fight is stronger; `GET /bounties` → beaten/high-value targets |
| 5 | T4.6a Flutter opponents list + attack + result panel + fight log | 35k | In app: open opponents, attack one, see result + fight log |
| 6 | T4.6b Flutter crew screen + bounty list | 25k | **CP-Combat**: in app recruit crew (power↑), view bounty list |

## Risk carried with this item

- **Async PvP at low population** — mitigated by the bot generator (T4.3). This risk lives
  entirely with this roadmap item; the MVP has no PvP surface.

---

## § M4 design — combat, bounty & crew (drafted and ✅ signed off 2026-08-21)

M4 arrives without the design work M3R had, and M3R is the evidence that settling it first is what
makes a milestone run cleanly: every decision below was a thing that run did **not** have to
re-litigate mid-task. This section is that same artefact for combat. **Nothing here is implemented.**
The numbered decisions are what T4.1–T4.6 execute rather than invent; the four questions at the end
are the ones I will not answer on your behalf.

### What already exists that M4 must not break

- **`Loadout` is base stat + equipped gear, and job odds are pinned to it.** M3R's whole balance —
  a 1/1 player on shake-down's 1/1 gate at exactly 0.65 — is arithmetic over that figure. Anything
  M4 adds to a player's effective stats reaches the job loop unless it is deliberately kept out.
- **Stamina exists and nothing spends it.** Max 5, one point per five minutes, untouched since M1.
  Combat is what it was reserved for.
- **Health is already a spendable meter.** Failed jobs take 0–1 from it. `Meter.SpendAt` throws on
  an overspend, so every loss must be clamped to what is left before it is applied.
- **Every action service has the same shape:** resolve content → load tracked rows → delegate the
  rules to a pure domain service → one `SaveChangesAsync` → `DbUpdateConcurrencyException` becomes
  a typed conflict, never a retry.
- **Refusals are `409` with a `reason` token and spend nothing; applied outcomes are `200` carrying
  the post-action player projection.** M3R decision 9 in force.
- **RNG is one method — `long NextInclusive(long, long)` — and roll order is a contract**, because
  `FakeRandomSource.Sequence(...)` scripts against it. Negative bounds are legal.

### The decisions

1. **Crew feeds combat power only. It never touches job odds.** Crew is the obvious thing to fold
   into `Loadout`, and folding it in would silently re-tune every job in the pack: a player with
   three crew would walk through stat gates M3R balanced against gear, and shake-down's 0.65 floor
   would drift upward the moment they recruited. So crew is a **separate addend applied inside the
   combat formula**, and `Loadout` keeps meaning exactly what it means today. The cost is that
   "your attack" now has two values depending on what is asking — the same wart A.5.1 already
   records for Home vs Inventory, widened. Revisit as one decision, not by accretion.

2. **A fight mutates both sides.** CP4 asks the tester to watch *their* health drop, so the defender
   cannot be a read-only snapshot. A fight loads both player rows tracked, applies the attacker's
   stamina spend, both sides' health loss and the cash transfer, and persists all of it in one
   `SaveChangesAsync`. Both rows carry `xmin`, so either side losing a race is the ordinary `409`.
   The defender is never asked for consent and is never notified — that is what "async PvP" means
   here, and it is why decisions 8 and 9 bound what an offline player can lose.

3. **The power formula, and why it never divides.**
   ```
   attackPower  = Loadout(attacker).TotalAttack  + crewAttack(attacker)
   defensePower = Loadout(defender).TotalDefense + crewDefense(defender)

   swingA = NextInclusive(-swingBasisPoints, +swingBasisPoints)
   swingD = NextInclusive(-swingBasisPoints, +swingBasisPoints)

   attacker wins  iff  attackPower × (10_000 + swingA)  >  defensePower × (10_000 + swingD)
   ```
   The two scaled products are compared **directly, without dividing back down**. A formula written
   the natural way — `power × (1 + swing)`, floored to an int — is *deterministic* at the powers
   this game actually starts at: ±15% of an attack of 1 rounds to zero, so every early fight would
   resolve identically and the randomizer T4.2 exists to provide would not appear until double
   digits. Comparing the scaled products keeps every point of precision at every scale, in integer
   arithmetic, with no floating point anywhere in the win/lose decision.

   **Ties go to the defender.** Equal products means the attack did not beat the defense.

   **Attack is measured against defense, not against a symmetric power score.** The defender's own
   attack is irrelevant to a fight they did not start. A counter-attack model where both sides
   contribute both stats is a legitimate alternative and is explicitly post-MVP.

4. **Roll order is a contract, and every conditional roll goes last.** M3R learned this the
   expensive way: a roll skipped on one branch shifts every later roll in the stream and silently
   invalidates a scripted test. The order is:
   1. attacker swing · 2. defender swing · 3. winner's health loss · 4. loser's health loss ·
   5. **bot cash roll — only when the target is a bot**, and last precisely so branches 1–4 script
   identically whoever the target is.

5. **Bots have no database rows. The id is the entire opponent.** A bot id like
   `bot:3:brick-mahoney` carries an index and a name seed and nothing else — **its stats are derived
   from the attacker's level at fight time, not from anything in the id.** That is the anti-tamper
   property: a client that forges a bot id cannot conjure a weak target, because the id contributes
   no numbers. No bot table, no snapshot store, no generator state to keep in sync with the ladder,
   and the pool is guaranteed non-empty at any population — which is the whole reason T4.3 exists.

6. **The opponent list shows a strength band, not a win percentage.** M3R decision 10 said report
   the real odds and compute them once; combat reads the same way but arrives at the opposite
   answer, so the difference is worth stating. A job's odds are a property of the *job* and cannot
   be shopped — every player sees the same number and the honest figure helps them choose. A
   defender's odds are a property of *that defender right now*, and publishing them turns the
   opponent list into a search for the weakest player online. So `GET /opponents` returns
   `weaker | even | stronger`, computed from the **same** `CombatPower` function with the swing
   omitted. One implementation, two presentations — the thing decision 10 was actually protecting.

7. **Gates spend nothing; precedence is level → stamina → health → target.** Four new reason tokens
   on the existing 409 vocabulary: `"level"` (below the pack's combat minimum — reuses the job
   token deliberately, so a client handles it with one code path), `"stamina"`, `"health"` (you are
   too hurt to fight), `"target"` (unknown id, yourself, outside the level band, or a defender who
   is already below the health floor). Most permanent first, as in `JobService.Attempt`.

   The last of those is not politeness: without it, a player found at 3 health is farmable forever
   by anyone who can beat them, and the health meter's one-point-per-minute regen makes that a
   twelve-hour sentence.

8. **A win takes a capped fraction of the loser's cash; a bot pays a rolled range instead.** Against
   a real player, `min(cashStealFraction × loser.Cash, cashStealCap)` moves from loser to winner —
   a fraction so a broke player is not worth hunting, a cap so a whale is not emptied by one lucky
   tap. Against a bot there is no loser to take from, so content supplies an inclusive range and the
   cash is minted. Bots are therefore mildly inflationary by construction; that is accepted for MVP
   and named here so it is not discovered later as a bug.

9. **Health floors at zero on both sides. Nobody is killed.** The rolled loss is clamped to the
   side's current health before `SpendAt` sees it — the M3R rule, now applied twice per fight. There
   is no death state, no hospital timer, no revive cost in the MVP; a player at 0 health simply
   cannot fight (decision 7) until the meter ticks back.

10. **A fight can level you up, and health loss is applied first.** Straight reuse of
    `Player.ApplyXpAndLevelUp`, and of M3R decision 7's ordering rule: a level-up refills every
    meter, so applying the health loss afterwards would silently undo it. Both sides gain XP — the
    winner `xpPerWin`, the loser `xpPerLoss` — which means **an offline defender can level up while
    being attacked**. That is intended and slightly odd, so it is written down rather than left to
    be found.

11. **`POST /fights/{targetId}`, not `POST /fights` with a body.** A deviation from the plan's
    shorthand, and the only one in M4. Every existing action route puts the subject in the path and
    carries no body at all (`/jobs/{id}/do`, `/items/{id}/buy`, `/properties/{id}/collect`); the
    reason is that a request body is a surface a client can put numbers in, and this API has
    deliberately never had one. `GET /fights` remains the log.

12. **A write deadlock is a conflict, not a fault.** A fight is the first two-row write in the
    codebase, so it is the first place where two concurrent requests can grab the same pair of rows
    in opposite orders — A attacks B while B attacks A. Postgres resolves that by killing one
    transaction with SQLSTATE `40P01`. `WriteConflicts` grows an `IsDeadlock` test alongside
    `IsUniqueViolation`, and the loser gets the same `409` as any other lost race rather than a 500.

13. **One row per fight, read from both sides.** A `Fight` row records attacker, defender (a player
    id **or** a bot id, never both), who won, cash moved, health lost by each side, XP, and the
    instant. `GET /fights` returns the most recent 20 in which the caller was *either* participant,
    which is how a defender discovers they were robbed while offline. Indexed on
    `(AttackerId, ResolvedAtUtc)` and `(DefenderId, ResolvedAtUtc)`. No pruning in MVP; the table
    grows unbounded and that is a named post-MVP item, not an oversight.

14. **Bounties are derived from the fight log, not stored.** A bounty entity would need placement,
    funding, expiry and settlement — four rules and a migration for a list CP4 only asks to *see*.
    Instead `GET /bounties` is a read over `Fights`: players who have **won** a fight in the last 24
    hours, ordered by a score of `wins × level`, excluding the caller. Hunting one is an ordinary
    fight; beating a listed target pays a content-tuned prize on top of the normal transfer.
    The trade-off is that this is a *heat list*, not the player-placed bounty of the genre — see the
    open questions.

15. **Two migrations: `AddPlayerCrew`, then `AddFights`.** `PlayerCrew` keys on
    `(PlayerId, CrewMemberId)` and `Fight` on a surrogate `Id` — the first fight-shaped table with no
    natural key, since a player can fight the same target repeatedly within a second. Both cascade
    from `Player`. Content ids stay `varchar(64)` non-FK columns, as with every catalog before them.

### Proposed content schema

Two new files, following the existing convention that a catalog file carries its own pack-wide rules
block (as `jobs.json` carries `mastery` and `risk`).

`content/crime/crew.json`
```jsonc
{
  "packId": "crime", "version": 1,
  "cap": { "base": 2, "perLevel": 1 },        // slots = base + perLevel × (level − 1)
  "members": [
    { "id": "lookout", "name": "Lookout", "levelRequirement": 1,
      "price": 150, "stats": { "attack": 2, "defense": 3 } }
    // …
  ]
}
```

`content/crime/combat.json`
```jsonc
{
  "packId": "crime", "version": 1,
  "rules": {
    "minLevel": 2,                 // combat opens after the first job grind
    "staminaCost": 1,
    "minHealthToFight": 10,        // gate for BOTH sides — see decision 7
    "swingFraction": 0.15,         // ±15%, applied as basis points, never divided
    "opponentLevelBand": 2,        // real targets within ±2 levels
    "opponentPoolSize": 8,
    "cashStealFraction": 0.10,
    "cashStealCap": 5000,
    "healthLossWinner": { "min": 2, "max": 6 },
    "healthLossLoser":  { "min": 6, "max": 15 },
    "xpPerWin": 20,
    "xpPerLoss": 5
  },
  "bots": {
    "count": 4,                    // blended into every pool, after real targets
    "levelOffsets": [-1, 0, 0, 1], // relative to the ATTACKER's level (decision 5)
    "attackPerLevel": 3.0,
    "defensePerLevel": 2.5,
    "cash": { "min": 40, "max": 120, "perLevel": 25 },
    "names": ["Brick Mahoney", "Sal the Hook", "Wire Kowalski", "Dutch Vega"]
  },
  "bounty": { "windowHours": 24, "listSize": 10, "prizePerLevel": 150 }
}
```

Every block is **nullable in the loader models** so an omitted block is a named startup error rather
than a silent read of zeros — M3R decision 8, which is now the house rule for content.

### Proposed starting balance, and the property to preserve

Numbers are a **balance pass inside T4.2/T4.3**, exactly as T3R.2 was for job risk, not something to
freeze here. What the pass must hold true:

> **A level-appropriate bot is a coin flip, tipped slightly to the player** — around 55–60% at
> parity — and **a fight is worth roughly one job's cash but costs a scarcer meter.** Stamina is 5
> deep and refills at one per five minutes; energy is 10 deep at the same rate. Combat must not
> out-earn the job loop per unit of *time*, or the jobs M2/M3R built stop being the game.

Reference points it has to sit against: a level-2 player is attack ~2 base, ~6 with Brass Knuckles,
plus ~2–5 from one or two crew; *Shake Down* pays 15–30 cash for 1 energy; *Boost a Parked Car* pays
40–75 for 2. So a win against a level-2 bot paying 40–120 + 25/level is in the right postcode and
will need a pass, not a guess.

### Task shape

| # | Session | Touches | Ends green at |
|---|---|---|---|
| T4.2 | S4.1 | `Domain/Combat/`: `CombatPower`, `FightOutcome`, `FightFailure`, `CombatRules`; pure, no I/O | `dotnet test` — scripted-swing win/lose/tie/clamp cases |
| T4.3 | S4.2 | `Content/CombatCatalog*`, `BotFactory`, `GET /opponents` | Swagger: a pool that is never empty, bands correct |
| T4.4 | S4.3 | `Fight` entity + `AddFights`, `FightActionService`, `POST /fights/{targetId}`, `GET /fights` | Swagger: attack a bot, then a real second player |
| T4.1 | S4.4a | `Crew`/`PlayerCrew` + `AddPlayerCrew`, `crew.json`, `GET /crew`, `POST /crew/{id}/recruit` | Swagger: recruit → the same fight resolves stronger |
| T4.5 | S4.4b | `GET /bounties` over the fight log | Swagger: win a fight, appear on another player's list |
| T4.6 | S4.5–4.6 | Flutter: Opponents, fight result panel, fight log, Crew, Bounties | `flutter test` + CP4 |

**T4.2 runs before T4.1 even though the formula consumes crew.** The plan's own session order does
this, and it is right: `CombatPower` takes crew attack/defense as **arguments**, so it is written and
fully tested against scripted numbers before any crew entity exists, and T4.1 simply starts passing
real ones. Same seam as `JobService` taking mastery thresholds rather than a catalog.

### ✅ RESOLVED 2026-08-21 — all four confirmed as recommended

The user signed off on every recommendation below without amendment, so **decisions 1–15 above are
binding on T4.1–T4.6**. The four questions and their answers, kept because the reasoning is what a
later reader will want:

| # | Question | Answer |
|---|---|---|
| 1 | Offline defender loses health and cash? | **Yes** — decision 2 stands; PvP is real, not cosmetic |
| 2 | Bounties derived or player-placed? | **Derived** heat list for MVP; player-placed is post-MVP |
| 3 | Combat opens at level 2? | **Yes** — `minLevel: 2` in `combat.json` |
| 4 | Crew combat-only? | **Yes** — crew never reaches `Loadout`, so no job re-balance |

<details>
<summary>The questions as they were put</summary>


1. **Does an offline defender really lose health and cash?** Decision 2 says yes, because CP4 asks
   the tester to watch it. The alternative — the defender is a pure snapshot and only the attacker's
   state changes — is gentler and much simpler (one row, no deadlock case, no decision 12), but it
   makes PvP cosmetic. *Recommend: yes, as designed.*
2. **Bounties: derived heat list, or player-placed?** Decision 14 builds the derived list — no
   entity, no migration, ~18k of work. Player-placed bounties (you pay cash to put a price on
   someone who beat you, the hunter collects) is the genre-faithful version and is roughly a session
   on its own. *Recommend: derived for MVP, player-placed as a post-MVP increment.*
3. **Combat opens at level 2. Right gate?** It means a brand-new player must clear roughly one
   energy bar of jobs first, which keeps the tutorial ordering clean and gives the ±2 level band
   something to work with. Level 1 would open PvP immediately at 1/1 stats. *Recommend: 2.*
4. **Crew is combat-only (decision 1). Confirm?** Letting crew feed `Loadout` would make it matter
   everywhere and is closer to the genre, but it silently re-tunes every job gate M3R balanced and
   would need a jobs re-balance in the same run. *Recommend: combat-only, revisit in M5.*

</details>

### Execution mode for M4

Subagents are **permitted and capped at Haiku 4.5**, effort up to `max` (user instruction,
2026-08-21). That collapses the skill's Haiku → Sonnet → Sonnet → controller ladder into
**Haiku → Haiku at max effort → controller**: there is no Sonnet tier, so every task the skill would
class as *judgment* is implemented by the controller directly. Mechanical work — content JSON,
DTOs, endpoint plumbing, screens that mirror an existing one — goes to Haiku. The controller
verifies **every** diff either way; a subagent never grades its own work.


---

# R2 — Other deferred scope

These were already marked post-MVP in the plan's locked decisions and are recorded here so
there is one place to look for "what comes after the core loop".

| # | Item | Notes |
|---|------|------|
| R2.1 | **Real accounts + IAP** | MVP ships anonymous guest identity only (device token → player row). Account linking and working in-app purchases are pipelined behind it. |
| R2.2 | **Live chat, guilds, wars** | Explicitly excluded from v1 scope during brainstorming. All three assume a player-vs-player surface, so they sit behind **R1**. |
| R2.3 | **A second, fantasy-themed app** | The content-pack architecture is retained so the same engine can drive a fantasy title. That is its own application, not a pack shipped inside this one. |
| R2.4 | **Unity client** | Flutter was chosen for the MVP (one codebase → Android + Web). Unity is revisited post-MVP if the renderer becomes the constraint. |
| R2.5 | **Property ownership cap** | Raised as A.4.3: a player may buy unlimited units of any property, so late-game income is bounded only by cash. Crime Inc itself capped counts. A balance decision to take before any economy-wide rebalance. |
| R2.6 | **Where the combat stats live on screen** | A.5.1: Home shows *base* attack, Inventory shows *effective*. The cleanest fix — move combat stats off Home entirely — only makes sense once **R1** gives them a home. Until then Home stays as it is. |
