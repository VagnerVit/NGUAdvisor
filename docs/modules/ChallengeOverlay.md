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

The pick is the pure `RunShape.Segment` (Unity-free, `RunShapeTests`); `UpdateSegment` only reads
the live inputs. The rebirth target is `Main.Profile.NextRebirthTargetSeconds()` — rebirth stays
profile-owned, so the profile's cadence decides which Evil shape runs.

```
Ch.5 (Evil)
EVIL CLIMB   → time target ≤ 2 h (the guide's 30-min rebirths) OR TM still re-locked (not NOTM)
TM HOUR      → run < 0.5 h and TM usable (NOTM goes straight to AUGMENTATION)
AUGMENTATION → 0.5 h ≤ run < 3 h
NGU MARATHON → after 3 h (Normal NGUs + AT ≤ 40 %; LevelPlanner switches to Evil NGUs for the tail)

Other chapters
TM HOUR      → TM unlocked AND (TM gold empty OR run < 1 h)
AT HOUR      → AT unlocked AND run < AtHourPlanner.EndSec(...)   (extendable to the 5 h mark)
RECOVERY     → number still cheap AND run < 4 h  (enter Bosses30 ≥ 2.5, leave < 1.5, stay ≥ 15 min)
NGU MARATHON → everything after (the guide's 22 h) — its start is never delayed
```

**Why time-anchored**: RECOVERY once held for 5 hours because kill-recency kept "push" alive —
past the boss ceiling bosses die continuously and would hold the run hostage forever. The wall
clock owns the shape; the number rule only gets a bounded window.

**Ch.5 is cadence-driven, not boss-gated** (user-reported 2026-10-16, "it doesn't handle Evil"). The
guide's "24HR RB Schedule" is written for the climb TO Boss 125 ("while you're climbing up to Boss
125"), after the 30-minute rebirths stop at Boss 60–80. The old gate ran EVIL CLIMB for every run
below Boss 125, so a 24 h profile at Boss 70 got a 24-hour climb: no TM half-hour, no augment phase,
no Evil NGU tail. AT HOUR and RECOVERY are Normal-shape segments and do not exist on the Evil day.

**Ch.5 NGU MARATHON energy**: `CAPALLAT` ahead of the NGU lanes while pre-T7 at Boss 115+ (guide: "BB
AT P/T for the entire rebirth to prep for T7"); otherwise the hot NGU lanes first, then `CAPALLAT:40`
("run AT with <40% of your Energy cap. Prioritize NGUs first"), then the surplus absorbers.

**AUGMENTATION's NGU tail is `CAPNGU-*`**: a plain `NGU-*` after `BESTAUG` joins the equal-share
divisor and halves the augment (the TM HOUR rule below), and in magic it halves `BR-30`. The guide
runs the best augment and puts magic into blood for the whole phase.

`SegmentGear()` → the gear objective per segment: EVIL CLIMB/RECOVERY "Adventure", AUGMENTATION
"Augments", TM HOUR "Time Machine", **AT HOUR "Advanced Training"** (user-compared vs the GO site:
"Adventure" here wore Power/Toughness gear instead of AT-speed), NGU MARATHON "NGUs".

**Wandoos ceiling is `CAPWAN:30` in EVERY segment** (was `:60` in NGU MARATHON, `:40` elsewhere —
user-reported: with `AutoProfile` on, Wandoos held 40–60 % of both caps every pass, the black hole
AllocationProfiles.md §WAN documents and `Normal-LRB` had already been fixed for). `WandoosBP` also
retires its own lane now (`WandoosAdvisor.DumpWorthwhile`), so `:30` is a ceiling, not a floor. The
`Templates` (NONGU/NOTM/NOAUG) and `Fallback` lists deliberately KEEP their original `:40`/`:60` —
those are the cases where Wandoos genuinely is the power source.

**`CAPALLBT` leads the energy list in EVERY segment** (user-reported 2026-09-29: the auto profile
neglected Basic Training). BT caps are fixed within a run (decomp `Training` — they only shrink at
rebirth), so the old "BT energy persists once capped" held for tiers capped at hour 0, but tiers that
unlock later (previous tier at 5000×id) and caps left partial by a short hour-0 pool were never
topped up once AT HOUR / NGU MARATHON took over.

**RECOVERY has hysteresis and keeps its input through gear swaps.** A single 2.0 bar flapped the
segment (2026-10-07: MARATHON 2.0 h → RECOVERY 2.04 h → MARATHON 2.17 h), and every flip swaps gear
and allocation. The projection window restarts when `nextAttackMulti` halves — a mid-run gear swap,
since a rebirth is caught by the 600 s gate — and now keeps the last `Bosses30` instead of going NaN.

**RECOVERY's AT is `CAPALLAT:40`, a group cap.** As a plain `CAPALLAT` ahead of the NGU lanes it took
everything it could use and left them nothing. Guide ch.5: "run AT with <40% of your Energy cap.
Prioritize NGUs first". EVIL CLIMB keeps the uncapped group: past boss 115 the guide BBs AT P/T.

**TM HOUR magic ends on the NGU lanes; TM HOUR energy does not.** Magic's TM HOUR tokens (`CAPTM`,
`CAPWAN`, `BR`) all stop at a 1-per-tick cap, so without an NGU tail 95 % of the magic cap sat idle
for the whole first hour (save 2026-10-08: 26.5B of 28B). Energy keeps `BESTAUG` as its sink: a
non-cap `NGU-*` after it would join the equal-share divisor and halve the augment's share.

