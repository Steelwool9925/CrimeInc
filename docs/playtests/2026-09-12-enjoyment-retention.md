# Playtest batch — first-session enjoyment & day-1 retention, 2026-09-12

**Method:** 3 player temperaments × 2 platforms (web + Android), each a fresh guest account played
with no prior knowledge until the **starting energy bar first runs dry** (the natural stopping
point a real first-time player would hit) — extended in-session if a level-up refills the bar
mid-session, since that's a real thing the game does, not an artificial extension. Findings logged
only; no fixes applied this round. Environment: local API + Postgres, web build served statically,
Android on a local emulator (`10.0.2.2:5080`), both with the crash fix from the prior bug-hunt
batch already applied.

**The three temperaments:**
- **Patient explorer** — reads everything, checks every screen before acting.
- **Impatient tapper** — skips all text, taps the fastest path to progress, never reads a toast.
- **Skeptical min-maxer** — evaluates the economy numerically, looks for the efficient play, forms
  an opinion on whether the pacing is fair.

---

## Summary — what actually drives day-1 return likelihood here

| Persona | Session length (real time) | Enjoyment read | Would they open it again? |
|---|---|---|---|
| Patient explorer | ~10 min incl. exploring Market/Profile/Crew | Mixed — good writing and a real shop to discover, undercut by a purchase that visibly did nothing until a hidden screen was found | Uncertain — depends on whether they stumble onto Inventory before giving up on gear |
| Impatient tapper | **under 15 seconds of actual input** | Snappy and responsive, but the entire first session evaporated almost instantly with zero exploration | Unlikely without an external reminder — nothing was left on screen to pull them back |
| Skeptical min-maxer | ~8 min, mostly deliberate purchases | Strong — found a real optimization (gear-up early → unlock better jobs → level up → energy refills → repeat) that rewarded analytical play | **Likely** — found genuine depth to theorycraft despite rough edges |

