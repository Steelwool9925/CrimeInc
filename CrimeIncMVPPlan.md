# Plan — "Underworld Empire": a Crime-Inc-style tap-RPG (MVP)

## Context

**Task source:** User-supplied design (not an ADO ticket). `/batman`'s ADO ticket
selection is skipped by explicit user override; the repo is empty (greenfield), so
there is no existing system to map — the domain research (Crime Inc / Mafia-Wars
mechanics) was completed during brainstorming.

**Goal:** Build the MVP of a mechanically-faithful clone of Uken Games' *Crime Inc.* —
a menu/stat-driven social crime-empire RPG — for **Android + Web**, under original
branding. Only the *mechanics* are cloned; name/art/branding are original (see IP note).

**Locked decisions (from brainstorming):**
- **Scope (v1):** playable core loop — jobs, properties, leveling, inventory, async
  fights vs other players/bots. No live chat/guilds/wars in MVP.
- **Client:** **Flutter** (one codebase → Android + Web). Unity revisited post-MVP.
- **Server:** **ASP.NET Core**, authoritative (all rules/RNG/timers server-side).
- **DB:** **PostgreSQL** (EF Core).
- **Identity (v1):** anonymous **guest** (device-token → server player row). Real
  accounts + working IAP are **pipelined** (post-MVP).
- **Content:** fully **data-driven content packs** so art/text swap without code
  changes. MVP ships **only the `crime` pack**. The data-driven architecture is retained
  so a **future, separate fantasy application** can reuse the same engine — but the
  fantasy theme is **out of MVP scope** (its own app later, not built here).

**IP note:** Game mechanics aren't copyrightable, but the *Crime Inc* name and Uken's
art/text/logos are. This build uses an original name and original/CC art.

---

## Architecture

```
Flutter app (Android + Web)      ── thin client: renders server state, sends actions
        │  HTTPS/JSON (REST)         never computes rules/money/RNG
        ▼
ASP.NET Core API (authoritative)  ── all game logic: regen, payouts, combat, economy
        │  EF Core                    RNG + timers server-side (= anti-cheat)
        ▼
PostgreSQL                        ── all persistent state, transactional mutations
```

**.NET solution layout (M0):**
- `UnderworldEmpire.Api` — controllers/endpoints, auth middleware, DI.
- `UnderworldEmpire.Domain` — entities, value objects, pure game-logic services (no I/O; unit-tested).
- `UnderworldEmpire.Infrastructure` — EF Core, DbContext, migrations, content-pack loader.
- `UnderworldEmpire.Tests` — xUnit unit + integration tests.
- `app/` — Flutter project (Android + web targets).
- `content/crime/` — the MVP theme pack (JSON + assets). Architecture supports more
  packs, but only `crime` ships in the MVP.
- `docker-compose.yml` — local Postgres.

**Content-pack schema:** `pack.json` (theme name, currency labels, palette, asset map),
`jobs.json`, `properties.json`, `items.json`, `strings.json`. Engine references abstract
ids; packs supply concrete content + art paths.

---

## Effort model

Estimates are **Claude Code execution effort (tokens)**, not your wall-clock time.
Bands: **S ~5–15k · M ~15–40k · L ~40–100k · XL >100k**. Greenfield generation skews to
the top of each band (everything is written from scratch, not edited).

---

## Milestones, iterable tasks & estimates

Every task is TDD where logic exists: write the failing test, then the implementation.

### M0 — Scaffolding & tooling
| # | Task | Band | ~tokens |
|---|------|------|--------|
| T0.1 | `git init`; create .NET solution (Api/Domain/Infrastructure/Tests) + Flutter app scaffold; README; folder layout | M | 25k |
| T0.2 | `docker-compose.yml` for Postgres; EF Core + DbContext + first migration; `/health` endpoint | M | 20k |
| T0.3 | Local test/run scripts (`dotnet test`, `flutter test`, run API + app); .gitignore | S | 8k |

**🔎 Test Checkpoint CP0:** `docker compose up -d`; `dotnet run` → `GET /health` returns
200; `dotnet test` and `flutter test` both run green (even if near-empty). Flutter app
launches on web and Android emulator showing a blank home.

