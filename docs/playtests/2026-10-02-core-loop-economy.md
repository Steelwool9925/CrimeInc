# Core-loop evaluation — first session, return value & progression, 2026-10-02

**Method:** a throwaway simulator driving the **real** `UnderworldEmpire.Domain` assembly —
`JobService.Attempt`, `Job.SuccessChanceFor`, `LevelCurve`, `Meter` regeneration, `Loadout`,
`PropertyService` — against the real `content/crime` pack. Nothing is re-implemented, so no number
here can drift from what the server actually does. Only the *player* is modelled, in two
temperaments drawn from the September playtest: a **tapper** who taps the cheapest legal job and
never shops, and a **min-maxer** who buys Brass Knuckles the moment the cash is there. 400–2,000
seeded runs per first-session figure, 25 per long-run scenario.

Companion to [the 2026-09-12 enjoyment & retention batch](2026-09-12-enjoyment-retention.md), which
observed one session and found the moment-to-moment loop solid with every problem sitting *around*
it. This quantifies that layer.

---

## Verdict

The tap-to-resolve loop is genuinely fine. Around it are three gates a free-to-play game has to
pass — **get them hooked, get them back, give them somewhere to go** — and all three are currently
closed.

| Gate | Measured | |
|---|---|---|
| **1. The first session hooks them** | **1%** of players reach level 2 in session one | ✗ |
| **2. Coming back tomorrow is worth something** | A daily player's session is **2.1 taps**; 29 of their first 40 days hold no reward | ✗ |
| **3. There is somewhere to go** | **98.4%** of the XP curve unlocks nothing | ✗ |

Each is a small number of specific, fixable things rather than a design that needs rethinking. The
good news is in §7: the parts that work are the hard parts.

---

## Gate 1 — The first session never pays out

A new player starts with 10 energy, 0 cash, and one legal job. Level 2 costs 100 XP.

| | Tapper | Min-maxer |
|---|---|---|
| Taps in the entire first session | 10.0 | 8.4 |
| XP when the bar runs dry | **68.6** of 100 | **82.5** of 100 |
| Cash held | 150 | 120 |
| Failed attempts | 3.6 of 10 | 2.7 of 8 |
| **Reached level 2 in session one** | **4 of 400 runs (1%)** | **28 of 400 (7%)** |

**Between 93% and 99% of new players finish their entire first session having never levelled up.**
They never see a level-up, never get a skill point, never see the energy refill, never unlock a job.
The game's whole reward vocabulary fires zero times. What they experience is: tap, sometimes fail,
bar empties, nothing happens.

It is worse than the average suggests, because the opening is also where the variance bites:

- **35% of players have their very first action in the game fail.** Base success is 65%.
- Average worst losing streak in the first bar: 1.9. **Worst seen across 400 runs: six in a row.**
- The September playtest's patient explorer hit three straight failures on their opening taps. That
  was not bad luck worth discounting — it is a ~4% event, which at any real install volume is a lot
  of first impressions.

And the session is tiny. Ten taps is roughly **20 seconds of input**, followed by 15 minutes of
nothing — a play-to-wait ratio of about **1:45**.

### The one hook that does work is hidden

The min-maxer bought Brass Knuckles in **396 of 400 runs** and unlocked a second job inside session
one. That is a real, satisfying beat, it fires early, and it is entirely intentional design.

But reaching it requires finding the Market — a dashboard tile *below* Crew, several screens down a
scrolling Home — and then the equip control, an unlabeled icon button in the Market's app bar that
opens an "Inventory" overlay. Per the September batch, two of three personas never found it.

**So the designed hook (level-up) does not fire, and the hook that does fire is behind the game's
two worst discoverability problems.** That is the whole first-session problem in one sentence.

### A documentation error is the proximate cause

`LevelCurve.cs` explains why the curve was not retuned after jobs gained the ability to fail:

> one bar of *Shake Down a Corner Store* expects roughly 91 XP rather than exactly 100 — level 2
> usually takes an eleventh run once energy ticks back. The curve was left alone rather than
> retuned to chase the old guarantee.

Measured, that figure is **68.6 XP, not 91**, and level 2 takes about **15 runs, not 11**. The
comment assumes roughly one failure per bar; at 65% base success there are 3.5. The explicit
decision to leave the curve alone rests on a number that is a third too high. Correct the number
and the decision probably reverses.

### What actually fixes it — measured, not guessed

**Lever 1 — a bigger opening energy grant** (curve and odds untouched):

