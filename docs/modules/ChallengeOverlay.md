# ChallengeOverlay (`Managers/ChallengeOverlay.cs`)

The AUTO PROFILE engine + challenge adaptation layer. It never touches the profile FILE — it
overlays generated breakpoints and a gear objective on top of whatever profile is loaded; toggling
`AutoProfile` off resumes the timeline. Rebirth + NGU-difficulty stay profile-owned.
Runs first in `AdvisorApply.Tick` (via `RunStep("Challenge overlay")` — R11 removed its own outer
catch so the bounded reporter owns faults).

## Public surface

| Member | Purpose |
|---|---|
| `Segment` | the run-shape segment (below) — read by LevelPlanner, OptimizationAdvisor, AtHourPlanner |
| `Phase` | "push"/growth phase from boss-kill recency |
| `GearObjectiveOverride` | consulted by `ApplyGearRefresh` instead of the profile objective |
| `Bosses30` | number-growth projection: bosses covered by 30 min of number growth |
| `AutoTokens(type)` | the generated allocation token list per resource |
| `TransformPriorities(...)` | challenge-aware rewrite of profile priorities |
| `Record(cat, action, reason)` | the `Feed` the Challenges/Advisors tab renders (newest-first, capped) |
| `Block()` | the challenge-block plan (completions from CONTROLLERS, see below) |

`Bosses30` is sampled from `nextAttackMulti` — number-derived, so it is **immune to our own gear
swaps** (a stat-based sample would read a loadout change as number growth).

## SEGMENT — the guide's 24 h shape, TIME-ANCHORED BY LAW

```
EVIL CLIMB   → Ch.5 and boss < 125 (Evil re-climb; TM/AT/Wandoos are re-locked, Number reset)
AUGMENTATION → Ch.5, boss ≥ 125, TM unlocked, 0.5 h ≤ run < 3 h (guide ch5 phase 2)
TM HOUR      → TM unlocked AND (TM gold empty OR run < 1 h)
AT HOUR      → AT unlocked AND run < AtHourPlanner.EndSec(...)   (extendable to the 5 h mark)
RECOVERY     → number still cheap (Bosses30 ≥ 2) AND run < 4 h
NGU MARATHON → everything after (the guide's 22 h) — its start is never delayed
```

**Why time-anchored**: RECOVERY once held for 5 hours because kill-recency kept "push" alive —
past the boss ceiling bosses die continuously and would hold the run hostage forever. The wall
clock owns the shape; the number rule only gets a bounded window. Evil/Sadistic re-lock the Time
Machine, so early-Evil runs skip TM HOUR and open on RECOVERY/MARATHON until it re-unlocks.

`SegmentGear()` → the gear objective per segment: EVIL CLIMB/RECOVERY "Adventure", AUGMENTATION
"Augments", TM HOUR "Time Machine", **AT HOUR "Advanced Training"** (user-compared vs the GO site:
"Adventure" here wore Power/Toughness gear instead of AT-speed), NGU MARATHON "NGUs".

**Wandoos ceiling is `CAPWAN:30` in EVERY segment** (was `:60` in NGU MARATHON, `:40` elsewhere —
user-reported: with `AutoProfile` on, Wandoos held 40–60 % of both caps every pass, the black hole
AllocationProfiles.md §WAN documents and `Normal-LRB` had already been fixed for). `WandoosBP` also
retires its own lane now (`WandoosAdvisor.DumpWorthwhile`), so `:30` is a ceiling, not a floor. The
`Templates` (NONGU/NOTM/NOAUG) and `Fallback` lists deliberately KEEP their original `:40`/`:60` —
those are the cases where Wandoos genuinely is the power source.

## NGU candidates (D2)

`ChapterNguIds`: ch.1 none (pre-NGU), ch.2 E{0,1}/M{0,3}, ch.3 E{4,6}/M{0}, **ch.4+ EVERY NGU**
(user rule 2026-07-11 — the old ch.4 group lists excluded E7 Magic / E8 PP / M5 Energy /
M6 Adventure-β outright so they never ran). `NGUAdvisors` then value-ranks which lanes actually
get resources.

**Surplus lanes are emitted as `CAPNGU-*`, not `NGU-*`** — a CAP token stays OUT of the
equal-share divisor and only drinks what's left when its turn comes, so the hot lanes' shares are
untouched (allocation walks tokens in order, recomputing idle/prioCount per non-cap token).

## Challenge adaptation

- **Stripping**: tokens for systems a challenge kills are dropped (BestAug refuses NOAUG,
  TimeMachineBP refuses NOTM, NGUBP dies with the disabled NGU button).
- **Re-weighting templates** (`Templates`): stripping alone leaves SURVIVORS holding the dead
  systems' shares (a 70 %-NGU profile in NONGU floods basic training), so per-challenge templates
  re-weight the remaining priorities. `_fallbackOn`/`_templateOn` narrate each injection ONCE per
  state change, not per tick.
- **LSC**: `SetLscAugTargets`/`RestoreLscAugTargets` inject sword-first aug targets; the target
  comes from the CONTROLLER's `laserSwordTarget()`, never
  `challenges.laserSwordChallenge.curCompletions + 2` (that field is the normal-difficulty
  counter — same trap documented in LscAdvisor.md).
- `Block()` reads completions from `Character.allChallenges` CONTROLLERS, never the serialized
  `Character.challenges` objects (`maxCompletions` there is `[NonSerialized]` and never assigned).

`AllocationStatus(type)` / `AutoStatus()` are the one-line HUD readouts.