### M1 — Foundation: identity, resource meters, economy, content loader
| # | Task | Band | ~tokens |
|---|------|------|--------|
| T1.1 | `Player` entity + Energy/Stamina/Health value objects with **lazy regen** (computed from `last_updated` × rate on read). TDD regen/cap. | M | 30k |
| T1.2 | Guest identity: `POST /session` (device-token → create/return Player) + bearer-token auth middleware | M | 25k |
| T1.3 | Economy service: cash + premium currency on Player; transactional add/spend with over-spend guards. TDD. | M | 18k |
| T1.4 | Content-pack loader: JSON schema + load `crime` pack at startup; `GET /content/pack` manifest endpoint. TDD load/validate. | M | 30k |
| T1.5 | `GET /me` — returns computed meters, currencies, level/XP, stats | S | 12k |
| T1.6 | Flutter: networking layer, device-token bootstrap/persist, Home screen (meters + cash), poll `GET /me` | L | 45k |

**🔎 Test Checkpoint CP1:** Launch the app fresh → it auto-creates a guest; Home shows
Energy/Stamina/Health bars + cash. Spend a meter via a debug button (or hit API) and
watch it **regenerate over time**. Confirm the same guest persists across app restarts.

### M2 — Jobs & progression
| # | Task | Band | ~tokens |
|---|------|------|--------|
| T2.1 | `jobs.json` content + Job domain + JobService (validate energy, RNG payout of XP/cash/loot, per-job mastery). TDD payout + mastery. | L | 45k |
| T2.2 | `POST /jobs/{id}/do` endpoint (transactional; deducts energy, applies rewards) | S | 12k |
| T2.3 | Leveling: XP curve + level-up (refill meters, grant skill points) + `POST /skills/allocate`. TDD curve + allocation caps. | M | 30k |
| T2.4 | Flutter: Jobs list screen, do-job + result panel, level-up popup, skill-point allocation UI | L | 45k |

**🔎 Test Checkpoint CP2:** Do a job → energy drops, cash + XP rise, mastery bar ticks
up. Grind jobs until level-up → meters refill, skill point granted → allocate it to
Attack and see the stat increase on Home.

### M3 — Items & properties
| # | Task | Band | ~tokens |
|---|------|------|--------|
| T3.1 | `items.json` catalog + PlayerItem inventory + equip/loadout + attack/defense aggregation. TDD aggregation. | M | 35k |
| T3.2 | Endpoints: buy item, equip/unequip, `GET /inventory` | M | 20k |
| T3.3 | `properties.json` + PlayerProperty + **capped lazy income accrual** + collect. TDD accrual + cap. | M | 30k |
| T3.4 | Endpoints: buy property, `POST /properties/{id}/collect`, `GET /properties` | S | 12k |
| T3.5 | Flutter: Inventory (equip), Store (buy items), Properties (buy/collect) screens | L | 55k |

**🔎 Test Checkpoint CP3:** Buy + equip a weapon → Attack/Defense rise. Buy a property →
income accrues over time up to its cap → collect → cash rises; confirm it stops accruing
at the cap.

### M4 — Combat, bounty & crew
| # | Task | Band | ~tokens |
|---|------|------|--------|
| T4.1 | Crew domain: recruit, cap grows with level, gear/power contribution. TDD. | M | 25k |
| T4.2 | Combat power formula (attack/defense + equipped gear + crew + randomizer) as a **pure function**. Heavy TDD (deterministic seed). | L | 45k |
| T4.3 | Opponent pool: snapshots of real players + AI bot generator (guarantees targets at low population) | M | 30k |
| T4.4 | `POST /fights` attack endpoint (stamina cost, resolve, health loss, cash transfer, fight log). TDD win/lose/edge. | M | 35k |
| T4.5 | Bounty list logic + `GET /bounties` | M | 18k |
| T4.6 | Flutter: Opponents list, attack + result panel, fight log, Bounty list, Crew screen | L | 60k |

**🔎 Test Checkpoint CP4:** Recruit crew → power rises. Open opponents → attack a
bot/player → stamina spent, clear win/lose result, cash transferred, your/their health
drops. Fight appears in log; beaten high-value targets show on the bounty list.