| Opening energy | Tapper levels in session 1 | Min-maxer |
|---|---|---|
| 10 (shipped) | 1% | 8% |
| 15 | 58% | 83% |
| 18 | 87% | 96% |
| **20** | **98%** | **99%** |
| 25 | 100% | 100% |

**Lever 2 — a cheaper first level** (10 energy, odds untouched). One opening bar yields a median of
73 XP for the tapper (10th percentile 55, 90th 82):

| Level-2 threshold | Tapper reaches it | Min-maxer |
|---|---|---|
| 100 (shipped) | 1% | 8% |
| 80 | 26% | 43% |
| 70 | 52% | 60% |
| **60** | **75%** | **80%** |
| 50 | 91% | 89% |

**Lever 3 — a kinder starter job** (10 energy, level 2 at 100):

| Starter success | First tap fails | Tapper levels in session 1 |
|---|---|---|
| 65% (shipped) | 35% | 1% |
| 75% | 25% | 6% |
| 85% | 15% | 20% |
| 95% | 5% | 61% |

**The combination worth shipping — 20 opening energy and the starter job at 80%:**

> **100%** of both temperaments level up in session one, after 12.5 taps (tapper) or 8.8
> (min-maxer), with first-tap failure down from 35% to 20%.

One implementation note, because it is not free: `Meter.GrantAt` caps at `Max`, so a one-time
welcome grant above the bar is not expressible today — the surgical version needs a bonus-energy
concept. Raising `PlayerDefaults.EnergyMax` to 20 instead is a one-line change, and it is cleaner
than it first looks. `Meter.CurrentAt` computes `points = elapsed / regenPerPoint` and then
`min(Max, StoredValue + points)` (`src/UnderworldEmpire.Domain/Meter.cs:64`), so **`MeterRates.Energy`
is the throughput knob and `EnergyMax` is only the buffer.** Raising the bar doubles the opening
session and widens the away-storage window from 15 to 30 minutes, but leaves steady-state
energy-per-hour unchanged at 40/hr — it therefore shifts **no** duration in §4. Leave
`MeterRates.Energy` at 90s: raising it to 180s would *halve* throughput and double every figure in
Gate 3.

---

## Gate 2 — Nothing asks them to come back tomorrow

The total work to reach level 100 is **identical at every cadence**: 27,388 energy and 5,492 taps,
whether the player opens the app once a day or sixty-four times a day. Only the wall-clock changes.

| Cadence | Level 8 — last job | Level 12 — all content | Level 100 |
|---|---|---|---|
| Once a day | day 20 | **day 40** | day 2,651 |
| Twice a day | day 10 | day 20 | day 1,326 |
| 3×/day | day 6.5 | day 13 | day 884 |
| Every 15 min, 16h/day | 4h 45m | 10h | day 41 |

**That is the whole problem in one table.** The game rewards *frequency within a day* and is
completely indifferent to *consistency across days*. A player who logs in sixty-four times on Monday
and never again beats a player who logs in faithfully every day for two months. There is no
mechanism anywhere that distinguishes a 30-day streak from a single long sitting.

### What a daily player's session actually is

Energy caps at 10, which is 15 minutes of regeneration. A once-a-day player therefore captures
**10 energy per day** — out of the 960/day the regen rate could supply to someone checking in on
cadence. They harvest about **1%** of the game's output.

And because the best job costs 9 of a 10-point bar, that 10 energy buys almost nothing:

| | Taps per daily session |
|---|---|
| Day 1 (1-energy jobs) | ~10 |
| Day 20 onward (9-energy job + mop-up) | **2.1** |

**The daily session shrinks fivefold as the player progresses.** By week three their entire daily
experience of the game is two taps and about four seconds. That is the habit the game is asking
them to form.

### The reward calendar is mostly empty

Level-ups for a once-a-day player land on days **1, 2, 5, 8, 11, 15, 20, 26, 31, 35, 40** — and then
never again in any meaningful sense (level 20 on day 101, level 50 on day 643).

| Window | Days with any reward |
|---|---|
| First 7 days (D7 retention) | **3 of 7** |
| First 30 days (D30 retention) | **8 of 30** |
| First 40 days | 11 of 40 — **29 empty days** |
| After day 40 | nothing new, ever |

On every one of those empty days, the session is two taps and nothing happens.

### All four daily-retention engines are missing

A daily habit is built by one of four things. The codebase has none of them:

| Engine | Status |
|---|---|
| Daily reset reward — login bonus, streak | **No code anywhere.** No `daily`, `streak`, `consecutive` or `bonus` machinery exists. |
| Daily objectives / quests | **None.** |
| Social obligation — crew, guild, PvP defence | **Crew and Territory route to `ComingSoonScreen`.** No server-side code at all. |
| Appointment mechanics — timed jobs, overnight builds | **None.** Nothing in the game takes longer than one request. |

Energy regeneration is the only time-based pull the game has, and **it is a within-day mechanic
wearing a retention mechanic's clothes**. Capping at 15 minutes means it can only ever ask the
player to come back in 15 minutes. It is structurally incapable of asking them to come back
tomorrow.

### Why the one daily-shaped mechanic does not land

Property accrual *is* the right shape — long caps reward long absences. But:

| Property | Level | Caps after | Day a once-daily player can buy it |
|---|---|---|---|
| Street Corner | 1 | **15 min** | day 1 |
| Night Club | 6 | 8 h | day 11 |
| Riverside Casino | 12 | 24 h | **day 40** |

The only property a new player can own caps at **15 minutes**, so a daily player collects the same
60 cash whether they were away an hour or a week. The first property that actually banks a full day
is the Casino — on day 40, the same day they run out of content. The mechanic arrives exactly when
there is nothing left to do with it.

And it pays in cash, which §5 shows stops being a constraint on day one.

### Two assets already built and sitting unused

- **`Player.Stamina`** is fully plumbed — entity, database column, 1 point per 5 minutes, refilled on
  level-up, projected through `/me` — and **nothing in the codebase ever spends it**. A complete
  second resource meter with no consumer.
- **`combat-r1-backup`** carries a built combat domain (`CombatService`, `FightService`,
  `BotOpponents`, `CombatPower`, with tests). PvP gated on the unused stamina meter is the standard
  daily-return mechanic for this genre, and most of it already exists off-branch.

---

## Gate 3 — There is nowhere to go

The long-run picture, carried to level 100 across three cadences.

| | Hardcore (every 15 min, 16h/day) | Casual (3 sessions/day) |
|---|---|---|
| Level 8 — last job unlocks | 4h 45m | 6d 12h |
| Level 12 — **all content seen** | **10h** | **13d 4h** |
| Level 100 | 41d | 884d |
| Share of the run with anything new in it | **1.0%** | **1.5%** |

The game ships 6 jobs, 9 items and 6 properties; the last is gated at level 12. `MaxLevel` is 100.
**98.4% of the XP curve unlocks nothing.**

### Why the pacing collapses

Level cost grows **99×** across the curve. The best available earn rate grows **2.1×**, and stops
growing at level 10.

| Level | XP for this level | Best xp/energy | × cost vs L1 | × rate vs L1 |
|---|---|---|---|---|
| 1 | 100 | 9.10 | 1.0 | 1.0 |
| 8 | 800 | 15.41 | 8.0 | 1.7 |
| 10 | 1,000 | 19.01 | 10.0 | 2.1 |
| 12 | 1,200 | 19.10 | 12.0 | 2.1 |
| 50 | 5,000 | 19.10 | 50.0 | 2.1 |
| 99 | 9,900 | 19.10 | **99.0** | **2.1** |

```
hours per level  =  level XP cost  /  (xp per energy × energy per hour)
```

`LevelCurve` grows the numerator linearly in `L`. The denominator is constant past level 10: the
last job unlocks at 8 and the last item at 10, so xp-per-energy stops improving; and energy-per-hour
is fixed at 40 by `MeterRates.Energy`, a `const` of 90 seconds that nothing in the codebase ever
varies by level. (`PlayerDefaults.EnergyMax` bounds how much a player can *bank between visits*, not
what they earn per hour — it is the buffer, not the rate.) So hours-per-level grows linearly and cumulative time grows
quadratically, against a content supply that already stopped.

### From level 8 there is exactly one correct action, forever

Once `rob-an-armored-transport` is legal it strictly dominates on both xp/energy and cash/energy,
and surplus stats push every job to the pack's 0.95 ceiling, flattening the risk differences the
content was written to express. Across a 41-day run to level 100:

> **2,728** taps on `rob-an-armored-transport` — the 9-energy job
> **2,653** taps on `shake-down-corner-store` — the level-1 job, mopping up leftover energy
> **~80** taps on everything else combined