**The throughline:** the moment-to-moment core loop (tap job → resolve → toast → occasional
level-up) is solid and never broke for any persona. Every problem found sits in the *layer around*
that loop: what the game shows (or doesn't show) when a purchase happens, when a session ends, and
whether the numbers displayed can be trusted at face value.

**Cross-platform:** Android's visual polish (dashboard grid, toasts, level-up modal) is a step
above web's, and skipping the dev-only server-address screen via a baked config made its first
launch feel more like a real, shipped product. But Android also produced the single most damaging
finding of the whole batch — see below.

---

## The one finding that matters most

On Android, the min-maxer persona did everything right: computed the odds, saved for a weapon,
bought it, equipped it, unlocked a better-paying job exactly as designed — and then **that job's
"Do job" button stopped responding to touch, permanently, for the rest of the session.** Confirmed
across more than 5 taps, different coordinates, different toast states, and a full navigation
away-and-back. The job right next to it kept working the entire time. There is no error message,
no visual difference — the button just silently stops doing anything the moment it becomes the
*right* move.

Every other finding in this batch is about polish, feedback, or discoverability — things that make
a first session feel worse but don't stop it. This one does: it takes the exact player most likely
to stick around (the one who found the optimal play) and lets the game go dead in their hands at
the moment they're rewarded for playing well. If only one thing from either playtest batch gets
fixed first, this recommendation is it.

---

## Web findings

### Persona 1 — Patient explorer

Checked every screen before touching a job. Landed on a rocky start: **the only legal job at
level 1 failed three times in a row** (a ~4% event on a stated 65% success rate, but a real one —
worth knowing new players occasionally hit this) before the first payout landed. From there, the
session was smooth and even found unexpected depth:

- **The Market is a genuine hidden gem.** Every item has real, well-written flavor text
  ("*Second-hand from a police auction. Nobody asked why.*") — but it's not one of the five main
  nav tabs. It's a dashboard tile on Home, reachable only by scrolling *past* Crew, several
  screens down. A player who doesn't scroll that far never finds it.
- **Buying gear gives zero feedback that anything happened.** No auto-equip, no stat change, no
  "go equip this" nudge. The actual Equip control is a small, unlabeled icon button in the
  Market's own app bar that opens an "Inventory" overlay — genuinely easy to miss entirely. Until
  found, a purchase looks exactly like money vanishing for nothing, which reads as broken or
  exploitative on a first playthrough. Once found, the equip flow itself is clean (shows a clear
  "1 + 4" attack breakdown).
- **Session end is silent.** At 0 energy, Home just shows a passive "+1 in 1m 00s" timer — no
  push-notification prompt, no "come back later" framing, no suggested downtime activity. A
  player has to already know to go check the Market with their earned cash; nothing points them
  there.
- The Rap Sheet (profile) screen surfaces a **"Create Account" upsell** for converting the guest
  session to a real login — a good retention safety net (protects progress from being lost if the
  browser data clears) *if* a player happens to visit that screen.

### Persona 2 — Impatient tapper

Skipped the welcome text, skipped Home, went straight to the job button and mashed it.

- **Rapid mashing does not drop inputs.** Firing the same click 3-4 times faster than the toast
  could render still resolved every attempt correctly (energy and cash both landed exactly right
  when checked afterward). The action itself feels snappy and trustworthy under abuse — a real
  positive for a persona that hates lag.
- **The entire starting session evaporated in well under 15 seconds of input.** Ten energy, spent
  in two rapid bursts, zero exploration. This persona would blow through "session one" essentially
  instantly.
- Same silent-session-end as persona 1: nothing on screen invites a return. Combined with how fast
  the session ended, the net impression is "that was it?" rather than "I want more" — the
  opposite of what a first session should leave behind for someone who plays exactly this way.

### Persona 3 — Skeptical min-maxer

Went in doing the math: at level 1 there is exactly **one legal job — no real decision yet**, a
fair critique of the very first minutes. Computed cash/XP per energy for the options visible, then
made a deliberate play: save for **Brass Knuckles** (75 cash, +4 attack) since base 1 + 4 = exactly
the 5 attack "Boost a Parked Car" needs — turning a locked, better-paying job into a legal one
*before* waiting for a level.

- **This optimization actually works and pays off immediately** (confirmed: buying and equipping
  the weapon unlocked the job on the spot, and its cash/energy rate is nearly 50% better than the
  starter job). A satisfying, correct read of the system.
- **Leveling up fully refills energy.** Confirmed directly: a level-up mid-grind took energy from
  0 back to 10/10. This is a real, well-designed lever — efficient/skilled play is rewarded with
  more session, not capped by a hard wall. Worth calling out as good design, not a bug.
- **Found a real gating inconsistency**: once a job's *level* requirement is met, its "Do job"
  button renders as enabled even if its separate *stat* requirement (e.g. 6 attack) is not met.
  Tapping it wastes a round trip and returns a rejection banner ("Your attack or defense is too
  low for that job") — correctly refused **before** spending energy, so nothing is actually lost,
  but the button should be greyed out the same way it already is for unmet level/energy
  requirements. As-is, the UI can't be trusted at a glance to say what's actually legal.
- Same "Inventory is a hidden unlabeled icon" friction as persona 1, but this persona kept
  searching until they found it (an analytical player is more motivated to hunt for utility than
  the other two personas).
- **Verdict: most likely of the three to come back**, specifically because the economy rewarded
  genuine optimization thinking even in an otherwise-rough first session.

---

## Android findings

Same three personas, same fresh-guest methodology, run on a local emulator (app rebuilt with the
server address baked in via `--dart-define`, matching how a real install would ship — no dev
address screen at all, a cleaner first impression than web's). Automation here is ADB
tap/screenshot rather than browser tools, which incidentally made one thing very clear: **most of
the "did my click even register?" uncertainty from the web session was a rendering-lag artifact,
not dropped input.** On Android, tapping and then waiting a beat before screenshotting was
completely reliable for ordinary navigation — the earlier "flaky nav clicks" read is better
understood as *screenshots taken before the app finished repainting*, not lost taps. That reframes
several items from the web report as one root cause (delayed repaint after a state change)
rather than two.

- **The Home screen can render fully blank right after guest-account creation.** Same underlying
  class of bug as the web Properties screen: the account is created and the data is there, but the
  very first screen a new player ever sees can look frozen for a moment. Higher-stakes than the
  web instance of this bug simply because of *when* it happens — first thing, first launch. Not
  reproduced on every run, so likely timing-dependent.
- **Android's dashboard layout is visibly better than web's.** The six Home tiles (Jobs,
  Properties, Crew, Market, Territory, Rap Sheet) render as a tidy 2-column grid instead of web's
  stretched, mostly-empty full-width cards. The job-result toasts and the level-up modal both look
  cleaner here too. If this game's future belongs to one platform's polish level, it's currently
  Android's.
- **A specific, reproducible dead button**, found by the min-maxer persona: after buying and
  equipping Brass Knuckles to unlock "Boost a Parked Car," that job's own "Do job" button stopped
  responding entirely — confirmed across 5+ taps at freshly-recalculated coordinates, across
  different toast states, and even after leaving the Jobs tab and coming back. The job directly
  above it kept working the whole time. **This is the single worst thing found in this entire
  batch**: it's not a cosmetic delay, it's the exact reward for the "smart" play sitting there
  visibly unlockable and functionally dead, with zero indication anything is wrong. A player who
  did everything right gets stonewalled at the payoff.
- Rapid same-spot tapping did **not** reliably fire multiple attempts the way it did on web (only
  ~1 in 3 rapid taps landed in one burst) — flagged with lower confidence since this may be an
  artifact of how ADB injects touch events rather than a real difference from a human finger.