## NGU candidates (D2)

`ChapterNguIds`: ch.1 none (pre-NGU), ch.2 E{0,1}/M{0,3}, ch.3 E{4,6}/M{0}, **ch.4+ EVERY NGU**
(user rule 2026-07-11 — the old ch.4 group lists excluded E7 Magic / E8 PP / M5 Energy /
M6 Adventure-β outright so they never ran). `NGUAdvisors` then value-ranks which lanes actually
get resources.

**Surplus lanes are emitted as `CAPNGU-*`, not `NGU-*`** — a CAP token stays OUT of the
equal-share divisor and only drinks what's left when its turn comes, so the hot lanes' shares are
untouched (allocation walks tokens in order, recomputing idle/prioCount per non-cap token).

**The `→ auto profile` feed line reports the lane DIFF, not the status, once the context is stable.**
`AutoGenerated` keys the parsed list on `segment|chapter|type|tokens`. A changed segment or chapter (or
the first generation, or after `_lastGenKey` is cleared) logs `AutoStatus()` as before; a change in the
tokens alone logs `+NGU-4, −NGU-1`, and a pure REORDER of the same set (the plan sorts lanes by rating,
which wobbles) logs nothing. Reported 2026-09-29: advisor.log 15:49–16:46 carried ~60 near-identical
"AT HOUR · … number ≈ 0,5 boss/30m" lines, one per 30–60 s, because every lane flip re-logged the
whole status. The flips themselves were the NGU plan following worn temp loadouts (NGUAdvisors.md).

## Challenge adaptation

**WITH `AutoProfile` THE AUTO PROFILE ALLOCATES INSIDE CHALLENGES TOO** (user rule 2026-10-16:
"allocation in challenges too"). `TransformPriorities` takes `AutoGenerated` as the base for every
challenge except 100LC (a 100-level budget, decomp `Character.canLevel`; the file keeps it); the
challenge's dead systems drop through `IsValid` like any locked one, and `AdvisorChallenges` gates only
the transforms below (templates, LSC sword, fallback). The "N/M active" line is skipped for a generated
list — `AutoGenerated` already narrates its diffs. Gear follows `SegmentGear()` in the challenges that
disable nothing a segment's gear serves (`GearFollowsSegment`: 24HR, NORB, BLIND, TC); BASIC keeps
push/growth, because it completes on a boss number while its systems are still re-locked.

