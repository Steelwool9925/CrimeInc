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

## M3 — Items & properties (in progress)

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
with two items in one slot. Silently summing both would hand the player double the gear bonus. The
state is unreachable through the API; the throw exists so corruption is loud rather than profitable.

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

**A.3.1 The scheduled runner's prompt is a no-op** · `scripts/scheduled-gru.ps1:66` · high

The standing prompt begins `' /goal /execute-plan for the next task…'`. Headless Claude Code parses
the leading token as the `/goal` slash command, finds `/execute-plan …` is not a valid goal
condition, prints `No goal set. Usage: /goal <condition>` and exits. The 08:00 run on 2026-08-19 —
the first real firing — did exactly that: the scheduled task reported success (`LastTaskResult 0`,
`NumberOfMissedRuns 0`) after a **two-second** session that did nothing. Every future run will
behave identically. The task itself, its triggers and all three guards are fine; only the prompt
string is wrong.

*Shape of a fix:* lead with prose rather than a slash command, or lead with `/execute-plan` if that
is the command meant to drive the run.

Secondary, same file: `Session exited with code .` in the log. `$proc.ExitCode` is read after
`WaitForExit(ms)` without a `Refresh()`, so it comes back empty and the runner's own exit code is
unreliable.

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