Half of every tap in the game is the starter job used as small change, because the best job costs 9
of a 10-point bar and the remainder has exactly one home.

---

## 4. `hijack-a-delivery-truck` loses money every time you run it

The only job with an item gimmick is strictly loss-making, win or lose. It requires holding two
switchblades and `JobService.Attempt` consumes exactly one per attempt on **both** branches. A
switchblade costs 180, and the Market is the only source — no job awards items.

| | |
|---|---|
| Expected gross cash per attempt | **+131** |
| Switchblade burned per attempt | **−180** |
| **Expected net** | **−49 cash** (−12.3 per energy) |
| Expected xp/energy | 9.46 — *below* `boost-a-parked-car`, two levels beneath it |

Dominated on both axes. A player who does the arithmetic never touches it; a player who doesn't is
quietly drained. This is a balance bug, not a tuning preference.

**Full job economy, each evaluated at exactly its own stat requirement:**

| Job | Lvl | Energy | p(win) | E[xp] | xp/en | E[$] | Burn | Net $ | Net $/en |
|---|---|---|---|---|---|---|---|---|---|
| shake-down-corner-store | 1 | 1 | 65% | 6.8 | 6.85 | 15 | 0 | 15 | 15.0 |
| boost-a-parked-car | 1 | 2 | 75% | 17.2 | 8.62 | 43 | 0 | 43 | 21.7 |
| run-numbers-for-the-bookie | 2 | 2 | 72% | 19.6 | 9.78 | 62 | 0 | 62 | 30.8 |
| **hijack-a-delivery-truck** | 3 | 4 | 65% | 37.9 | 9.46 | 131 | 180 | **−49** | **−12.3** |
| torch-a-rival-warehouse | 5 | 6 | 60% | 61.0 | 10.17 | 272 | 0 | 272 | 45.3 |
| rob-an-armored-transport | 8 | 9 | 55% | 107.1 | 11.90 | 555 | 0 | 555 | 61.6 |

`xp/en` spans 6.85 to 11.90 across the whole pack; `net $/en` spans 15 to 62. The economy makes
later jobs *richer* but barely *faster*. Cash scales; XP — the thing the level curve gates — does not.

---

## 5. Cash stops being a constraint on day one

| | Cash |
|---|---|
| One of every item in the pack | 10,775 |
| One of every property in the pack | 218,300 |
| **Everything purchasable in the game** | **229,075** |
| Earned from jobs by level 100, hardcore | **2,643,045** — 11.5× everything |
| Left over at level 100, casual + reinvesting | 28,421,414 |

Two compounding causes. **Properties have no quantity cap** — `PropertyService.CheckPurchase`
validates level and cash and nothing else — and a Street Corner pays back its 500 price in 2.08
hours of on-cadence collection, so the hundredth is as cheap as the first. And **there is no sink**:
items are one-time purchases totalling 10,775, and the only consumable is the switchblade, for the
one job no rational player runs.

The Market — which the September batch rightly called a hidden gem with the best writing in the
game — is economically irrelevant after the first hour. Every decision in it collapses to "buy all
of it."

---

## 6. Mastery is a progress bar that pays nothing

`jobs.json` sets `completionsPerRank: 6` and `maxRank: 5`, so 30 completions caps a job.
`JobService` increments the rank, reports it, and **never reads it** — no payout, no success chance,
no unlock.

In every simulated run, "all six jobs at max mastery" is reached **never**. Four of the six jobs are
dominated, mastery is the only thing running them could give, and mastery gives nothing. The system
that should be the long-tail reason to revisit old content supplies no reason at all.

---

## 7. What is working — and it is the hard part

Worth protecting through any retune:

- **The level-up energy refill is a genuine accelerator.** `Player.ApplyXpAndLevelUp` refills every
  meter, roughly doubling early effective session length. It is the best-designed lever in the loop.
  It is also, right now, a lever that 99% of players never experience in their first session.
- **The gear-to-unlock play works exactly as intended.** The agent reproduces the min-maxer's
  discovery unprompted: bank 75 cash, buy Brass Knuckles, `boost-a-parked-car` becomes legal on the
  spot. Items having a purpose before combat exists is a real design win — it just needs to be
  findable.
- **Risk tuning is coherent.** Expected value rises monotonically with level across the pack (except
  §4), and the consolation fractions mean a failure is never a total loss.