**WITHOUT `AutoProfile` THE PROFILE FILE WINS INSIDE CHALLENGES** (user rule 2026-08-22, reported as
"equipment and energy are not guided by the profile file"). The overlay adapts what a profile leaves
UNSAID; it does not overrule what a profile says. Two gates enforce it, and both are the fix for one
report:

- **Gear rotation stands down on `GearBreakpoints.ProfileOwnsGear`** — an `ID` list, a `Priorities`
  chain, or an `Objective` in the active gear breakpoint. `Tick` clears `GearObjectiveOverride`
  (narrating it once) instead of rotating Adventure/NGUs, so `ApplyGearRefresh` falls through to the
  profile. The flag is NOT derivable from `ActiveChain`: after `GearBreakpoints`' smart default folds
  a challenge objective in, that chain is the ADVISOR's — see AllocationProfiles.md §GearBreakpoints,
  where the same report also stopped the smart default from outranking an explicit `ID` list.
- **The rotation itself is push → "Adventure", growth → "NGUs", except in LSC**: LSC completes on
  the laser sword alone and does not reset the Number, so bosses fall continuously and "push" would
  hold Adventure gear for the whole challenge (user-reported 2026-10-04). LSC wears "Augments", and
  "Adventure" only while the controller's `augLocked()`/`upgradeLocked()` say the boss has not yet
  opened the sword (the boss requirements live in the Unity scene, not in code).
- **`GearFollowsSegment` challenges wear `SegmentGear()`, not push/growth**: 24HR is the ordinary
  24 h rebirth, and its boss re-climb keeps kill recency alive, so "push" held the pure
  Power/Toughness "Adventure" set through TM HOUR (user-reported 2026-10-14: A Giant Apple worn during
  TM HOUR). With no segment yet (`SegmentGear()` null) it falls back to push/growth.
- **Template takeover is generated-list-only** (below).

- **Stripping**: tokens for systems a challenge kills are dropped (BestAug refuses NOAUG,
  TimeMachineBP refuses NOTM, NGUBP dies with the disabled NGU button).
- **Re-weighting templates** (`Templates`): stripping alone leaves SURVIVORS holding the dead
  systems' shares (a 70 %-NGU profile in NONGU floods basic training), so per-challenge templates
  re-weight the remaining priorities. `_fallbackOn`/`_templateOn` narrate each injection ONCE per
  state change, not per tick. **The `gutted` (≥ half inactive) trigger requires a generated list** (`autoBase`) —
  reshaping a GENERATED list is the overlay's job, reshaping a hand-written one is not: CBlock1's
  6-token energy list was replaced wholesale by the strip template, whose `ALLNGU` lane then dumped
  the cap into NGUs, a system that profile never named. A manual profile keeps its surviving
  priorities; only `valid.Count == 0` still falls through to the template/`Fallback`, where the
  alternative is an idle cap rather than a profile being overruled.
- **LSC**: `SetLscAugTargets`/`RestoreLscAugTargets` inject sword-first aug targets; the target
  comes from the CONTROLLER's `laserSwordTarget()`, never
  `challenges.laserSwordChallenge.curCompletions + 2` (that field is the normal-difficulty
  counter — same trap documented in LscAdvisor.md). **The sword goes AFTER the base list's leading
  setup caps** (BT / TM / Wandoos, `IsLscSetupCap`), not at the head: LSC disables nothing, so it is a
  normal run first (user rule 2026-09-29); only augs, AT and NGUs wait for the sword to reach target.
- `Block()` reads completions from `Character.allChallenges` CONTROLLERS, never the serialized
  `Character.challenges` objects (`maxCompletions` there is `[NonSerialized]` and never assigned).

`AllocationStatus(type)` / `AutoStatus()` are the one-line HUD readouts.
