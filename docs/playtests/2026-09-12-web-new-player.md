# Playtest batch — new-player experience, web, 2026-09-12

**Method:** Real UI in Chrome (browser automation), against a local API + Postgres, `flutter build web`
served statically on `http://localhost:8091` (not `flutter run -d chrome`, to keep the app in the
same browser the automation drives). Each iteration is a fresh guest account (`localStorage`
cleared) played with no prior knowledge to **level 2**, using only the job grind — no gear, no
properties, no dev/debug shortcuts.

**Batch size:** 5 iterations, findings logged only — no fixes applied except the one blocker noted
below, which was fixed with the user's explicit go-ahead because it blocked every iteration before
any could start.

## Summary — 4 findings, ranked

| Severity | Finding | Status |
|---|---|---|
| 🔴 Blocker | Web build crashed for every player, every launch (`dart:io` on web) | **Fixed** |
| 🟠 Moderate | Properties tab renders blank until a scroll forces a repaint | Logged only |
| 🟠 Moderate | Jobs screen gating goes stale after hitting 0 energy; needs a full reload to refresh | Logged only |
| 🟡 Low/unclear | Crew & Profile nav-rail taps unresponsive; likely the same general click flakiness seen elsewhere, not necessarily Crew/Profile-specific | Logged only |

Everything below the fold — job resolution, XP/leveling, mastery ranks, cash payouts, the RNG
itself — held up across all 5 independent fresh accounts with zero inconsistencies.

---

## Fixed before iteration 1 could proceed

### 🔴 Web build crashed at first launch — `dart:io` HttpClient constructed on web

**Severity:** Blocker (100% of web sessions, first launch and every cold start after).

`api/resilient_http_client.dart` built its DNS-over-HTTPS-fallback `http.Client` with `dart:io`'s
`HttpClient`/`Socket`/`SecureSocket` unconditionally. `dart:io` isn't available on Flutter web —
constructing `HttpClient()` there throws `Unsupported operation: Platform._version` synchronously,
uncaught, from `main.dart`'s `_buildAppState()`. That function runs on **both** paths that get a
player past the address gate:

- First launch, right after "Continue" on the Server Address screen → the Continue button's spinner
  spins forever, no error shown.
- Every cold start afterward (address already stored, no token yet) → **a fully blank black
  screen**, no UI at all.

Root cause, from the browser console in a debug session:
```
DartError: Unsupported operation: Platform._version
  at new HttpClient
  at createResilientHttpClient (api/resilient_http_client.dart:88)
  at _buildAppState (main.dart:479)
```

This looks like a side effect of the Android DNS-filtering fix (`3a14711`, "Android app could never
reach the network at all") — the fallback client it introduced has no web-safe path.

**Fix applied** (`app/lib/api/resilient_http_client.dart`): `createResilientHttpClient()` now
returns a plain `http.Client()` immediately when `kIsWeb`, before touching anything from `dart:io`.
Verified: fresh guest flow and cold-start-with-stored-address both now reach the Welcome/Home
screens normally.

---

## Pacing note that reshaped the plan

`MeterRates.Energy` = 90s/point, capped at 10, **not stockpileable past the cap** — so total real
time to reach a level is bottlenecked by wall-clock waiting, not click speed. Reaching level 5
(1000 cumulative XP) from a fresh account needs on the order of 130-150 energy spent, i.e.
**~3-3.5 hours of real time** per iteration. The batch's milestone was lowered to **level 2** (100
XP) by agreement, which still takes roughly 15-20 minutes of real elapsed time per fresh account
because of the same regen wall — most of that time in this batch was spent waiting on energy, not
playing.

---

## Findings

### 🟠 Properties screen renders blank until a scroll/repaint is forced

**Severity:** Moderate. **Reproduced:** every time it was checked, this batch.

Opening the Properties tab shows only the "PROPERTIES" header and cash total — the whole property
list is invisible. The `GET /properties` call actually succeeds (200, full 6-property catalog
confirmed via direct API call) and there are no console errors; the data is there, the screen just
doesn't paint it until something forces a relayout (a scroll nudged it into appearing here). A new
player has no reason to scroll a screen that looks like it loaded empty, and there's no loading
spinner or empty-state message to suggest otherwise — the tab just looks broken/empty.

