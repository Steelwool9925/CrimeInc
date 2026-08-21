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

### M3R — Job risk: stat gates & failable jobs ✅

> Added after M3 was scoped, at the user's request (2026-08-20). Slotted **after** M3 rather than
> inside M2 because the success chance is measured against *effective* stats — base plus equipped
> gear — so it depends on `Loadout` from T3.1, which did not exist when M2 ran. The full design,
> already decided, is specified in **§ M3R design** below; these rows are the executable tasks.

| # | Task | Band | ~tokens |
|---|------|------|--------|
| T3R.1 | `Job` record: `attackRequirement`/`defenseRequirement`, `baseSuccessChance`, `successChancePerSurplusPoint`, `healthLossMin/Max` + `EnsureValid` guards + `SuccessChanceFor`/`MeetsStatRequirements`. TDD the chance curve and the per-stat surplus floor. | M | 25k |
| T3R.2 | `jobs.json`: pack-wide `risk` block (`maxSuccessChance`, `consolationCashFraction`, `consolationXpFraction`) + per-job `requirements` and `risk` blocks; balance pass across all six jobs; `content/crime/README.md` schema + balance notes. | M | 20k |
| T3R.3 | Content loader: `JobRequirementsJson`/`JobRiskJson`/`RiskRulesJson` (nullable, so a *missing* block is a named error), `ValidateJobs` rules, `JobCatalog` exposes the pack-wide risk rules. TDD every new validation error. | M | 25k |
| T3R.4 | Domain: `JobFailure.StatsTooLow`, a `JobOutcome` that can describe a **failed but applied** attempt, and `JobService.Attempt` — stat gate, basis-point success roll, consolation payout, clamped health loss, no mastery on failure. Heavy TDD with the scripted `FakeRandomSource`. | L | 45k |
| T3R.5 | `JobActionService`: build the `Loadout` for the attempt, persist a failed attempt (it now mutates state), keep the xmin conflict behaviour. Then API contracts + `JobEndpoints`: `GET /jobs` reports requirements and live success chance; `POST /jobs/{id}/do` returns a failure outcome as **200, not 409**. TDD both routes. | L | 40k |
| T3R.6 | Flutter: models + `api_client` + `app_state` for the new fields; Jobs screen shows each job's requirement and success %, and gates on `"stats"`; result panel renders a failed attempt (consolation cash/XP, health lost). Widget tests for both branches. | L | 45k |

**🔎 Test Checkpoint CP3R:** A level-1 job below your stat minimum refuses with `"stats"` and
spends nothing. Equip a weapon → the same job becomes attemptable and its displayed success %
rises. Grind it until one attempt fails → energy still spent, ~1% cash and ~10% XP awarded,
health may drop, mastery bar does **not** advance.

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
| ✅ S0.2 | T0.2 Postgres compose + EF + first migration + `/health` | 20k | `docker compose up -d`; `dotnet run` → open `/health` → 200; DB schema created (inspect tables) |

### M1 — Foundation (4 sessions)
| Session | Contents | ~tok | How you test it |
|---|---|---|---|
| ✅ S1.1 | T1.1 meters domain + lazy-regen value objects + tests | 30k | Run `dotnet test` → energy/stamina/health regen + cap tests green |
| ✅ S1.2 | T1.2 guest identity + auth middleware, T1.3 economy service | 43k | Swagger `POST /session` → get device token + new player; economy add/spend tests green |
| ✅ S1.3 | T1.4 content-pack loader (crime), T1.5 `GET /me` | 42k | Swagger `GET /content/pack` → crime manifest; `GET /me` → meters (regenerating), cash, level/XP |
| ✅ S1.4 | T1.6 Flutter networking + guest bootstrap + Home screen | 45k | **CP1**: launch app → guest auto-created, Home shows meter bars + cash, persists across restart |

### M2 — Jobs & progression (3 sessions)
| Session | Contents | ~tok | How you test it |
|---|---|---|---|
| S2.1 | T2.1 jobs domain + JobService (RNG payout, mastery) + tests | 45k | `dotnet test` → seeded payout + mastery tests green |
| S2.2 | T2.2 do-job endpoint, T2.3 leveling + skill-point allocation | 42k | Swagger: `POST /jobs/{id}/do` → energy↓, cash+XP↑; grind to level-up → skill point; `POST /skills/allocate` → stat↑ |
| S2.3 | T2.4 Flutter Jobs list + result panel + level-up + skill UI | 45k | **CP2**: in app do a job, watch meters/cash/XP, level up, allocate a skill point |

### M3 — Items & properties (4 sessions)
| Session | Contents | ~tok | How you test it |
|---|---|---|---|
| ✅ S3.1 | T3.1 items domain + attack/defense aggregation + tests | 35k | `dotnet test` → loadout aggregation tests green |
| ✅ S3.2 | T3.2 item endpoints (buy/equip/`GET /inventory`), T3.3 property domain + capped accrual + tests | 50k | Swagger: buy+equip item → attack/defense change; property accrual + cap tests green |
| ✅ S3.3 | T3.4 property endpoints (buy/collect), T3.5a Flutter Inventory + Store screens | 40k | Swagger buy/collect property; in app: Store→buy item, Inventory→equip, stats rise |
| ✅ S3.4 | T3.5b Flutter Properties screen (buy/collect UI + accrual display) | 27k | **CP3**: in app buy a property, watch income accrue to cap, collect → cash↑ |

### M3R — Job risk ✅ (3 sessions, executed as one run)
| Session | Contents | ~tok | How you test it |
|---|---|---|---|
| ✅ S3R.1 | T3R.1 `Job` risk fields + chance curve, T3R.2 `jobs.json` balance + README, T3R.3 loader validation | 45k | `dotnet test` → chance-curve and content-validation tests green; host refuses a `jobs.json` with a missing `risk` block, naming it |
| ✅ S3R.2 | T3R.4 `JobService` failure path, T3R.5 `JobActionService` loadout + endpoints | 50k | Swagger: a job under your stat minimum → 409 `"stats"`, nothing spent; a job you qualify for → 200 with `succeeded: true/false` and the right consolation figures |
| ✅ S3R.3 | T3R.6 Flutter jobs UI: requirements, success %, failure result panel | 45k | **CP3R**: in app a stat-locked job is greyed with its requirement; equip a weapon → it unlocks and the % rises; a failed attempt shows the consolation payout and health lost |

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

**Session count by milestone:** M0=2 · M1=4 · M2=3 · M3=4 · M3R=3 · M4=6 · M5=3 = **25 sessions**.
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

### ✅ S0.2 — Postgres + EF + first migration + /health (M0)
- EF Core 10 + Npgsql provider 10.0.3 in Infrastructure; `Microsoft.EntityFrameworkCore
  .Design` 10.0.2 + `Swashbuckle.AspNetCore` 10.2.3 in Api. Dropped the unused
  `Microsoft.AspNetCore.OpenApi` (Swagger UI now via Swashbuckle at `/swagger`). OpenApi
  2.12.0 pin retained (Swashbuckle pulls it transitively) → **build 0/0**.
- `GameDbContext` (Infra) with `DbSet<Player>`; minimal `Player` (Id:uuid, CreatedAtUtc:
  timestamptz) in Domain — first-migration seed, extended in M1. `AddInfrastructure(config)`
  DI extension; connection string from `ConnectionStrings:Default` env/config with a
  local-dev fallback matching docker-compose. **No secret committed.**
- `Program.cs`: `AddInfrastructure`, Swagger UI (Dev), `GET /health` (liveness, 200) and
  `GET /health/db` (readiness, checks `CanConnectAsync`).
- `docker-compose.yml`: `postgres:17-alpine`, volume, `pg_isready` healthcheck, local-dev
  creds. First EF migration `InitialCreate` → `Players` table.
- **Verification:** `dotnet build` 0/0 · `dotnet test` **2 passed** (added
  `HealthEndpointTests` proving `GET /health`→200 in-memory via `WebApplicationFactory`, no
  DB needed) · `flutter test` 1 passed.
- **CP0 fix (native-Postgres port conflict):** on first CP0 run, `dotnet ef database update`
  failed with `role "underworld" does not exist` despite a healthy container. Root cause: a
  **native PostgreSQL service** on `127.0.0.1:5432` shadowed the container for host tools
  (localhost → 127.0.0.1 first). Resolved by republishing the container on host port **5433**
  and updating the local-dev connection string. Migration then applied; schema verified.
- gru: controller single-context (M0 plumbing is tightly coupled/foundational). Minion
  dispatch begins at **S1.1**.

---

## ▶ CP0 — Milestone M0 checkpoint

Prereq: **start Docker Desktop** first (its daemon must be running).

> **Note — Postgres runs on host port 5433, not 5432.** CP0's first run surfaced a real
> conflict: this machine has a **native PostgreSQL Windows service** already listening on
> `127.0.0.1:5432`. Because `localhost` resolves to `127.0.0.1` first, host tools (`dotnet
> ef`) hit the native server — which has no `underworld` role — instead of the container,
> giving `28000: role "underworld" does not exist`. Fixed by publishing the container on
> **5433** (`docker-compose.yml`) and pointing the app's local-dev connection string at 5433.
> The native Postgres is left untouched.

**Server-side CP0 (✅ verified by the controller):**
- `docker compose up -d --wait` → `underworld-postgres` healthy on `0.0.0.0:5433`.
- `dotnet ef database update` → migration `InitialCreate` applied.
- Schema confirmed in the `underworld` DB: `Players` (Id uuid PK, CreatedAtUtc timestamptz)
  + `__EFMigrationsHistory` (InitialCreate, 10.0.4).
- `dotnet test` 2 pass (incl. `/health`→200 via WebApplicationFactory); `flutter test` 1 pass.

**Client/UI CP0 (yours to eyeball):**
```powershell
docker compose up -d --wait                 # underworld-postgres healthy (host port 5433)
./scripts/run-api.ps1
#    open http://localhost:<port>/health      → 200 {"status":"ok"}
#    open http://localhost:<port>/health/db   → 200 reachable   (Postgres up on 5433)
#    open http://localhost:<port>/swagger     → Swagger UI loads
./scripts/run-app.ps1                         # web (Chrome) → blank home
#    and on an Android emulator: cd app; flutter run -d <emulator>
```