### M5 — Art, theming & polish
| # | Task | Band | ~tokens |
|---|------|------|--------|
| T5.1 | CC art asset sourcing + theming (palette, icons) for the `crime` pack, driven purely by pack manifest — assets swap without code changes | M | 30k |
| T5.2 | Flutter UX polish: loading/empty/error states, meter-regen countdown timers, navigation | L | 45k |
| T5.3 | End-to-end smoke pass, web + Android; fix fallout | M | 15k |

**🔎 Test Checkpoint CP5 (final):** Full playthrough (job → level → buy/equip → property
→ fight) works on **both** the web build and an Android build under the `crime` theme.
Confirm swapping an art asset in the pack updates the UI **without a code change**.

---

## Session plan (≤50K target, 25K buffer → 75K ceiling, each testable)

Every session ends at a state **you can verify yourself** — an app action, a Swagger/curl
call, or a green `dotnet test` / `flutter test` run. "Swagger" = the auto-generated API
test UI at `/swagger`. 22 sessions total.

### M0 — Scaffolding (2 sessions)
| Session | Contents | ~tok | How you test it |
|---|---|---|---|
| ✅ S0.1 | T0.1 solution + Flutter scaffold + git + T0.3 scripts/.gitignore | 33k | `dotnet build` + `dotnet test` + `flutter test` all run green (empty); Flutter app launches blank on web + Android emulator |
| S0.2 | T0.2 Postgres compose + EF + first migration + `/health` | 20k | `docker compose up -d`; `dotnet run` → open `/health` → 200; DB schema created (inspect tables) |

### M1 — Foundation (4 sessions)
| Session | Contents | ~tok | How you test it |
|---|---|---|---|
| S1.1 | T1.1 meters domain + lazy-regen value objects + tests | 30k | Run `dotnet test` → energy/stamina/health regen + cap tests green |
| S1.2 | T1.2 guest identity + auth middleware, T1.3 economy service | 43k | Swagger `POST /session` → get device token + new player; economy add/spend tests green |
| S1.3 | T1.4 content-pack loader (crime), T1.5 `GET /me` | 42k | Swagger `GET /content/pack` → crime manifest; `GET /me` → meters (regenerating), cash, level/XP |
| S1.4 | T1.6 Flutter networking + guest bootstrap + Home screen | 45k | **CP1**: launch app → guest auto-created, Home shows meter bars + cash, persists across restart |

### M2 — Jobs & progression (3 sessions)
| Session | Contents | ~tok | How you test it |
|---|---|---|---|
| S2.1 | T2.1 jobs domain + JobService (RNG payout, mastery) + tests | 45k | `dotnet test` → seeded payout + mastery tests green |
| S2.2 | T2.2 do-job endpoint, T2.3 leveling + skill-point allocation | 42k | Swagger: `POST /jobs/{id}/do` → energy↓, cash+XP↑; grind to level-up → skill point; `POST /skills/allocate` → stat↑ |
| S2.3 | T2.4 Flutter Jobs list + result panel + level-up + skill UI | 45k | **CP2**: in app do a job, watch meters/cash/XP, level up, allocate a skill point |

### M3 — Items & properties (4 sessions)
| Session | Contents | ~tok | How you test it |
|---|---|---|---|
| S3.1 | T3.1 items domain + attack/defense aggregation + tests | 35k | `dotnet test` → loadout aggregation tests green |
| S3.2 | T3.2 item endpoints (buy/equip/`GET /inventory`), T3.3 property domain + capped accrual + tests | 50k | Swagger: buy+equip item → attack/defense change; property accrual + cap tests green |
| S3.3 | T3.4 property endpoints (buy/collect), T3.5a Flutter Inventory + Store screens | 40k | Swagger buy/collect property; in app: Store→buy item, Inventory→equip, stats rise |
| S3.4 | T3.5b Flutter Properties screen (buy/collect UI + accrual display) | 27k | **CP3**: in app buy a property, watch income accrue to cap, collect → cash↑ |

