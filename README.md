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
| `scripts/` | Local build / test / run helpers (PowerShell) |
| `docker-compose.yml` | Local Postgres (added in S0.2) |

## Prerequisites

- .NET SDK 10.x
- Flutter 3.35+ (Dart 3.9+)
- Docker (for local Postgres, from S0.2)

> Local Postgres is published on **host port 5433** (see `docker-compose.yml`) to avoid
> clashing with a natively-installed PostgreSQL on the default 5432. Override the connection
> string in any real environment via the `ConnectionStrings__Default` env var.

## Quick start

```powershell
# Build + test everything
./scripts/test.ps1

# Run the API (Swagger at /swagger once S0.2 lands)
./scripts/run-api.ps1

# Run the Flutter app (web)
./scripts/run-app.ps1
```

## Content packs

Content is fully data-driven: `content/<pack>/pack.json` plus `jobs.json`, `properties.json`,
`items.json`, `strings.json`. The engine references abstract ids; packs supply concrete content
and art paths. **The MVP ships only the `crime` pack.** The architecture keeps other packs
possible (a separate future app can reuse the engine), but no other theme is built here.

## Status

Built session-by-session against [`CrimeIncMVPPlan.md`](./CrimeIncMVPPlan.md). See that plan for
milestones (M0–M5), tasks, and manual test checkpoints (CP0–CP5).
