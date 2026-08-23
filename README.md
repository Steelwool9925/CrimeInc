# Underworld Empire

A menu/stat-driven social crime-empire tap-RPG (a mechanically-faithful, original-branding
MVP inspired by the *Crime Inc.* genre). **Android + Web** from one Flutter codebase, backed by
an authoritative **ASP.NET Core** server. All game rules, RNG and timers live server-side
(= anti-cheat); the client only renders server state and sends actions.

> Only *mechanics* are cloned. Name, art and text are original / CC-licensed. Game mechanics
> aren't copyrightable; the *Crime Inc* name and Uken's assets are — none are used here.

## Architecture

```
Flutter app (Android + Web)   ── thin client: renders server state, sends actions
        │  HTTPS/JSON (REST)
        ▼
ASP.NET Core API (authoritative)  ── all game logic: regen, payouts, combat, economy
        │  EF Core                    RNG + timers server-side
        ▼
PostgreSQL                    ── all persistent state, transactional mutations
```

## Layout

| Path | What |
|------|------|
| `src/UnderworldEmpire.Api` | HTTP endpoints, auth middleware, DI (ASP.NET Core) |
| `src/UnderworldEmpire.Domain` | Entities, value objects, pure game-logic services (no I/O; unit-tested) |
| `src/UnderworldEmpire.Infrastructure` | EF Core, DbContext, migrations, content-pack loader |
| `tests/UnderworldEmpire.Tests` | xUnit unit + integration tests |
| `app/` | Flutter project (Android + Web) |
| `content/crime/` | The MVP theme pack (data-driven JSON + assets) |
| `scripts/` | Local build / test / run helpers (PowerShell) — start with `doctor.ps1` |
| `docker-compose.yml` | Local Postgres (added in S0.2) |

## Prerequisites

| Need | For | Required? |
|---|---|---|
| **.NET SDK 10.x** | API, tests | yes |
| **Flutter 3.35+** (Dart 3.9+) | app, widget tests | yes |
| **Docker** | local Postgres | yes |
| Chrome | `run-app.ps1` web run | for the web run |
| Android SDK | emulator / APK builds | optional — `./scripts/setup-android.ps1` installs it |
| Python 3 | regenerating pack art | optional |

> **You do not need to put the SDKs on PATH.** `scripts/_toolchain.ps1` locates them and the
> scripts invoke them by full path. This matters more than it sounds: a machine with .NET 8
> system-wide and .NET 10 per-user resolves `dotnet` to the 8, which cannot parse this repo's
> `.slnx` solution and fails with `MSB4068: The element <Solution> is unrecognized` — an error
> that reads like a corrupt solution file but is purely SDK resolution.

> Local Postgres is published on **host port 5433** (see `docker-compose.yml`) to avoid
> clashing with a natively-installed PostgreSQL on the default 5432. Override the connection
> string in any real environment via the `ConnectionStrings__Default` env var.

## Quick start

```powershell
# 0. Check the machine has everything, and be told how to fix what it doesn't.
#    Changes nothing; exits non-zero only on BLOCKING problems.
./scripts/doctor.ps1

# 1. Start Postgres
docker compose up -d postgres

# 2. Create the schema. REQUIRED on a fresh clone, and any time the Docker volume is
#    recreated — compose gives you a database, not a schema. Safe to re-run.
./scripts/migrate.ps1

# 3. Build + test everything (570 .NET tests, 240 Flutter tests)
./scripts/test.ps1

# 4. Run the API — Swagger at http://localhost:5080/swagger
./scripts/run-api.ps1

# 5. In a SECOND terminal, run the app (web)
./scripts/run-app.ps1
#    Android emulator instead:  ./scripts/run-app.ps1 -d <device-id>
```

**Start with `doctor.ps1`.** It reports the SDKs it found, whether Postgres is up, whether the
schema has been created, which optional targets are missing, and whether any access token is
sitting in plaintext git config.

> **Skipping step 2 produces a confusing failure.** Nothing applies migrations at startup — the
> host never silently rewrites a database it was pointed at — so an unmigrated database serves
> `GET /health` and even `GET /health/db` as **200**, then fails the first real request with
> `42P01: relation "Players" does not exist`, surfacing as a 500 on `POST /session`. The app
> calls `/session` during bootstrap, so the *frontend* is usually where you notice. If you see
> that, run `./scripts/migrate.ps1`.

### Android

Android Studio is **not** required. This installs only the pieces the project needs
(command-line tools, platform 36, build-tools 36, platform-tools — a few hundred MB):

```powershell
./scripts/setup-android.ps1                  # APK builds, physical devices
./scripts/setup-android.ps1 -WithEmulator    # also an emulator (~1.5 GB more)

cd app; flutter build apk --debug            # prove it works
flutter devices                              # then: ./scripts/run-app.ps1 -d <device>
```

> Running that script **accepts Google's Android SDK licence agreements** on your behalf —
> the same ones `flutter doctor --android-licenses` presents interactively.

Everything installs under `%LOCALAPPDATA%\Android\Sdk`, so undoing it is deleting that folder
and the `ANDROID_HOME` user variable. It needs a JDK (17 or 21); set `JAVA_HOME` if the script
says it cannot find one.

### Verifying the core loop without clicking through it

```powershell
# With the API running, in another terminal:
./scripts/smoke-core-loop.ps1
```

Drives the real HTTP surface end to end — session, the art route, a job grind, buy + equip,
and the property gate — and prints a pass/fail line per check.

## Content packs

Content is fully data-driven: `content/<pack>/pack.json` plus `jobs.json`, `properties.json`,
`items.json`, `strings.json`. The engine references abstract ids; packs supply concrete content
and art paths. **The MVP ships only the `crime` pack.** The architecture keeps other packs
possible (a separate future app can reuse the engine), but no other theme is built here.

## Status

MVP complete: milestones M0–M4 (scaffolding, foundation, jobs & progression, items & properties,
job risk, art & polish) are built and merged to `main`. The session-by-session build plan and its
manual test checkpoints (CP0–CP4) are preserved in git history at `CrimeIncMVPPlan.md` as of this
commit's parent, for anyone who wants the design rationale or balance-tuning notes.