- **The architecture is ready for the fixes.** `Meter` is already a lazy, time-based accumulator, so
  timed jobs and longer accrual windows are the pattern the codebase already uses rather than new
  machinery. Content is data-driven, so most of §Gate 2's fixes are numbers in `properties.json`.

The expensive things — an authoritative server, a coherent economy, a data-driven content pipeline,
a loop that feels good to tap — are built. What is missing is the scaffolding around them.

---

## Recommendations, in funnel order

### Gate 1 — make the first session pay out

1. **Ship a 20-energy opening and raise the starter job to ~80% success.** Measured: 100% of both
   temperaments level up in session one, and first-tap failure drops from 35% to 20%. `EnergyMax` is
   the buffer rather than the rate, so raising it to 20 costs nothing in long-run pacing — and
   `MeterRates.Energy` should stay at 90s.
2. **Fix the `LevelCurve.cs` comment**, which says 91 XP where the real figure is 68.6. Whatever is
   decided about the curve, the next person to read that file should not inherit the error.
3. **Surface the Market and the equip control.** The one hook that already works is behind a
   dashboard tile below the fold and an unlabeled icon. This is the highest-value UI change in the
   game and it needs no balance work at all.

### Gate 2 — give them a reason to come back tomorrow

4. **Add a daily reset reward with a streak.** This is the single highest-leverage missing feature.
   A login bonus worth a full extra energy bar *doubles* a daily player's session from 2 taps to 4,
   and a streak counter creates the loss aversion that is the strongest daily-return lever in the
   genre. There is currently no `daily` or `streak` concept anywhere in the codebase.
5. **Add daily objectives built from content you already have.** "Complete three different jobs
   today" instantly gives the four dominated jobs a reason to exist and fixes the
   mastery-pays-nothing problem in the same stroke. `PlayerJobMastery` already tracks per-job
   completions per player.
6. **Invert the accrual caps.** `street-corner`'s `capMinutes: 15` → 480 turns the first property a
   new player can buy into an overnight appointment. One number in `properties.json`, and the
   cheapest retention fix available.
7. **Add one overnight appointment** — a job you start and collect in 8–24 hours. `Meter` is
   already exactly this pattern, so this is content and an endpoint, not new machinery. It is the
   mechanic that makes "come back tomorrow" a literal instruction rather than a hope.
8. **Spend the stamina meter.** It is fully built and has no consumer. PvP against the
   `BotOpponents` already sitting on `combat-r1-backup`, gated on stamina, is the genre-standard
   daily hook and is mostly written.
9. **Build the return notification.** Nothing currently tells a player anything is waiting.

### Gate 3 — give them somewhere to go

10. **Make a level raise earning capacity, not just a number.** The earn rate is flat past level 10
   because both halves of the denominator above stop moving. The two levers that actually widen it
   are the regen rate (`MeterRates.Energy`) and xp-per-energy — a per-rank mastery multiplier, or
   content above level 10. Growing `EnergyMax` is **not** one of them: it is the buffer, so it lets a
   player bank more between visits without changing what they earn per hour. An earlier draft of this
   report had that backwards and recommended the bar; the correction is recorded here because the
   mistake is an easy one to repeat.
11. **Decide what `MaxLevel = 100` is for.** Either cut it to roughly match the content (15–20),
   flatten `XpPerLevelStep` at the top, or give every level a reward that stands alone.
12. **Make mastery pay.** Already tracked, persisted and surfaced; a per-rank multiplier would give
    the four dominated jobs a reason to exist and give the post-level-12 player something still moving.
13. **Fix `hijack-a-delivery-truck`** — switchblade to ≤ 90, consume on success only, or raise the
    cash floor above ~320.
14. **Cap property quantity or add a real sink**, so the Market stays a place where decisions happen.

Items 1 and 3 decide whether a new player has a second session at all. Items 4-6 decide whether
they have a thirtieth. Everything in Gate 3 decides whether any of that was worth doing — daily
hooks do not create content, they only raise the rate at which a player consumes it, so without
Gate 3 a perfect daily loop just walks them into the same wall on schedule.

---

**Reproducing this:** a throwaway `net10.0` console project with a `ProjectReference` to
`src/UnderworldEmpire.Domain` — `Content.cs` (maps pack JSON onto the domain records), `Sim.cs`
(long-run agent), `FirstSession.cs` (the opening bar), `WhatIf.cs` (candidate fixes and return
value), `Program.cs` (the report). Every number above re-derives in about ten seconds, which is why
it is worth keeping as `tools/loopsim` if balance tuning is going to be iterative.