### M4 — Combat, bounty & crew (6 sessions)
| Session | Contents | ~tok | How you test it |
|---|---|---|---|
| S4.1 | T4.2 combat power formula (pure fn) + heavy seeded tests | 45k | `dotnet test` → deterministic win/lose/edge-case combat tests green |
| S4.2 | T4.3 opponent pool + AI bot generator | 30k | Swagger `GET /opponents` → pool including bots |
| S4.3 | T4.4 attack endpoint (`POST /fights`) + tests | 35k | Swagger attack a bot → stamina spent, win/lose result, cash transfer, health↓, fight logged |
| S4.4 | T4.1 crew domain (recruit, cap, power), T4.5 bounty list | 43k | Swagger recruit crew → re-run fight is stronger; `GET /bounties` → beaten/high-value targets |
| S4.5 | T4.6a Flutter opponents list + attack + result panel + fight log | 35k | In app: open opponents, attack one, see result + fight log |
| S4.6 | T4.6b Flutter crew screen + bounty list | 25k | **CP4**: in app recruit crew (power↑), view bounty list |

### M5 — Art & polish (3 sessions)
| Session | Contents | ~tok | How you test it |
|---|---|---|---|
| S5.1 | T5.1 CC art sourcing + manifest-driven theming (crime) | 30k | Swap one art asset in the pack → UI updates with **no code change** |
| S5.2 | T5.2 Flutter polish: loading/empty/error states, meter countdown timers, nav | 45k | In app: see countdown timers on meters, graceful loading/error states |
| S5.3 | T5.3 end-to-end smoke, web + Android; fix fallout | 15k | **CP5**: full playthrough (job→level→buy/equip→property→fight) on web build **and** Android build |

**Session count by milestone:** M0=2 · M1=4 · M2=3 · M3=4 · M4=6 · M5=3 = **22 sessions**.
Each is ≤50K estimated with 25K headroom; a session that runs hot stops at its test
checkpoint regardless. Under `/gru`, each session is one decompose→implement→verify unit,
so its ledger entry lines up 1:1 with a checkpoint you can sign off.

---

## Total cost estimate

| Milestone | ~tokens |
|-----------|--------|
| M0 Scaffolding | ~53k |
| M1 Foundation | ~160k |
| M2 Jobs & progression | ~132k |
| M3 Items & properties | ~152k |
| M4 Combat/bounty/crew | ~213k |
| M5 Art & polish | ~90k |
| **Core subtotal** | **~800k** |
| Iteration/debugging buffer (~25%) | ~200k |
| **Projected total** | **~0.95–1.05M tokens** |

This is Claude Code **execution effort** to build the MVP end-to-end. Rough framing:
~15–22 focused build sessions. Combat (M4) and the Flutter UI tasks are the biggest and
riskiest chunks.

### Cost recalculated for `/gru` execution

`/gru` is the executor half of batman: an **Opus controller** decomposes the plan and
**verifies every task** (plus regression re-eval), while the actual implementation is done
by cheap-model **minions capped at Sonnet 4.6** — Haiku 4.5 for mechanical work, Sonnet
4.6 for logic/integration and fix-loop escalations. This changes cost two ways at once:

- **More total tokens** (~1.2–1.5×): minion implements → controller re-reads & verifies
  the diff → ≤5-round fix loop → regression re-eval of the integration each task.
- **Lower blended $/token**: the bulk of implementation moves off Opus onto Haiku/Sonnet.

**Model prices (per 1M tokens, current):** Opus 4.8 $5 in / $25 out · Sonnet 4.6 $3 / $15
· Haiku 4.5 $1 / $5. Blended at a ~65% input / 35% output mix (typical for agentic
coding): **Opus ≈ $12.0/M · Sonnet ≈ $7.2/M · Haiku ≈ $2.4/M**.

**Assumed gru token split:** ~25% Opus (controller: decompose + verify + regression +
final review) · ~40% Sonnet (logic minions + fix escalations) · ~35% Haiku (scaffolding,
DTOs, simple endpoints, Flutter widgets).

| Scenario | Total tokens | Blended cost |
|---|---|---|
| **Baseline — all-Opus** (single-context build, the estimate above) | ~1.0M | **≈ $12** |
| **gru, low overhead (~1.2×)** | ~1.2M | **≈ $8** |
| **gru, high overhead (~1.5×)** | ~1.5M | **≈ $10–11** |