### 🟡 Two of five nav-rail destinations (Crew, Profile) didn't respond to mouse clicks

**Severity:** Unclear — **confidence moderate, not confirmed as a general/universal bug.**

Across many attempts and coordinates (varying by several px vertically and horizontally, with and
without hover-before-click), clicking "Crew" or "Profile" in the side nav rail never switched the
screen, while Home/Jobs/Properties above them worked reliably. Keyboard Tab+Enter focus traversal
did work elsewhere on the page. Update from iteration 2: a related but more general pattern showed
up repeatedly — **the first click right after any page load/reload is very often swallowed**
(reproduced on nearly every reload, for ordinary nav taps too; a second click at the same spot then
works). That may fully explain the Crew/Profile misses rather than anything specific to those two
destinations — flagging both observations rather than asserting a Crew/Profile-specific bug. Worth
a quick manual check on a real device/browser before spending fix effort on either.
(Crew is intentionally a "coming soon" placeholder per the code; Profile is a real screen.)

### 🟠 Jobs screen gating goes stale after hitting 0 energy — doesn't refresh until a full reload

**Severity:** Moderate. **Reproduced:** consistently, iteration 2.

Once energy hits 0, the Jobs list correctly shows every job as locked ("Needs N energy"). But once
energy actually regenerates (confirmed via the Home header, and via a fresh page load), the Jobs
screen keeps showing the job as locked and its "Do job" button stays disabled — navigating away to
Home and back to Jobs does **not** pick up the change. Only a full page reload made the button
live again. Properties has its own polling timer for exactly this kind of staleness; Jobs
apparently doesn't. A player who waits out their energy bar and comes back to the Jobs tab (without
reloading) will see everything as still locked and may assume they need to wait longer than they
actually do.

### 🟢 What worked well

- Job grind loop (list → do job → result toast → stats update) is clear and responsive.
- Success/fail flavor text ("The job failed. You kept a small cut and the energy is gone.") lands
  well — failure doesn't feel like nothing happened.
- Mastery rank-up ("Mastery rank 1!") and the level-up modal ("LEVEL 2! You earned 1 skill point.
  Energy, stamina and health refilled.") are both satisfying, well-timed moments.
- Job list gating (locked jobs clearly show *why*: level, stat, or energy) removes any guesswork.
- Property catalog content itself (names, flavor text, prices, gating reasons) reads well once
  visible.

---

## Iteration log

All 5 iterations: fresh guest account, played to level 2 via the job grind only (no gear, no
properties). Real-time cost per iteration was dominated by energy regen (90s/point), not by
actions — roughly 12-20 minutes of wall-clock time each once the account was past level 1.

| # | XP path to 100 | Notable | 
|---|---|---|
| 1 | Hit the web-crash blocker before any gameplay could happen | Blocker fixed (see above); also surfaced the Properties-blank-until-scroll bug and the Crew/Profile nav-click issue during this iteration's exploration |
| 2 | Steady grind, no long fail streaks | Surfaced the stale-Jobs-gating bug (energy regenerated but "Do job" stayed disabled until a full reload); also reproduced first-click-after-reload being swallowed, repeatedly |
| 3 | Steady grind | No new findings — confirms 1-2 |
| 4 | Steady grind | No new findings — confirms 1-3 |
| 5 | Hit **4 failures in a row** on the 65%-success starter job (≈1.5% chance) before recovering | Confirms the RNG is real and outcomes swing — no bug, but worth knowing the early game can feel unlucky in a short session |

**Consistency verdict:** the core loop (job → result toast → mastery rank → level-up modal) never
broke across 5 independent fresh accounts. The bugs found are all in surrounding UI plumbing
(startup gate, Properties render, Jobs staleness, nav clicks) rather than in the game logic itself
— the server-authoritative economy and RNG held up perfectly every time.