Server-side M0 is green. Once you've eyeballed the API/app in a browser, M0 is fully signed
off and I'll start **M1** (identity, meters, economy, content loader) with real minion
dispatch (worktree-isolated, model ≤ Sonnet).

---

## M1 — Foundation ✅ (executed via /gru; pending user CP1)

Real minion dispatch began here: **5 Sonnet minions** across 4 sessions, each independently
verified by the Opus controller (diff review + `dotnet build`/`dotnet test` + migration apply
+ DB schema check). **0 fix-loop rounds** — every minion passed first verification.

### ✅ S1.1 — Resource meters (T1.1)
- `Meter` value object (readonly record struct): lazy regen `CurrentAt(now, rate)` (floor,
  capped), `SpendAt` (guard + clock reset), `GrantAt` (capped), `RefillAt`; ctor guards.
- `MeterRates` (energy/stamina 5 min/pt, health 1 min/pt), `PlayerDefaults` (10/5/100).
- Player gains Energy/Stamina/Health (start full); EF `ComplexProperty` mapping (value-type
  struct → can't `OwnsOne`). Migration `AddPlayerMeters`. 35 domain tests.

### ✅ S1.2 — Guest identity + economy (T1.2 + T1.3)
- Economy: `Player.Cash` + `PremiumCurrency` (bigint) with guarded Add/Spend (negative →
  `ArgumentOutOfRange`; overdraft → `InvalidOperationException`); starting 100 / 5. Thin
  `EconomyService`. 18 domain tests.
- Identity: `Player.DeviceToken` (unique index); server-generated 32-byte base64url token.
  `POST /session` (anonymous) find-or-create guest; `GET /whoami` (`[Authorize]`).
  `DeviceTokenAuthHandler` (Bearer scheme) → `NameIdentifier` claim; no fallback policy so
  `/health` + `/session` stay anonymous; Swagger Bearer definition. 4 integration tests
  against a dedicated `underworld_test` DB. Migrations `AddPlayerCurrencies` +
  `AddPlayerDeviceToken`.

### ✅ S1.3 — Content-pack loader + /me (T1.4 + T1.5)
- `content/crime/pack.json` (id/name/currencies/palette/assets/version), copied to build
  output. `ContentPackLoader` with validation (id match, name, currency labels; collects all
  errors → `ContentPackException`); `IContentPackProvider` singleton loaded at startup.
  `GET /content/pack` (anonymous). 7 loader tests.
- Player progression fields (Level 1, Xp 0, SkillPoints 0, Attack/Defense 5 — **fields only;
  leveling/allocation logic is M2**). Migration `AddPlayerProgression`. `GET /me`
  (`[Authorize]`) projects lazy-regenned meters (current/max/regenSecs/secsUntilNext) +
  currencies + level/xp/skillpoints/stats; read-only. 2 integration tests.

### ✅ S1.4 — Flutter Home (T1.6)
- `ApiClient`/`HttpApiClient` with platform-aware base URL (web `localhost:5080`, Android
  emulator `10.0.2.2:5080`, `API_BASE_URL` override) + Bearer auth. Device-token persistence
  via `shared_preferences`. `AppState` (`ChangeNotifier`): bootstrap (token → `/session` →
  persist → `/content/pack` → `/me`), 10 s polling, error/retry. `HomeScreen`: Energy/Stamina/
  Health bars (current/max + regen countdown) + cash with the pack's currency label; pack
  primary colour on the app bar. 19 Flutter tests (fakes).
- API dev prep (controller): dev CORS + HTTPS-redirect skipped in Development; `run-api.ps1`
  pinned to `http://localhost:5080`.

**M1 verification:** `dotnet build` 0/0 · **`dotnet test` 67 pass** · `flutter analyze` clean
· **`flutter test` 19 pass** · `flutter build web` OK. All migrations applied to the dev DB.

---

## ▶ CP1 — Milestone M1 checkpoint (please run)

Prereq: Docker Desktop running; dev DB migrated (from CP0 — or run `dotnet ef database update
--project src/UnderworldEmpire.Infrastructure --startup-project src/UnderworldEmpire.Api`).

```powershell
# 1. Postgres + API (fixed port 5080)
docker compose up -d --wait
./scripts/run-api.ps1               # http://localhost:5080  (Swagger at /swagger)

# 2. (optional API sanity via Swagger) POST /session -> copy deviceToken -> Authorize ->
#    GET /me returns meters + cash 100; GET /content/pack -> crime manifest

# 3. Flutter app — in a second terminal
./scripts/run-app.ps1               # web (Chrome)
#    Expect: app auto-creates a guest, Home shows Energy/Stamina/Health bars + Cash 100.
#    Close & reopen the app -> SAME guest (token persisted), state unchanged.
#    Android: cd app; flutter run -d <emulator>   (uses 10.0.2.2:5080 automatically)
```

Note: with no spend action yet in M1, meters start full; **visible regen is easiest to see
after M2 jobs spend energy.** CP1's core assertions — guest auto-create, Home shows meters +
cash, and the **same guest persists across restart** — are all testable now.

Green CP1 = M1 done. Approve and I'll start **M2** (jobs, RNG payouts, mastery, leveling,
skill allocation, Jobs UI).

---

## M2 — Jobs & progression ✅ (executed via /execute-plan; pending user CP2)

Executed from `.claude/plans/FEATURE_PLAN_JobsProgression.md`, **Tier 2 (Localized
Optimization)**, in worktree `.claude/worktrees/m2a-jobs-progression` on branch
`worktree-m2a-jobs-progression`. Run as a **single-context executor** — no subagents, since the
session's standing instructions bar them unless asked. Milestones M2-A → M2-D.

### ✅ M2-A — Job catalog as content (T2.1, content half)
- `content/crime/jobs.json`: 6 jobs, level gates 1/2/3/5/8, energy costs 1–9, inclusive cash
  ranges, fixed XP, pack-wide mastery (10 completions/rank, max rank 5).
- `ContentPackLoader.LoadJobs` + `ValidateJobs` — collects **all** errors in one message like
  `pack.json` does, naming each bad entry by array index *and* id. Loaded at startup inside
  `AddInfrastructure`, so invalid content fails the host rather than the first request.
- Domain: `Job` (with `EnsureValid`), `PlayerJobMastery` (composite key, stored rank),
  `IRandomSource` seam; `SystemRandomSource` in Infrastructure (`Random.Shared`, deliberately not
  crypto — a payout roll is unobservable to the player, unlike a device token).
- `JobCatalog` holds jobs in a `FrozenDictionary` with an **ordinal** comparer — ids are content
  keys, so casing is significant.
- `GET /jobs` (`[Authorize]`, unlike anonymous `/content/pack`) — catalog + per-player mastery +
  server-computed `canAttempt`/`unavailableReason`. Two queries regardless of catalog size.
- Migration `AddPlayerJobMastery`: `PK ("PlayerId","JobId")`, FK cascade from Players.

### ✅ M2-B — Doing a job (T2.1 logic half + T2.2)
- `JobService.Attempt` — validates **before** mutating (level, then energy), spends via
  `Meter.SpendAt`, rolls cash from the content range, grants XP, records one completion. A refusal
  mutates nothing and takes no roll.
- `POST /jobs/{id}/do` — no request body at all; every figure is server-decided. 404 unknown job,
  409 + `reason` token (`level`/`energy`) on refusal, matching `GET /jobs`' vocabulary.
- `JobActionService` — one `SaveChangesAsync` covers energy + cash + XP + level + mastery.
- The response carries the **full post-action player projection**, so the client never follows a
  job with a `GET /me`. `PlayerProjection` was extracted from `MeEndpoints` for this rather than
  duplicating the regen countdown arithmetic.

### ✅ M2-C — Levelling and skill points (T2.3)
- `LevelCurve`: cumulative XP to reach level L is `50·(L−1)·L` → 0, 100, 300, 600, 1000 …
  `LevelForXp` solves the quadratic then corrects by stepping, because `Math.Sqrt` on a large
  `long` can land a level either side of a boundary.
- `Player.ApplyXpAndLevelUp` resolves **all** levels from one grant in a single pass, grants
  skill points, and refills all three meters. Never demotes a player whose level is ahead of the
  curve.
- `POST /skills/allocate` — target parsed to an enum at the boundary (case-insensitive), 400 on
  an unknown target, 409 `noSkillPoints` when the pool is empty. A point is decremented only
  after the target is known good.

### ✅ M2-D — Flutter Jobs & Skills UI (T2.4)
- Models: `Job`, `JobList`, `JobMastery`, `JobResult`, `LevelUp`, `DoJobResult`.
  `JobMastery.completionsPerRank` recovers the threshold exactly from what the server sends
  (`(completions + remaining) / (rank + 1)`), so the mastery bar needs no extra contract field.
- `JobRefusedException` separates normal-play refusals from transport faults, so the UI shows a
  message in place instead of an error screen with a retry.
- `JobsScreen` (cards, mastery bars, server-driven lock labels), `JobResultPanel` (realised
  payout), `LevelUpDialog`, `SkillsScreen`; Home gains a level/XP/stats card and navigation.
- **No router added.** Direct `MaterialPageRoute` pushes keep the existing hand-wired
  composition; named routes would need argument plumbing or a DI container to pass `AppState`.

### Balance tuning that CP2 depends on
Ten runs of the 1-energy job — exactly one full starting energy bar — award exactly **100 XP**
(the level-2 threshold) and exactly **10 completions** (the first mastery rank). This makes the
whole CP2 loop reachable without waiting on energy regeneration. Two tests in
`JobCatalogLoaderTests` pin the underlying properties so a later balance pass cannot silently
break the checkpoint.

