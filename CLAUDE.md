# CrimeInc — project instructions

## Never spawn subagents

Do all work yourself, in the current session. Never delegate any part of a task to a subagent.
See `~/.claude/CLAUDE.md` for the full rule and the list of forbidden dispatch surfaces.

Project-specific notes on this:

- **`/code-review` is off** (`skillOverrides` in `~/.claude/settings.json`). Several
  `FEATURE_PLAN_*.md` files in `.claude/plans/` name `/code-review` in their **Validation
  gate** section. Satisfy that gate by reviewing the diff yourself, inline, and recording in
  the plan's ledger that it was done inline. Do not try to reach the skill another way.
- `/execute-plan` and `/test-feature` both already forbid subagents in their own text
  (`test-feature` §7 says to do the correctness pass inline *because* `code-review` forks).
  Those rules stand; this file makes them unconditional.

## Toolchain

`dotnet` on PATH is the wrong SDK. Scripts resolve the right one via `scripts/_toolchain.ps1`
(`Initialize-Toolchain`, add `-RequireFlutter` for Flutter). Prefer the scripts:

- `./scripts/test.ps1` — build + .NET + Flutter suites
- `./scripts/run-api.ps1` — API on `http://localhost:5080`
- `./scripts/run-app.ps1` — Flutter app (Chrome by default)
- `./scripts/migrate.ps1` — EF migrations
- `./scripts/smoke-core-loop.ps1` — core-loop smoke against a running API

`pwsh` (PowerShell 7) is **not installed** — these are Windows PowerShell 5.1 scripts. Invoke
them as `& ./scripts/<name>.ps1`, not via `pwsh -File`. In the Bash tool, `pwsh` is not on PATH
at all, and piping a script through `| tail` **masks its exit code** — check exit status
separately or run it through the PowerShell tool.

Local Postgres is `docker compose up -d` (container `underworld-postgres`, host port **5433**).
~320 of the .NET tests need it; without it they are skipped and the suite reports ~249 instead
of the full 674.

## File encoding

Never use PowerShell `(Get-Content -Raw) | Set-Content` on source files here — it double-encodes
and turns every em-dash into mojibake. Use Python with explicit `encoding="utf-8"` and
`newline="\n"`, or the Edit tool.