**Net: `/gru` lands around ~$8–11 vs ~$12 all-Opus — a ~10–35% dollar saving**, despite
running 1.2–1.5× more tokens, because ~75% of the work runs on Sonnet/Haiku. The saving is
*modest*, not dramatic, because the Opus controller still verifies every task and the
fix/regression loops add real overhead.

**Why use `/gru` anyway:** the point is **reliability, not price** — a capped minion never
grades its own work, every task is independently verified against the plan, and later tasks
that break earlier ones get caught by the regression pass. For a game with interlocking
economy/combat/leveling math, that verification is worth more than the ~$2–4 saved.

**Caveats:** figures are estimates — the batman token counts are execution effort, not a
billed input+output split, and prompt caching (which helps all tiers) is not modelled. The
*relative* comparison (gru vs all-Opus) is far more robust than the absolute dollars. gru
also needs the multi-agent `Workflow` opt-in and a git repo (T0.1 provides it); wall-clock
is longer per task but minions on independent tasks run in parallel.

---

## Risks & unknowns
- **Balancing** (regen rates, payouts, XP curve, combat scaling) is iterative tuning, not
  a one-shot — kept fully config-driven so it's tweakable without code. Buffer covers a
  couple of tuning passes.
- **Flutter web + Android parity** (fonts, asset loading, network) — CP0/CP5 verify both.
- **Async PvP at low population** — mitigated by the bot generator (T4.3).
- **Content-pack schema churn** — nail the schema in M1 (T1.4) before packs multiply.

## Fallbacks
- If combat (M4) over-runs, ship M0–M3 as a playable single-player-feel checkpoint build
  and treat M4 as the next increment.
- If Flutter web underperforms, ship Android first, keep web behind the same API.

## Verification (end-to-end)
Primary verification is the six manual **Test Checkpoints (CP0–CP5)** above — one per
milestone — plus the automated suites (`dotnet test` for domain/economy/combat/regen +
API integration tests; `flutter test` for widget tests). A milestone is "done" only when
its checkpoint passes on both web and Android and its tests are green.

## Suggested execution flow
Build milestone-by-milestone. After each milestone I recommend a `/batman`-style
validation gate before moving on: pseudo-build/tests green → `/code-review` → fix →
proceed. (Full worktree gate needs git, which T0.1 initializes.)

---

## Execution log (gru)

Session-by-session record of the actual build. Env: Windows 11 · .NET 10.0.301 · Flutter
3.35.7 (Dart 3.9.2) · Docker 29.4 · git local identity `jasonweiss9925@gmail.com`.

### ✅ S0.1 — Scaffolding & tooling (M0)
- `git init` on `main`; local git identity set to personal account.
- .NET solution `UnderworldEmpire.slnx` (new .NET 10 XML solution format) with
  `src/{Api,Domain,Infrastructure}` + `tests/UnderworldEmpire.Tests`; references wired
  (Infra→Domain; Api→Domain,Infra; Tests→all three).
- API `Program.cs` trimmed to minimal host (WeatherForecast removed); `Program` made
  `partial` for future `WebApplicationFactory` integration tests. `/health` deferred to S0.2.
- Pinned `Microsoft.OpenApi` 2.12.0 to clear an NU1903 high-severity advisory in the
  template's transitive 2.0.0 → **build is 0 warnings / 0 errors**.
- Flutter app scaffolded under `app/` (`android` + `web`, org `co.pepla`, pkg
  `underworld_empire`).
- Added `content/crime/` placeholder, `scripts/{test,run-api,run-app}.ps1`, root README,
  `.gitignore`, `.gitattributes` (LF normalize, CRLF for `.ps1`).
- **Verification:** `dotnet build` 0/0 · `dotnet test` 1 passed · `flutter test` 1 passed.
- Deferred to CP0 (end of M0): manual app launch on web + Android emulator.
- gru: executed single-context by the controller (sequential in-place scaffolding, no git
  repo yet to worktree-isolate). Minion dispatch begins at S1.1.