### Bug found and fixed during execution
Two concurrent attempts at a job a player has **never done before** each tried to INSERT the same
`(PlayerId, JobId)` mastery row. The loser hit the composite primary key (`23505`) rather than the
`xmin` concurrency token — the INSERT fails before the player UPDATE's check can matter — so it
surfaced as an unhandled `DbUpdateException`, i.e. a 500 on any first-time double-tap. Now caught
and reported as the same 409 conflict, narrowed to that one SQLSTATE so genuine faults still throw.
Found by `JobConcurrencyTests`, which existed only because Tier 2 required proving the fix.

**M2 verification:** `dotnet build` 0 warnings / 0 errors · **`dotnet test` 198 pass** ·
`flutter analyze` clean · **`flutter test` 84 pass** · migration applied to `underworld` and
`underworld_test` · composite key confirmed in the live schema · **no physical `xmin` column**
added (it maps to Postgres's system column, so the concurrency token needed no DDL) · host
verified to refuse startup on a deliberately corrupted `jobs.json`, reporting all three injected
violations at once.

---

## ▶ CP2 — Milestone M2 checkpoint (please run)

```powershell
# 1. Postgres + API (fixed port 5080)
docker compose up -d
./scripts/run-api.ps1

# 2. API sanity via Swagger or UnderworldEmpire.Api.http:
#    POST /session       -> copy deviceToken -> Authorize
#    GET  /jobs          -> 6 jobs; level-1 ones attemptable, others "unavailableReason": "level"
#    POST /jobs/shake-down-corner-store/do  -> energy down 1, cash +15..30, xp +10, mastery 1
#    repeat x10          -> levelUp {newLevel: 2, skillPointsGranted: 1, metersRefilled: true}
#                           and mastery rank 1; meters back to full
#    POST /skills/allocate {"target":"attack"} -> attack 5 -> 6
#    GET  /me            -> the higher attack persists

# 3. Flutter app — in a second terminal
./scripts/run-app.ps1
#    Home -> Jobs -> tap "Do job" on Shake Down a Corner Store.
#    Expect: energy drops, result panel shows the realised payout, mastery bar ticks up.
#    Keep tapping to level 2 -> level-up dialog, meters refill, Home shows "1 skill point to spend".
#    Home -> Skills -> tap + on Attack -> stat rises; back on Home, Attack shows the new value.
#    Android: cd app; flutter run -d <emulator>   (uses 10.0.2.2:5080 automatically)
```

Green CP2 = M2 done. Note **CP1 was never run** — M2 was built on M1 unverified, at the user's
direction; if anything in CP1 fails, treat it as an M1 defect surfacing late rather than an M2
regression.

---

## M3 — Items & properties ✅ (code-complete; pending user CP3)

Executed via `/execute-plan` in worktree `.claude/worktrees/m3a-items-loadout` on branch
`worktree-m3a-items-loadout`. Single-context executor — no subagents, per the session's standing
instructions. Built test-first throughout: every type below had its test written and watched fail
before the production code existed.

### ✅ S3.1 — Item catalog, inventory & loadout aggregation (T3.1)

**Content.** `content/crime/items.json`: 9 items across the three slots (weapon/armor/vehicle),
level gates 1–10, prices 75–3200, non-negative `stats.attack`/`stats.defense`.
`ContentPackLoader.LoadItems` + `ValidateItems` collect **all** errors in one message the way
`jobs.json` does, naming each bad entry by array index *and* id. Loaded at startup inside
`AddInfrastructure`, so invalid content fails the host rather than the first request that opens the
store.

**Domain (`UnderworldEmpire.Domain/Items`).**
- `Item` (with `EnsureValid`), `ItemSlot` (engine vocabulary, not content), `PlayerItem`
  (composite key, quantity + equipped flag), `OwnedItem` (a holding paired with its definition).
- `Loadout.For(player, equippedItems)` — the attack/defense aggregation. Pure, computed on read,
  **never stored**: an effective-stat column would need recomputing on every equip, unequip and
  content retune, and one missed path would leave a player permanently wrong. Reports base and item
  contributions separately so a client can show both.
- `InventoryService` — `Purchase` / `Equip` / `Unequip`. Validates before mutating, so a refusal
  changes nothing; level is reported ahead of cash because it is the permanent obstacle. Stateless
  and deliberately RNG-free: a shop price is a figure the player plans against, unlike a job payout.
- Item stats are **not** copied onto the holding. A retune of `items.json` must move every player's
  numbers; a snapshot taken at purchase time would freeze balance into player data.

**Persistence.** `PlayerItems` with `PK ("PlayerId","ItemId")`, `ItemId varchar(64)`, FK cascade
from Players. Migration `AddPlayerItems`.

### The one-equipped-item-per-slot invariant has no database constraint

An item's slot lives in `items.json`, so there is no slot column on `PlayerItems` to build a
filtered unique index over — and adding one would duplicate a content fact that a retune can change.
The invariant is therefore enforced in two places instead: `InventoryService.Equip` unequips the
incumbent in that slot on the way in, and `Loadout.For` **throws** rather than aggregate a loadout
with two items in one slot. Silently summing both would hand the player double the gear bonus.

> **Corrected 2026-08-20 (S3.2).** This section originally claimed the two-items-in-one-slot state was
> "unreachable through the API". That was **false**, and S3.2 fixed the hole before shipping the
> endpoints that would have opened it. Two concurrent equips of two different weapons each saw an
> empty slot, so neither displaced the other and both writes landed on different `PlayerItems` rows.
> The `xmin` token on `Players` could not catch it because an equip changes no player column — nothing
> conflicted. The damage was permanent rather than transient: `Loadout.For` throws on that state, so
> every later read of the player's own inventory would 500 with no API path back. Fixed in
> `InventoryActionService.TouchForConcurrency`, which forces the player row into the update batch so
> its token becomes the serialisation point. Pinned by `InventoryConcurrencyTests`.

The throw in `Loadout.For` remains, and remains worth having: it is the reason the race showed up as a
loud failure rather than as a quietly doubled gear bonus.

### Balance tuning that CP3 depends on

*Brass Knuckles* — level 1, 75 cash, +4 attack — is buyable outright from the starting 100 cash, so
"buy + equip a weapon and watch Attack rise" needs no grinding first. Three tests in
`ItemCatalogLoaderTests` pin the underlying properties (a level-1 item within starting cash that
actually moves a stat; every slot populated; no free items) so a later balance pass cannot silently
break the checkpoint.

### Bug found and fixed during execution

`InventoryService.Equip` located the incumbent to displace by slot alone. A caller that passed every
holding rather than only the equipped ones would get a result naming an item the player was not
wearing as "unequipped" — a lie the client shows in its result panel. Now filtered on `Equipped` as
well, which also makes passing the whole inventory safe rather than wrong. Found by self-review after
green, then reproduced as a failing test before the fix.

**S3.1 verification:** `dotnet build` 0 warnings / 0 errors · **`dotnet test` 282 pass** (203 before
this session) · migration applied to `underworld` and `underworld_test`, composite key + cascade
confirmed against the live schema · host verified to refuse startup on a deliberately corrupted
`items.json`, reporting all six injected violations at once. No Flutter or endpoint code touched —
`GET /inventory`, buy and equip are T3.2, and nothing yet surfaces effective stats over the wire.

### ✅ S3.2 — Item endpoints, property domain & capped income accrual (T3.2 + T3.3)

Resumed 2026-08-20 in worktree `.claude/worktrees/m3b-item-endpoints-properties` from the run the user
killed on 2026-08-19. Merged to `main`. The interrupted session's uncommitted work was reviewed before
being committed; T3.3 was then built from nothing, test-first throughout.

**State on resume.** The concurrency fix the previous session had stubbed out to watch its tests fail
was already restored in the worktree, and the suite was **328/328**, not the 326/2 the interruption
notes predicted. Nothing else had to be recovered.

#### T3.2 — buy, equip, unequip, `GET /inventory`

`GET /inventory` returns the **whole catalog**, not only owned rows: the store and the inventory are
two views of one list, so a single call serves both screens and they cannot disagree about an item.
Each row carries `canBuy`/`canEquip` plus a stable reason token, computed by asking
`InventoryService.CheckPurchase`/`CheckEquip` — the same question the write path refuses on. That is
the point of extracting them: a client told it may buy something is never then rejected, where
re-deriving the gate at the projection would be a second copy of the rule, free to drift.

`InventoryActionService` applies each action in one `SaveChangesAsync`. Conflicts are reported and
never retried, because a retry would charge twice — the same rule `JobActionService` follows.

**The cross-row equip race, fixed.** See the corrected S3.1 note above: two concurrent equips left a
player wearing two weapons in one slot, permanently 500ing every later read of their own inventory.
`TouchForConcurrency` forces the player row into the update batch so its `xmin` token becomes the
serialisation point for an operation that writes no player column. Latent on `main` until these
endpoints landed, which is exactly why it was fixed before merging rather than filed.

#### T3.3 — properties, holdings, capped lazy accrual, collect

**Content.** `content/crime/properties.json`: six properties, level gates 1–12, prices 500–140,000,
each with `income.perHour` and `income.capMinutes`. Validated at startup on the same terms as
`items.json` — every problem reported at once, an invalid catalog stops the host.

**Lazy, like the meters.** Nothing runs on a timer. What a property has earned is derived from
elapsed time whenever somebody asks, so a server with a million idle properties does no work for any
of them.

**The clock advances by less than the elapsed time.** Cash is whole numbers, so a settle usually
leaves a fraction of a coin unearned. Resetting the clock to `now` would discard it — which means a
client polling every second would earn strictly *less* than one polling once an hour, and a player
tapping *collect* repeatedly could earn nothing at all. `PropertyIncome.Accrue` advances the stored
instant only by the time the payout actually consumed. Pinned by tests that settle 600 times and
compare against settling once. Collecting nothing is **refused rather than applied** for the same
reason: a refusal must not settle the holding.

**The cap does discard time**, deliberately. Once full the clock jumps to `now`: the hours a full
property spent waiting are worth nothing, and banking them as a head start would quietly undo the
reason to come back.

**Two stored fields, not one.** Income could come from a timestamp alone if the rate never changed,
but buying another unit changes it. `UncollectedCash` banks what the old rate earned so the new one is
never applied retroactively; `AccruedAtUtc` carries the part-earned coin. Every method that can change
the rate settles first. Rate and price are never copied onto the holding — a retune of
`properties.json` must move every player's income, the same rule `PlayerItem` follows for stats.

**Persistence.** `PlayerProperties` with `PK ("PlayerId","PropertyId")`, `PropertyId varchar(64)`,
`UncollectedCash bigint`, `AccruedAtUtc timestamptz`, FK cascade from Players. Migration
`AddPlayerProperties`.

**No endpoints.** `POST /properties/{id}/buy`, `POST /properties/{id}/collect` and `GET /properties`
are **T3.4**, in S3.3. Nothing surfaces property income over the wire yet.

### Balance tuning that CP3 depends on (properties)

*Street Corner* — level 1, 500 cash, 240/hour, a 15-minute window — earns 4 a minute and fills its
cap at 60. That is roughly two full energy bars of the starting job to buy, and a window short enough
that "watch income accrue to the cap, then collect" is observable inside one sitting rather than
overnight. Four tests in `PropertyCatalogLoaderTests` pin those properties (a level-1 property inside
a short grind; a starter that fills in ≤30 minutes and earns ≥1 coin a minute; no free properties;
every property repays its own price within 200 full collections) so a later balance pass cannot
silently break the checkpoint.

### Bug found and fixed during execution

`ContentPackLoader.ValidateProperties` checks that `perHour × capMinutes` does not floor to zero cash
— but that guard cannot see an **overflow**. The realistic content typo is extra zeros, and while most
of those wrap the product negative (which the guard does catch), some wrap back round to a positive:
`2^62` an hour over nine minutes reads as `2^62` again. That would load as a property with an absurd
cap, and enough units of it would wrap the cap negative, so a collection would pay negative cash and
`Player.AddCash` would throw a 500. Found by self-review after green, then reproduced as three failing
tests — the wrapping catalog demonstrably loaded clean — before upper bounds were added to both
fields. The bounds are stated as design facts, not arithmetic ones: a month is the longest window for
which "come back before it fills" means anything, and a million an hour is 280× the top property in
the shipped pack.

**S3.2 verification:** `dotnet build` 0 warnings / 0 errors · **`dotnet test` 429 pass** (328 on
resume, 282 before the session) · `AddPlayerProperties` applied to `underworld` and `underworld_test`,
composite key + cascade confirmed against the live schema · host verified to refuse startup on a
deliberately corrupted `properties.json`, reporting all nine injected violations at once · live API
smoke against a running host: guest session → `GET /inventory` (9 items, gating tokens correct) → buy
Brass Knuckles (cash 100→25) → equip (attack 5→9) → re-read confirms it persisted → unequip (attack
back to 5), plus refusals verified end to end (re-equip 409 `alreadyEquipped`, level gate 409 `level`,
affordability 409 `cash`, unknown id 404, wrong-case id 404, double unequip 409 `notEquipped`,
anonymous read 401). No Flutter code touched.

### ✅ S3.3 — Property endpoints + Flutter Inventory/Store (T3.4 + T3.5a)

Executed 2026-08-20 via `/execute-plan` in worktree `.claude/worktrees/m3c-property-endpoints-flutter`
on branch `worktree-m3c-property-endpoints-flutter`, from `8d5739c`. Single-context executor — no
subagents, per the session's standing instructions. Test-first throughout: every type below had its
test written and watched fail before the production code existed.

#### T3.4 — `GET /properties`, buy, collect · commit `112676b`

`GET /properties` returns the **whole catalog**, not only owned rows — the same rule `GET /inventory`
follows, so the "buy" and "collect" halves of the Properties screen are one list that cannot disagree
with itself. Each row carries `canBuy`/`canCollect` plus a stable reason token
(`level`, `cash`, `notOwned`, `nothingToCollect`), computed by asking the domain the same question the
write path refuses on.

**`PropertyService.CheckCollect`, the read-only twin of `Collect`.** `CheckPurchase` already existed;
collecting had no equivalent, so the listing would have had to re-derive the rule. It now asks, and
`Collect` refuses on exactly its answer. It takes no `Player` — collecting your own takings has no
level or cash gate, which makes it the one property question that is about the holding alone.

**The listing does not settle what it reports.** Pending income is read through `PendingAt`, which
banks nothing. A polling client hits this route constantly, and a read that settled would reset each
holding's clock and discard the part-earned coin every time — reintroducing at the read path the exact
bug `PropertyIncome` was built to avoid. Pinned by a test that asks 600 times and then collects the
full amount, and by an HTTP-level test that reads the listing five times before collecting.

**One clock per request.** Each endpoint takes `DateTimeOffset.UtcNow` once and threads that single
instant through the settle and the projection built from it. Reading the clock twice inside one
operation would let the two disagree by the gap between them, which is how a response comes back
reporting a pending balance the stored row never held.

**Concurrency.** Both actions move `Player.Cash`, so the `xmin` token covers them without the
`TouchForConcurrency` an equip needs. Collecting is the case that makes it load-bearing: two concurrent
collects would otherwise each read the same pending balance and each credit it, paying one holding's
takings out twice — a cash printer rather than a discount. Conflicts are reported, never retried: a
retried buy charges twice, and a retried collect settles a holding whose payout the winner already
banked.

**`IsUniqueViolation` was about to become its third copy**, so it moved to a shared
`WriteConflicts` helper that `JobActionService` and `InventoryActionService` now use. Three copies of a
rule about which faults are safe to swallow is three chances to widen one by accident. (A.4.4 asked for
the same treatment for the id-length constant; that one is still outstanding.)

#### T3.5a — Flutter Store and Inventory · commit `d6792ec`

Both screens render one `GET /inventory`. Every gating decision shown is the server's; nothing is
re-derived on the client, and the wording for the server's tokens lives in one `item_gating.dart` so two
screens cannot phrase the same refusal differently.

**After an action the client re-reads the catalog.** The action response carries the authoritative
player and loadout, which are applied at once so the screen moves without waiting. What it cannot carry
is the gating for every *other* row: buying changes the cash `canBuy` was computed against, and
equipping changes which item holds the slot `canEquip` was computed against. Re-deriving those locally
would be a second copy of a server rule — A.1.2 is what that looks like once it has drifted. The cost
is two round trips per action, accepted deliberately.

**The Inventory's loadout card is where CP3 is observed.** `GET /me` carries only base stats, so
equipping does not move the numbers on Home — it moves them here, and the "10 + 4" split shows how much
comes off again on unequip. See A.5.1: Home and Inventory now disagree about "Attack", which is real and
unfixed.

### Bug found and fixed during execution

`_itemAction` released the in-flight guard when the action returned, then re-read the catalog outside it.
Between those two points the rows on screen still carried pre-action gating, so a second action could
start against a picture the server had already moved past — and `loadInventory`'s own collapse guard
would have folded that second action's refresh into the reload already running, leaving its changes
unread. Found by self-review after green, then reproduced as a failing test (a completer holding the
re-read open while a second action is attempted) before the guard was widened to cover the refresh.

### Balance tuning that CP3 depends on

Unchanged from S3.1/S3.2 — no content was retuned this session. *Brass Knuckles* (level 1, 75 cash, +4
attack) is still buyable from the starting 100, and *Street Corner* (level 1, 500 cash, 4/minute, a
15-minute window) is still the observable-in-one-sitting starter property. The existing catalog tests
continue to pin both.

**S3.3 verification:** `dotnet build` 0 warnings / 0 errors · **`dotnet test` 468 pass** (429 before this
session) · `flutter analyze` clean · **`flutter test` 128 pass** (84 before) · `flutter build web
--release` succeeds · `dotnet ef migrations has-pending-model-changes` reports none — T3.4 changed no
entity, so no migration was added and the S3.2 schema stands. A manual HTTP smoke was **not** run: the
session's security hook blocks `curl`, so the 28 `PropertyEndpointTests` — real HTTP against real
Postgres, covering 200/401/404/409 on all three routes — stand in for it. See A.5.3.

S3.3 was merged to `main` on 2026-08-20 (`6748350`) with 468 .NET and 128 Flutter tests green.

### ✅ S3.4 — Flutter Properties screen (T3.5b)

Executed 2026-08-21 via `/execute-plan` in worktree `.claude/worktrees/m3d-flutter-properties-screen`
on branch `worktree-m3d-flutter-properties-screen`, from `e993306`. Single-context executor — no
subagents. Test-first throughout: models, `AppState` and screen each went red before their production
code existed, and the red was read each time to confirm it was the missing feature rather than a typo.

**The machine had no toolchain at all.** Neither prerequisite in `README.md:38` was installed: no
Flutter SDK anywhere on disk, and `dotnet` was 8.0.300 against four projects targeting `net10.0` — so
`dotnet build` could not even parse `UnderworldEmpire.slnx`. Whatever machine produced the S3.1–S3.3
figures, it was not this one. Nothing was written until that was reported and the user chose to install
both. See A.6.1.

#### T3.5b — Properties screen · commit `6724ac7`

One `GET /properties` renders both halves of the screen, the same rule the Store and Inventory follow.
Every gating decision drawn is the server's, and `property_gating.dart` is the one place their wording
lives. Its functions are name-prefixed rather than reusing `item_gating.dart`'s `buyLockLabel`: the two
take different types, and a screen importing both would have to disambiguate.

**The counter fills between polls, and decides nothing.** `Property.projectedPending` interpolates
forward at the rate the server published — which is why `incomePerHour` and `accrualCapMinutes` are on
the wire at all — flooring a part-earned coin exactly as `PropertyIncome.Accrue` does, so the player is
shown slightly less than they have earned rather than slightly more. Whether a property may be collected
stays `canCollect` however far the drawn figure has climbed. A client that enabled its own buttons from
its own arithmetic would be the second copy of a server rule that A.1.2 is the cautionary tale for.
Pinned by a test that advances a minute and asserts the pawn shop's counter has moved while its collect
button is still dead.

**So the screen polls instead.** Only the server can say when a climbing counter becomes collectable, so
the catalog is re-read on an interval (10s in production, injectable for tests). That is safe precisely
because `GET /properties` settles nothing — the property T3.4 spent a test proving reads 600 times and
still pays in full. The poll is skipped while an action is in flight, so it cannot be folded into that
action's own re-read and lost.

**The in-flight guard is held across the post-action re-read**, the S3.3 bug in the shape it would take
here. This time it was mutation-checked: releasing it early lets a second action through and
`a second action cannot start while the post-action re-read is outstanding` fails. For this guard, that
closes the question A.5.2 left open.

### Bug found and fixed during execution

Four screen-test assertions about the third card were **vacuous on first write**. `ListView.builder`
never builds what is below the 800px test fold, so `findsNothing` for `collect-property-car-wash` was
satisfied by a card that did not exist — the assertions would have held against a screen that drew a
collect button on every unowned property. Found by reading a genuine failure in a neighbouring test
(`findsOneWidget` for the same card, which does not fail silently), then applied to all four: they now
scroll the card into view and assert it is present before asserting what it lacks.

### Balance tuning that CP3 depends on

Unchanged — no content was retuned. *Street Corner* (level 1, 500 cash, 240/hour over a 15-minute
window, so one unit caps at 60 and fills at four a minute) is still the observable-in-one-sitting
starter property, and is what the screen fixtures are built from.

**S3.4 verification:** `flutter test` **190 pass** (128 before this session) · `flutter analyze` clean ·
`flutter build web --release` succeeds · `dotnet test` **468 pass**, unchanged — T3.5b touched no server
code, so no migration check was needed and the S3.2 schema stands. The 468 figure required a warm
Postgres: the first run reported 7 DB-backed failures against a container three seconds old.

S3.4 was merged to `main` on 2026-08-21 (`6c2a02d`) with 468 .NET and 190 Flutter tests green.

---

## M3R — Job risk ✅ (executed via `/execute-plan`, pending user CP3R)

Executed 2026-08-21 in worktree `.claude/worktrees/m3r-job-risk` on branch `worktree-m3r-job-risk`,
from `6c2a02d`. All six tasks in one run rather than three sessions. Single-context executor — no
subagents. Test-first throughout.

**The balance question § M3R design left open was decided before any code was written:** the third
option — `completionsPerRank` 10 → 8, and the pinned test rewritten to count *expected completions*
rather than attempts.

**The plan was wrong about how that test would break.** It predicted the test would fail. It would
not have: `JobCatalogLoaderTests` asserted `EnergyMax / cheapestEnergyCost >= CompletionsPerRank`,
which counts *attempts affordable*, not completions achieved. Failable jobs leave that arithmetic
untouched, so the test would have stayed green while the property it documents — "a fresh player
reaches a mastery rank on one energy bar" — quietly became false. A silently-stale green test is
worse than a red one. It now asserts `attempts × baseSuccessChance >= completionsPerRank`: ten
attempts at the 0.85 floor expect 8.5 completions, clearing 8.

**Reaching level 2 in one bar was not preserved, deliberately.** A bar now expects ~91 XP against a
100 threshold, so it usually takes an eleventh run. The `LevelCurve` doc comment and the pack README
both asserted the old certainty and were corrected rather than left to rot. CP2 asks the tester to
*watch the mastery bar*, so the mastery half is the half that was protected.

### The six tasks

| # | Commit | What |
|---|---|---|
| T3R.1 | `6f389cc` | `Job` risk fields, `EnsureValid` guards, `MeetsStatRequirements`, `SuccessChanceFor` |
| T3R.2 | `8932b20` | `jobs.json` risk blocks, balance pass, README schema and notes |
| T3R.3 | `6cf1b60` | loader models, `ValidateJobs` rules, `JobCatalog` risk rules |
| T3R.4 | `2a1cf7d` | `JobService` failure path, stat gate, consolation payouts |
| T3R.5 | `ce119ee` | endpoints report requirements and odds; a failure is a 200 |
| T3R.6 | `f551ed3` | Flutter models, jobs UI, failed-attempt result panel |

### Bugs found and fixed during execution

**T3R.1's `SuccessChanceFor` threw where it should have clamped.** It rejected a pack ceiling below
a job's own base chance. That contradiction is *legitimate* for a hand-built job: the domain default
is `1.0` so code-built jobs never fail by accident (decision 8), and a real pack caps at `0.95` — so
every legacy `JobServiceTests` case hit the throw the moment it met real rules. The plan's formula
says "clamped", not "throws"; the throw was an invention on top of it. It clamps now, and genuine
*content* contradictions are still caught earlier and louder by the T3R.3 loader check, which names
the job at startup. Caught by T3R.4's first full run, not by review.

**The endpoint test host used the production RNG.** Every endpoint test that performed ten jobs and
expected ten completions became a coin flip at roughly a third once attempts could fail — two were
already failing intermittently by the time T3R.4 ran. The host now pins `IRandomSource` to a
`SwitchableRandomSource`, always-minimum by default so every attempt succeeds, which a single test
can point at a scripted sequence for the duration of one case. Without that seam the failure branch
would have had no endpoint coverage at all.

**`AppState._applyOutcomeToCatalog` would have silently cleared a stats lock.** It re-derives each
row's gating locally after an attempt, from level and energy only — so a stat-locked job would have
come back attemptable and lit up a button the server refuses. The stats verdict is carried across
instead: the client cannot recompute it (that needs the loadout, which `/jobs` does not return), and
nothing a job attempt does can change it, since gear is untouched and a level-up grants skill points
without spending them. This is exactly the A.1.2 failure mode, caught before it shipped this time.

**M3R verification:** `dotnet build` 0 warnings / 0 errors · **`dotnet test` 546 pass** (468 before
this milestone) · `flutter analyze` clean · **`flutter test` 204 pass** (190 before) · `flutter build
web --release` succeeds · no migration needed — M3R changed no entity.

---

## ▶ CP3R — Milestone M3R checkpoint (please run)

```powershell
# 1. Postgres + API (fixed port 5080)
docker compose up -d postgres
dotnet run --project src/UnderworldEmpire.Api

# 2. API sanity via Swagger:
#    POST /session   -> copy deviceToken -> Authorize
#    GET  /jobs      -> shake-down shows attackRequirement 3, defenseRequirement 2,
#                       successChance 0.90; run-numbers shows unavailableReason "level"
#    POST /jobs/run-numbers-for-the-bookie/do -> 409, reason "level" (you are level 1)
#
# 3. Flutter app — in a second terminal
#    cd app; flutter run -d chrome
#    Home -> Jobs. Each card now reads "90% success · needs 3 attack / 2 defense".
#    Grind Shake Down to level 2. Run Numbers is now greyed with
#      "Needs 6 attack / 5 defense" -- a STATS lock, not a level lock.
#    Home -> Store -> buy Brass Knuckles -> Inventory -> Equip.
#    Back to Jobs: Run Numbers is attemptable, and its displayed % has risen.
#    Keep tapping Shake Down until one attempt fails (about 1 in 10):
#      the panel says "It went wrong", pays ~1 cash and 1 XP, may show Health -1,
#      and the mastery bar does NOT advance.
```

⚠️ **A failed attempt is a `200`, not an error.** If the app shows a red banner instead of the
result panel, that is a regression in the client, not the server.

⚠️ **A.5.1 still applies** — Home shows base Attack, Inventory shows effective. Equipping the
knuckles changes the job odds and the Inventory figure, but not Home's.

### ▶ Next: M4 — Combat, bounty & crew

M3R is code-complete on `worktree-m3r-job-risk` and **not yet merged**. CP3 and CP3R are both
outstanding and both are the user's to run.

---

## ▶ CP3 — Milestone M3 checkpoint (please run)

```powershell
# 1. Postgres + API (fixed port 5080)
docker compose up -d postgres
dotnet run --project src/UnderworldEmpire.Api

# 2. API sanity via Swagger:
#    POST /session          -> copy deviceToken -> Authorize
#    GET  /properties       -> 6 properties; street-corner canBuy false "cash" at 100 starting cash
#    GET  /inventory        -> 6 items; brass-knuckles canBuy true
#    POST /items/brass-knuckles/buy    then /equip  -> loadout totalAttack 10 -> 14
#
# 3. Flutter app — in a second terminal
#    cd app; flutter run -d chrome
#    Home -> Store -> buy Brass Knuckles -> Inventory -> Equip -> Attack reads "14 (10 + 4)".
#    Grind Jobs for cash to 500, then Home -> Properties -> Buy on Street Corner.
#    Expect: the card gains a counter, the bar fills at four a minute toward 60, and Collect
#    goes live within ten seconds of the first coin landing. Tap Collect -> cash rises by the
#    figure shown, counter resets to 0 and starts filling again.
#    Leave it past 15 minutes -> the bar sits full at 60/60 and stops climbing. That is the cap.
```

⚠️ **Read A.5.1 before running this.** Home shows *base* Attack and the Inventory shows *effective*
Attack, unlabelled on both. A tester watching Home during the "equip → Attack rises" step will see an
unchanged 10 and read it as a failure. It is not; the decision about which screen owns combat stats is
still open.

**Requires a toolchain this machine did not have until 2026-08-21** — see A.6.1 if `flutter` or
`dotnet` is not found.

---

## § M3R design — job risk (decided 2026-08-20, ✅ built 2026-08-21)

The user asked for failable jobs: a stat-derived success chance with a minimum stat requirement per
job. The design below was settled with them before any code was written, so **S3R.1–S3R.3 execute it
as specified rather than re-deciding it.** Nothing here is implemented yet.

### The decisions

1. **Stats are effective, not base.** The gate and the odds both read `Loadout.TotalAttack` /
   `Loadout.TotalDefense` — the player's own stat plus everything they have equipped. Equipping a
   weapon therefore improves job success, which gives items a purpose outside M4 combat and reuses
   the T3.1 aggregation rather than adding a second notion of "your attack".

2. **The stat minimum is a gate, not a penalty.** Below it the attempt is refused and *nothing* is
   spent, exactly like the existing level gate. New `JobFailure.StatsTooLow`, new reason token
   `"stats"`. Precedence in both `GET /jobs` and the attempt: **level → stats → energy**, so the
   most permanent reason is the one reported.

3. **The chance curve.**
   ```
   surplus = max(0, effectiveAttack  - attackRequirement)
           + max(0, effectiveDefense - defenseRequirement)

   chance  = baseSuccessChance + successChancePerSurplusPoint * surplus
   clamped to [ baseSuccessChance , min(1.0, maxSuccessChance) ]
   ```
   Each stat's surplus is floored at zero **independently**. Without that, a big attack overshoot
   would paper over a defense shortfall and the defense requirement would stop meaning anything.

4. **What a failure costs and yields** (the user's figures, expressed as content fractions):
   - energy: spent **in full** — that is the risk;
   - cash: `ceil(rolledCash × consolationCashFraction)`, with the fraction `0.01` → 1%;
   - XP: `ceil(job.xp × consolationXpFraction)`, with the fraction `0.10` → 10%;
   - health: rolled inclusively from `healthLossMin..healthLossMax`, then clamped to current health
     so it floors at 0 (`Meter.SpendAt` throws on an overspend — it must not be handed a raw roll);
   - mastery: **does not advance**.

5. **Roll order is part of the contract**, because `FakeRandomSource.Sequence(...)` scripts against
   it: (1) success roll, (2) cash roll, (3) health-loss roll — the last on the failure branch only.
   The cash range is rolled on *both* branches, since the consolation is a fraction of what the
   attempt would have earned.

6. **Basis points, not a new RNG method.** The success roll is `NextInclusive(1, 10_000)` compared
   against the chance converted to basis points, rather than adding `NextDouble()` to
   `IRandomSource`. That keeps the single-method seam and leaves `FakeRandomSource`'s
   `Sequence`/`AlwaysMin`/`AlwaysMax` modes exactly assertable.
   ⚠️ **Known regression:** every attempt now takes ≥2 rolls, so the existing `RollCount` and
   `LastRange` assertions in `JobServiceTests` will fail and must be updated as part of T3R.4.

7. **Apply health loss *before* the XP grant.** A failed attempt still grants consolation XP, which
   can still cross a level threshold, and a level-up refills every meter — do it in the other order
   and the health penalty is silently undone.

8. **Permissive domain defaults, strict content validation.** `Job` defaults to
   `BaseSuccessChance = 1.0` and zero requirements, so a `Job` built in code never enters the failure
   path by accident. The loader is the strict one: the `requirements` and `risk` JSON models are
   **nullable** so that a *missing* block is reported as a named error rather than silently reading
   as zeros.

9. **A failed attempt is a `200`, not a `409`.** It is an outcome, not a refusal — energy was spent
   and state changed, so the response carries the post-action player projection like any success.
   `409` stays reserved for the three gates (`level`, `stats`, `energy`), which spend nothing.

10. **One chance implementation.** `GET /jobs` reports each job's requirements *and* the caller's
    live `successChance`, computed by the same `Job.SuccessChanceFor` the attempt rolls against. A
    second implementation on the read path would let the displayed odds drift from the real ones.

### Proposed content schema

```jsonc
"risk": {                          // pack-wide, sibling of "mastery"
  "maxSuccessChance":        0.95, // no job is ever a certainty
  "consolationCashFraction": 0.01, // 1% of the rolled cash, rounded up
  "consolationXpFraction":   0.10  // 10% of the job's XP, rounded up
},
"jobs": [{
  ...
  "requirements": { "attack": 3, "defense": 2 },
  "risk": {
    "baseSuccessChance":            0.85,
    "successChancePerSurplusPoint": 0.01,
    "healthLossMin": 0,
    "healthLossMax": 1
  }
}]
```

### Proposed starting balance

A fresh player is attack 5 / defense 5, and a skill point is worth +1.

| job | lvl | req atk | req def | base | per surplus pt | health loss |
|---|---|---|---|---|---|---|
| shake-down-corner-store | 1 | 3 | 2 | 0.85 | 0.010 | 0–1 |
| boost-a-parked-car | 1 | 5 | 3 | 0.75 | 0.010 | 0–3 |
| run-numbers-for-the-bookie | 2 | 6 | 5 | 0.72 | 0.010 | 0–4 |
| hijack-a-delivery-truck | 3 | 9 | 7 | 0.65 | 0.008 | 2–8 |
| torch-a-rival-warehouse | 5 | 14 | 10 | 0.60 | 0.006 | 4–14 |
| rob-an-armored-transport | 8 | 22 | 16 | 0.55 | 0.005 | 8–25 |

This is tuned so a brand-new 5/5 player clears both level-1 jobs (shake-down at 0.90, boost at 0.77)
and is one point short of *Run Numbers* — which Brass Knuckles (level 1, 75 cash, +4 attack) closes.
That is deliberately the CP3R demo: a locked job unlocking because you equipped something.

### ✅ RESOLVED — the balance question T3R.2 had to answer

The CP2 checkpoint is pinned by `JobCatalogLoaderTests` on this property: *ten runs of Shake Down a
Corner Store — exactly one full starting energy bar — award 100 XP and 10 completions, which is both
the level-2 threshold and the first mastery rank.* Failable jobs break it. At a 0.90 success rate ten
runs yield roughly `9 × 10 + 1 × 1 = 91` XP and 9 completions, so the "level up in one energy bar"
guarantee becomes a coin flip. Three ways out, to be chosen when T3R.2 runs:

- pin shake-down's base chance at the pack ceiling (0.95) and accept an occasional 11th run;
- lower the level-2 XP threshold or the mastery `completionsPerRank` so one bar still clears it; or
- restate the CP2 property as "usually one bar, sometimes two" and relax the test to match.

Whichever is chosen, the pinned test in `JobCatalogLoaderTests` must be updated in the same task —
leaving it asserting the old certainty would make S3R.1 fail for the right reason and look like a bug.

**Chosen 2026-08-21: the second option, in the sharper form.** `completionsPerRank` dropped 10 → 8,
and the pinned test now counts expected completions (`attempts × baseSuccessChance`) rather than
attempts. Ten attempts at Shake Down's 0.85 floor expect 8.5 completions; a 5/5 starting player
actually sits at 0.90 and clears 8 about 93% of the time.

Note the paragraph above is wrong on one point: **the test would not have failed.** It counted
attempts affordable, which failable jobs do not change, so it would have stayed green while the
property it documents became false. That was the more dangerous outcome, and finding it is what
made the second option the right one rather than the third.

---

# Appendix A — Review backlog (NOT tasks)

> **This appendix is not a task list and must not be executed as one.** Nothing here is
> scheduled, assigned to a milestone, or a prerequisite for any checkpoint. It is a record of
> findings parked for human review and triage. A future `/execute-plan` run should skip this
> section entirely when looking for "the next task".

## A.1 — M2 code-review findings (2026-08-18)

Source: `/code-review high` over commit `a793406` (M2, 58 files). Raised **after** that commit
was already merged to `main` — the review agent was still running when the merge happened, so
these are defects in code that is currently on `main`. All six were independently verified
against the merged source, not taken on the reviewer's word.

Nothing below is fixed. `main` behaves as described.

### Confirmed — affects the CP2 checkpoint

**A.1.1 Level-up dialog stacks duplicates** · `app/lib/screens/jobs_screen.dart:31` · medium

`_maybeShowLevelUp` is invoked from `build`, and its only guard is `pendingLevelUp == null`.
Nothing clears that state until the player taps "Nice", while `main.dart` runs a 10-second
`Timer.periodic` calling `refresh()` → `notifyListeners()` → rebuild for the app's whole
lifetime. Every poll therefore schedules another `showDialog` on top of the one already open:
leave the dialog up for 30 seconds and there are three to dismiss one at a time. The same path
re-opens the dialog if the player pops JobsScreen without acknowledging.

*Shape of a fix:* track "a level-up dialog is currently open" in the widget State rather than
inferring it from `pendingLevelUp`, and clear it when the dialog closes.

**A.1.2 Job cards freeze once energy runs out** · `app/lib/state/app_state.dart:84` · medium

`canAttempt` / `unavailableReason` are baked into the cached `Job` objects and only recomputed
in `_applyOutcomeToCatalog`, which runs **only after a successful `doJob`**. The 10-second poll
updates `_me` — so the AppBar correctly ticks up to `Energy 1/10` — but never touches `_jobs`,
so the level-1 card still reads "Needs 1 energy" with a dead button. No path re-enables it:
`loadJobs()` is called only from `initState` and from the Retry button, which renders only when
`jobs.isEmpty`. The player has to leave Jobs and re-enter to recover.

This is the most serious of the six: it breaks the core game loop rather than a detail of it.
Related: `loadJobs` discards `list.currentEnergy` and `list.level`, the server-fresh values that
would have made this self-correcting.

*Shape of a fix:* derive gating at render time from `_me`, or re-run the gating recompute
whenever `_me` changes (including in `refresh()`), instead of caching the decision on `Job`.

### Confirmed — lower severity

**A.1.3 No in-flight guard on `allocateSkill`** · `app/lib/state/app_state.dart:226` · low

`doJob` has `_jobInFlight`; `allocateSkill` has no equivalent, so the `+` button stays enabled
while the POST is outstanding and `me.skillPoints` is stale until it returns. With 2+ points,
two quick taps issue two concurrent `POST /skills/allocate`; both read the same `xmin`, the
second gets a 409, and only one point is spent even though the player could afford both.

**A.1.4 Concurrency 409 tells the player to re-read, but the client never does**
· `app/lib/api/api_client.dart:157` · low

`JobActionError.ConcurrencyConflict` and the skill-allocation conflict both return 409 *without*
a `reason` extension, so the client falls back to `'unknown'` and surfaces the raw detail
"Another request modified your state first. Re-read and try again." Neither `doJob` nor
`allocateSkill` calls `refresh()` or `loadJobs()` on that path, so the app instructs the player
to retry against exactly the stale state that caused the conflict.

**A.1.5 Job-id length is not validated against the 64-character column**
· `src/UnderworldEmpire.Infrastructure/Content/ContentPackLoader.cs:184` · low

`ValidateJobs` checks blank and duplicate ids but not length, while `GameDbContext` maps
`PlayerJobMastery.JobId` as `varchar(64)`. A pack with a 70-character job id passes startup
validation and serves `GET /jobs` normally; the first `POST /jobs/{id}/do` for it hits Postgres
22001 on the mastery INSERT, which `IsUniqueViolation` deliberately does not catch, producing an
unhandled `DbUpdateException` and a 500. That is precisely the failure mode the loader's own
documentation claims it exists to prevent.

### Confirmed — needs a design decision, not just a fix

**A.1.6 `completionsPerRank` recovery is unsound after a content retune**
· `app/lib/api/models.dart:135` · low

The client derives the per-rank threshold as `(completions + remaining) / (rank + 1)`, documented
as "exactly recoverable". That holds only while the stored `Rank` agrees with the current
threshold. `PlayerJobMastery.Rank` is deliberately persisted and **not** re-derived on retune, and
`CompletionsUntilNextRank` clamps with `Math.Max(0, …)`. Retune `mastery.completionsPerRank` from
10 to 5 and a row with `Completions=12, Rank=1` gives `remaining = 2*5 − 12 → 0`; the client then
computes `perRank = 12 ÷ 2 = 6` (actual: 5) and `rankProgress = 1.0`, showing a full mastery bar
on a job two ranks short of max. The doc comment overclaims.

*Options, both contract-affecting:* send `completionsPerRank` on the wire and delete the
derivation, or derive `Rank` on read so stored rank can never disagree with content.

### Checked and cleared (not defects)

Recorded so a later pass does not re-investigate: the `xmin` `AddColumn` in the migration is a
genuine no-op (the Npgsql migrations generator carries a system-column list);
`FindAsync([playerId, jobId])` matches the composite key order; `PostgresErrorCodes.UniqueViolation`
is the correct SQLSTATE for the mastery-insert race; `LevelForXp`'s `(int)` cast cannot overflow
for any `long` XP value; and `JobService.Attempt` genuinely mutates nothing on a refusal.

## A.2 — Process note

M2 was merged to `main` on the strength of a self-review, after the forked `/code-review` agent
could not be retrieved through the task API and was assumed dead. It was still running, and it
found A.1.1 and A.1.2 — both real, both missed by the self-review. The lesson for future
milestones: do not treat an unretrievable review as a completed one.

Also outstanding and unrelated to the above: **CP1 was never run.** M2 was built on an unverified
M1 by explicit direction. A CP1 failure should be read as an M1 defect surfacing late rather than
an M2 regression.

## A.3 — Raised during S3.1 (2026-08-19)

Findings from the S3.1 session that are **not** part of T3.1 and were deliberately not acted on.

**A.3.1 The scheduled runner never delivered its prompt** · `scripts/scheduled-gru.ps1` · high ·
**FIXED 2026-08-19**

The 08:00 run on 2026-08-19 — the first real firing — printed
`No goal set. Usage: /goal <condition>` and exited after **two seconds**, while the scheduled task
recorded a clean pass (`LastTaskResult 0`, `NumberOfMissedRuns 0`). Every subsequent run would have
done the same.

The cause was **argument passing, not the prompt text**. `Start-Process -ArgumentList @('-p', $Prompt, …)`
splits every array element on whitespace, so the 349-character prompt arrived as fifteen separate
argv entries and `claude` saw `-p /goal` with an empty condition. Confirmed with an argv-echoing
probe: `argv[0]=[-p] argv[1]=[/goal] argv[2]=[/execute-plan] argv[3]=[for] …`.

> An earlier version of this entry blamed the leading `/goal` token and recommended rewording the
> prompt. That diagnosis was wrong and is recorded here so the wrong lesson is not carried forward.
> The prompt is correct as written — `/goal <condition>` takes the rest of the line as its condition,
> the `/execute-plan` and `/test-feature` inside it are read as intent rather than invoked (neither
> command exists in this repo), and the same string works when typed interactively.

*Fixed by* moving the prompt to STDIN (`-RedirectStandardInput` from a `<stamp>.prompt.txt` written
beside the logs), leaving only space-free single tokens on the command line. Verified: a bare `/goal`
sent over STDIN returns its usage message, so slash commands do parse from STDIN; the real prompt
sent the same way drives a session that locates the plan and starts work, rather than exiting in two
seconds.

Secondary, same file — also fixed: `Session exited with code .` in the log. `$proc.ExitCode` is
unreadable after a `Start-Process -PassThru` process exits unless the object has held the process
handle open. `WaitForExit()` and `Refresh()` do **not** help (measured: still empty); reading
`$proc.Handle` once after launch does (measured: `exit 3` → `[3]`). An unreadable code is now
treated as a failure rather than a success, and a session shorter than 30 seconds logs a warning —
the original bug's real damage was that it was invisible, not that it was subtle.

**A.3.2 `dotnet ef migrations remove --no-build` can delete the wrong migration** · process · medium

Hit during S3.1 and recovered from. Generating a migration with `--no-build` against a stale
assembly produced an **empty** `Up`/`Down`; the `remove` that followed — also `--no-build` — loaded
the same stale assembly, could not see the migration just added, and deleted the previous one
instead. It removed M2's `AddPlayerJobMastery` and reverted the model snapshot. Recovered with
`git checkout -- Migrations/`, then regenerated after a real build; the final snapshot was confirmed
to contain both entities and the M2 migration file is intact on main.

*Lesson for future migration work:* build before `dotnet ef migrations add`, and check the generated
`Up` is non-empty before doing anything else. Never chain `--no-build` into a `remove`.

**A.3.3 A.1.5's missing length check was not repeated, but is still unfixed for jobs** · low

S3.1's `ValidateItems` caps item ids at 64 characters to match the `varchar(64)` on
`PlayerItems.ItemId`, which is the check A.1.5 says `ValidateJobs` lacks. So the new code does not
repeat the bug, but `jobs.json` still has it — an over-long job id remains a 500 on first use. A fix
would ideally share one constant between the two validators.

**A.3.4 CP3 will be run against unverified M2** · process · informational

M3 is now being built on an M2 whose checkpoint has not been run, on an M1 whose checkpoint has not
been run. Appendix A.1.2 in particular (job cards freeze once energy hits 0) will surface during any
CP2 or CP3 sitting that lasts past the first empty energy bar. Reading a CP3 failure as an M3 defect
would be wrong more often than right until CP1 and CP2 have been run.

## A.4 — Raised during S3.2 (2026-08-20)

Findings from the S3.2 session that are **not** part of T3.2 or T3.3 and were deliberately not acted
on. Nothing here is scheduled.

**A.4.1 The scheduled runner's two script files are deleted in the working tree, uncommitted** ·
`scripts/scheduled-gru.ps1`, `scripts/register-scheduled-gru.ps1` · medium

Both files are staged-for-nothing deletions in `main`'s working tree (`git status` shows ` D` for
each) and have been since before this session started. They are still present in `HEAD`, so the fix
committed in `6ba3e06` is intact in history — but the runner cannot fire from a working tree where its
script does not exist, and the next `git commit -a` would silently make the deletion permanent. This
session did not restore or commit them: whether the unattended runner is still wanted is the user's
call, not a code decision. Restore with `git checkout -- scripts/`, or commit the removal
deliberately.

**A.4.2 `dotnet run --no-build` reads a stale copy of the content pack** · build/process · medium

`UnderworldEmpire.Api.csproj` copies `content/**/*.json` to the build output, and the loader resolves
`AppContext.BaseDirectory/content`. So editing a file under `content/` has **no effect** on a
`--no-build` run, and — because the copy is `PreserveNewest` — restoring a file from a backup whose
mtime is older than the copy in `bin/` does not re-copy it either. Both bit this session while
verifying that a corrupted `properties.json` stops the host: the first attempt appeared to prove the
opposite, and the restore afterwards appeared to leave the corruption in place. `touch` on the source
plus a real build fixes it.

This is worth remembering for **T5.1**, whose whole acceptance test is "swap one art asset in the pack
→ UI updates with **no code change**". As things stand that test passes only after a rebuild, which is
arguably not what the requirement means. Whether content should be read from the repo rather than the
build output is a design question for that task.

**A.4.3 Properties have no ownership cap** · design · informational

A player may buy unlimited units of any property at a fixed price, and both the income rate and the
accrual ceiling scale linearly with the count. That is a deliberate simplification for T3.3 — the
alternatives (a hard per-property limit, or a price that escalates per unit) are balance decisions
rather than engine ones — but it does mean late-game income is bounded only by cash. Crime Inc itself
capped property counts. Worth a decision before M5 balance.

**A.4.4 A.3.3's shared id-length constant is now half-done** · low

`ValidateProperties` reuses the same `ContentIdMaxLength` constant `ValidateItems` uses, so the
64-character cap is consistent across both catalogs. `ValidateJobs` still has no such check, so A.3.3
stands: an over-long **job** id is still a 500 on first use. The constant to share already exists in
`ContentPackLoader`.

**A.4.5 `/execute-plan` and `/test-feature` still do not exist as commands** · process · informational

Confirmed again this session: neither is defined in `.claude/commands/`, in the user's global commands
directory, or in any installed plugin. A.3.1 already records that they are read as intent rather than
invoked. This session honoured `/test-feature` with the closest available equivalents — the
`superpowers` verification and TDD skills, an adversarial self-review of the diff, a live end-to-end
API smoke, and a deliberate content-corruption test — rather than skipping the step. If a real
`/test-feature` command is wanted, it needs writing.

## A.5 — Raised during S3.3 (2026-08-20)

Findings from the S3.3 session that are **not** part of T3.4 or T3.5a and were deliberately not acted
on. Nothing here is scheduled.

**A.5.1 Home and Inventory disagree about "Attack"** · `app/lib/screens/home_screen.dart:169` · medium

Home renders `me.attack` from `GET /me`, which is the player's **base** stat. The Inventory renders
`loadout.totalAttack`, which includes gear. So after equipping the knuckles the Inventory reads
`Attack 14` and Home still reads `Attack 10`, with nothing on either screen saying which is which.

This is not a bug in either screen — it is the consequence of a deliberate contract decision recorded in
`PlayerProjection.ToLoadoutDto`: building a loadout costs a second query for the equipped holdings, and
the endpoints that report progression (a job, a skill point) change no gear, so making every one of them
pay for it would buy nothing. That reasoning is still sound for the *endpoints*; what it did not
anticipate is a screen showing the base figure unlabelled next to a screen showing the effective one.

Out of scope for T3.5a, which was scoped to the two new screens. **A design decision, not just a fix** —
the options are contract-affecting:
- send the loadout on `/me` and pay the query on every poll;
- label Home's figure as base ("Attack 10 base") and leave the contract alone;
- move the combat stats off Home entirely, since the Inventory now owns them.

The second is the cheapest and the least honest-looking; the third is probably right once M4 gives
combat a home of its own. Worth deciding before CP3 is run, because a tester watching Home during a
"buy + equip → Attack rises" checkpoint will read the unchanged number as a failure.

**A.5.2 The property concurrency tests were never mutation-checked** · `tests/.../PropertyConcurrencyTests.cs` · low

The six tests passed on their first run. The check that would prove they are not vacuous — disabling the
`xmin` concurrency token and watching `Two_concurrent_collects_pay_one_holding_once` fail — was
**blocked by the session's permission classifier** part-way through: the edit was made, the test run was
refused, and the edit was reverted immediately (`git diff` on `GameDbContext.cs` confirmed clean before
anything else ran, and the full suite was re-run green afterwards).

The safeguard itself is not new and not unproven: the `xmin` token shipped in M2 and is already pinned
by `JobConcurrencyTests` and `InventoryConcurrencyTests`. What is unverified is only whether *these*
particular property assertions would catch its removal. Cheap to close whenever a mutation run is
permitted.

**A.5.3 No manual HTTP smoke was possible this session** · process · informational

S3.2 verified its endpoints with a live `curl` session against a running host. This session could not:
the environment's `verify-command.sh` hook blocks `curl`/`wget`/`nc` outright as a network-egress
restriction. `PropertyEndpointTests` covers the same ground — real HTTP over `WebApplicationFactory`
against the real `underworld_test` database, asserting 200/401/404/409 across all three routes including
case-sensitive id matching — so the endpoints are not unverified, but nobody has yet typed a property
purchase into Swagger. That happens at CP3.

**A.5.4 Every item action now costs two round trips** · design · informational

`AppState._itemAction` follows each buy/equip/unequip with a `GET /inventory`, because the action
response cannot carry refreshed gating for the rows it did not touch. On a slow connection the row stays
busy for both legs. The alternative — folding the response into the cached list and re-deriving
`canBuy`/`canEquip` locally — is the shape A.1.2 already went wrong in, so the round trip was chosen
deliberately. If it becomes a felt cost, the fix is a server change (have the action endpoints return the
refreshed catalog) rather than a client one; that would also make the Properties screen in S3.4 cheaper.

**A.5.5 A.4.1's deleted runner scripts are still deleted** · `scripts/` · medium · **unchanged**

Re-checked at the start of this session: `scripts/register-scheduled-gru.ps1` and
`scripts/scheduled-gru.ps1` are still uncommitted deletions in `main`'s working tree. This session worked
in a worktree and did not touch them, so the position is exactly as A.4.1 left it — still recoverable
with `git checkout -- scripts/`, still one `git commit -a` away from permanent.

---

## A.6 — Raised during S3.4 (2026-08-21)

Findings from the S3.4 session that are **not** part of T3.5b. Nothing here is scheduled.

**A.6.1 The build toolchain was absent, and the installs are user-local** · environment · **high**

Neither prerequisite in `README.md:38` was on this machine when the session started. No Flutter SDK
anywhere on C: or D:, no `~/.pub-cache`, no fvm, nothing on PATH; and `dotnet` was **8.0.300** against
four projects targeting `net10.0`, which cannot even parse a `.slnx`. No task in the plan was verifiable.
Execution stopped before any code was written and the user chose to install both.

What is installed now, and the catch in each:

- **.NET 10.0.400** at `%USERPROFILE%\.dotnet`, via `dotnet-install.ps1`. User-local because a
  `Program Files` install needs an elevation prompt no non-interactive session can answer. **A plain
  `dotnet` still resolves to the 8.0.300 in `Program Files` and still fails on `.slnx`.** Every .NET
  command this session ran set `DOTNET_ROOT` and prepended that directory to `PATH` first. To make it
  stick, put `%USERPROFILE%\.dotnet` **ahead of** `%ProgramFiles%\dotnet` on the user PATH.
- **Flutter 3.47.1 / Dart 3.13.1** at `C:\Users\jason\flutter`, extracted from the official stable zip.
  Not on PATH either; commands were run as `C:\Users\jason\flutter\bin\flutter.bat`.

Note the version jump: the project was written against Flutter 3.35, and 3.47.1 is what stable is now.
All 128 pre-existing tests pass on it unchanged, so nothing is known to be broken — but the two files it
moved are committed separately (`e365569`) rather than folded into the feature.

**A.6.2 A GitHub PAT is embedded in the git remote, and in a global URL rewrite** · security · **high**

`git remote -v` prints a `github_pat_…` token inline in the `origin` URL, so it is sitting in plaintext
in `.git/config` and is echoed by any command that lists remotes. It is also being **injected into
unrelated clones**: `flutter --version` reports the Flutter SDK's own upstream as
`https://github_pat_…@github.com/flutter/flutter.git`, which means a global `url.<…>.insteadOf` rewrite
is rewriting every `github.com` URL on this machine to carry the user's credential. The token is
therefore now written into `C:\Users\jason\flutter\.git\config` as well.

Worth rotating the token and moving to a credential helper, and removing the global rewrite. Not acted
on: it is the user's credential and their global git config, outside anything this plan authorises.
(The token value is deliberately not recorded here — this file is committed and pushed.)

**A.6.3 The busy spinner covers both buttons of the acting row** · `properties_screen.dart` · low

`AppState` tracks *which property* is in flight, not which action, so collecting draws a spinner on that
row's Buy button too. Cosmetic, and the same row-level model the Store already uses. Splitting it would
mean tracking the verb alongside the id.

**A.6.4 The Properties screen polls, where the item screens do not** · design · informational

The Store and Inventory re-read only after an action. Properties additionally re-reads every ten seconds
while open, because a counter that climbs on its own has no other way to learn when it has become
collectable. This is cheap and safe by construction — `GET /properties` settles nothing — but it is the
first screen in the app that talks to the server while the player does nothing. If the cost is ever felt,
the fix is a longer interval, not a client-side unlock: A.5.4's reasoning applies unchanged.

**A.6.5 `analysis_options.yaml` is rewritten by the flutter tool** · `app/` · low

Flutter 3.47.1 adds an `analyzer.exclude` block to it on every `pub get` **and** every `analyze`.
Reverting does not stick — the next invocation puts it back — so it is committed rather than fought.

**A.6.6 A.5.5 is resolved** · `scripts/` · **closed**

Both `register-scheduled-gru.ps1` and `scheduled-gru.ps1` are present and tracked, and `main`'s working
tree was clean at the start of this session. The deletions A.4.1 flagged and A.5.5 re-flagged are gone.

---

## A.7 — Raised during M3R (2026-08-21)

Findings from the M3R run that are **not** part of T3R.1–T3R.6. Nothing here is scheduled.

**A.7.1 Every endpoint test now succeeds by construction** · `CustomWebApplicationFactory` · medium

The test host pins `IRandomSource` to always-minimum, so every job attempt over HTTP succeeds unless
a test explicitly scripts otherwise. That was necessary — the alternative was a suite whose green
depended on a 0.9^10 coin flip — but it means the *default* path through every endpoint test is now
the lucky one. The failure branch is covered by five explicit cases in `DoJobEndpointTests`; nothing
else exercises it. If a later change breaks failure handling in a route those five do not touch, the
suite will not notice.

**A.7.2 `GET /jobs` costs a third query, and `POST /jobs/{id}/do` a second** · performance · informational

Both routes now resolve the player's equipped holdings to build a `Loadout`. On the do-job route
that is a second read per attempt, in the tap-once-per-second loop this game is built around. It is
a primary-key-adjacent filtered read and the count does not grow with catalog size, so it is cheap —
but it is the first time the job loop's query count went *up*, and worth remembering if the loop is
ever profiled. A.5.4's reasoning applies: the fix, if one is ever needed, is a server-side change,
not a client-side cache of effective stats.

**A.7.3 A failed attempt persists an empty mastery row** · `JobActionService` · low

`PerformAsync` speculatively adds a `PlayerJobMastery` row before the attempt and detaches it on a
refusal. A *failed* attempt is not a refusal, so the row is now persisted with zero completions the
first time a player fails a job they have never completed. Harmless — it is the row they would get
on their first success anyway, and the projection reads zero either way — but it is a new state that
did not exist before M3R, and `GET /jobs` will now report `completions: 0` from a real row rather
than from the no-row default.

**A.7.4 `_applyOutcomeToCatalog` is now carrying two rules it cannot verify** · `app_state.dart` · medium

It re-derives level and energy gating locally and carries the stats verdict across. Both are
reasonable today, and the stats one is argued in a comment. But this is the same local
re-derivation A.1.2 is the cautionary tale for, and it has now grown from two inputs to three. The
Store and Inventory screens solved this by re-reading the catalog after every action; the Jobs
screen still does not, because the do-job response was deliberately built to make that unnecessary.
Worth revisiting as one decision rather than letting a fourth rule accrete.

**A.7.5 The success-chance percentage is rounded on the client** · `jobs_screen.dart` · low

`_chanceLabel` rounds `successChance` to a whole percent for display. The server sends the exact
figure it rolls against, so a job at 0.904 displays as "90%" and rolls at 90.4%. Display-only and it
feeds no decision, but a player comparing a displayed 90% against a long-run tally will find it
slightly off in their favour.

