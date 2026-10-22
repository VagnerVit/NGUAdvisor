# LevelPlanner (`Managers/LevelPlanner.cs`)

Level-cap manager: drives the GAME'S OWN target fields (`advancedTraining.levelTarget`,
`machine.speedTarget/multiTarget`), which allocation breakpoints already respect (a met target
drops out; ALLAT waterfill redistributes its share) and which display in the game's own AT/TM
boxes. Runs only under `AutoProfile`; turning it off thaws everything and restores the user's
snapshotted targets.

## Purpose-driven AT caps (slots 2–4, live every tick)

- **Block (2)**: stop at 99 % damage reduction. Game: `blockBonus = 0.5/(1 + f·L)` (remaining
  damage; tooltip reduction = 1 − that) → 99 % needs `f·L ≥ 49`. Reads **only**
  `advancedTrainingController.block.levelFactor` — no gear at all — so unlike the Wandoos stops below
  it keeps running through mode locks.
- **Wandoos ATs (3=E, 4=M)**: stop once Wandoos' cap-speed dump costs ≤ 1 % of max E/M. Dump cost
  = `baseTime / totalWandoosSpeed` (decomp `capAmountEnergy/Magic`); the speed carries the slot
  linearly as `(1 + f·L)`, so `AtMath.WandoosStopLevel` solves for L. Recomputed live (OS levels
  raise `baseTime` during the run).
  - **Solved against the BASE speed** (`AtMath.WandoosBaseSpeed`): live speed with this slot's
    `(1 + f·L)`, the bootup factor and the Wandoos digger divided out, times the full-boot factor
    (`bootupSpeedFactor` reaches **1.1** only once booted with `wandoosComplete`; dividing by the
    pre-boot value alone would drop that 10 %). `boot < 0.02` → unknown. Both terms were measured:
    the digger toggling moved the stop 7 283 ↔ 26 593 (×3.64, same ratio on E and M) within one AT
    HOUR on 2026-10-05, and a near-zero bootup at a fresh run's start produced the 2.8M stop of
    2026-10-03. The digger is divided out because it does not run in the marathon (owner's call),
    so the marathon's dump never sees it.
  - **Gated on the worn gear being the gear we keep** (2026-08-10): the solve reads live
    gear-derived caps and speeds, but `Tick()` runs OUTSIDE `LockManager.CanSwap()`, so with a temp
    loadout equipped (gold set, titan set, pit, ygg, cooking — or the quest gear `CanSwap()`
    deliberately lets through, hence `!CanSwap() || HasQuestLock()`) slots 3/4 were solved from the
    WRONG gear and the wrong targets PERSISTED after the loadout was restored. While a temp loadout
    is worn the stops are `long.MinValue`, which `ApplyPurpose` treats as "leave the target alone",
    and `[CapDbg]` prints `stop=tempgear`. `TempLoadoutWorn()` is `internal` because
    `NGUAdvisors.Compute` holds its lane plan on the same gate.
  - **Per segment** — `ApplyPurpose` SETS (doesn't ratchet):

    | segment | slots 3/4 target |
    |---|---|
    | NGU MARATHON, (Evil) EVIL CLIMB, AUGMENTATION | the live stop; the marathon's is remembered |
    | AT HOUR | `min(live stop, last marathon stop)`; the live stop alone while no marathon stop is known |
    | anything else | paused (−1), also through temp loadouts |

    **Why AT HOUR is bounded by the marathon** (2026-10-05, owner's call): AT HOUR is the only
    segment that feeds AT in earnest — the marathon's `CAPALLAT` is a surplus absorber after the NGU
    lanes — so pausing Wandoos ATs there meant they practically never grew and the dump held 5–20 %
    of E cap through the marathon. But AT HOUR wears AT gear, whose weaker cap inflates the live
    solve (user-caught, 2026-07-17), and AT levels cannot be un-bought. The marathon stop is solved on
    the gear the dump actually runs on, so it caps the AT HOUR target. It is a process-lifetime static
    so it survives the rebirth into the next run's AT HOUR. After a fresh inject it is unknown until
    the marathon runs once, and AT HOUR uses the live stop meanwhile (owner's call, 2026-10-05): that
    day the AT gear read an energy cap of 11.6B against the marathon's 9.94B, so the inflation the
    bound guards against did not hold, and pausing cost the whole run's Wandoos AT.
    **Why everything else is paused, not left alone** (2026-10-03, user-caught): the game keeps
    `levelTarget` across rebirths, so a stale stop kept drawing an equal `CAPALLAT` share (Wandoos
    Magic AT fed up to L90k against a live stop of ~20k).
- Return value `long.MinValue` = unknown → leave the current target alone; −1 = hold at zero
  (target 0 means UNCAPPED in the game's semantics — the reason freezes write `lvl > 0 ? lvl : −1`).

## Sufficiency freezes (marathon segment only)

- **P/T (slots 0–1)**: frozen while adventure stats beat `OptimizationAdvisor.NextObjective()`
  ×1.1 — the realistic, difficulty-capped objective (never Evil content on Normal — the T7
  overreach bug). New titan/version target automatically thaws.
  **The unknown-objective case is split, not dropped** (2026-08-10): the test used to read
  `!obj.Known || stats ≥ req×1.1`, so an objective that could not be READ meant "sufficient" and
  froze P/T — the same fail-open shape `SpendPlanner` deliberately fails closed. But
  `NextObjective()` returns `Known == false` for two situations, and only one of them justifies the
  freeze, so `!obj.Known` could not simply be deleted either. They are separable at the source, and
  `TitanObjective.LadderComplete` now says which:
  - `LadderComplete` — the whole ladder was walked and every titan+version at this difficulty is
    auto-killed. Freezing IS correct: further P/T would buy nothing, there is no fight left.
  - `!LadderComplete` — a still-un-AK'd version had no readable AK requirement
    (`TryAkRequirementFor` false), or the probe threw. Fail **closed**: keep leveling P/T, because a
    silent lookup miss read as sufficiency stalls the titan push indefinitely.
  `[CapDbg]`'s `atBasis=` names which of the three paths produced `atSufficient`, so the case is no
  longer invisible.
- **TM**: frozen while the TM holds gold AND **neither** gold sink is starving —
  `!GoldStarvedForAugs(c, 1.0) && !GoldStarvedForDiggers(c, 1.0)`; trouble in either thaws.
  **The digger half was missing until 2026-08-10** (user-caught): the freeze tested augments only, so
  whenever augments happened to be paid up the TM froze while digger upgrades were still unfunded,
  cutting the gold supply. That is wrong because gold is a *universal* input — there is a digger for
  drop chance, adventure stats, the NGUs and PP, so freezing the gold source starves far more than the
  augment ladder. `BloodPlanner.cs:344` already asked the same question both ways
  (`GoldStarvedForAugs(c, 2.0) || GoldStarvedForDiggers(c)`); this now matches it. Do not "simplify"
  the digger term back out.

All freezes snapshot the user's targets first and log via `ChallengeOverlay.Record`.

## NGU track switch (`TickNguTrack`)

Guide ch5 Evil day: Normal NGUs most of the run, EVIL NGUs the LAST N hours where
**N = 1 + T7 versions defeated** ("run Evil NGUs for the last hour", "After v1, that means 2 hours";
`RunShape.EvilNguStartSec`). Applies on `RunShape.IsEvilDay`: chapter 5 (read through
`ChallengeOverlay.Chapter()`) and a TIME-based rebirth target over 2 h — the climb to Boss 125
included, because the guide's 24 h schedule is written for that climb. The old gate (boss ≥ 125 and
N = versions with no base hour) never switched before T7. Elsewhere the profile's NGUDiff owns the
track; on the Evil day `CustomAllocation` skips the file's NGUDiff swap so the two do not fight.

**Settings › NGU track** (`SavedSettings.NguTrack`, user rule 2026-10-22) sits above both. "Advisor"
is the rule above. "Normal" / "Evil" / "Sadistic" (`PinnedNguTrack`) skips `TickNguTrack` and the
profile's NGUDiff; `CustomAllocation` writes the pin each pass through `ApplyPinnedNguTrack`, capped
at `rebirthDifficulty` — the game clamps the track there on every rebirth (decomp
`Rebirth.startNormalRebirth`/`startHardRebirth`). It replaced the `ManageNGUDiff` checkbox, which
the Evil-day switch ignored; a settings file that still carries it reads as "Advisor".

R11 note: no outer whole-Tick catch — AdvisorApply's `RunStep("Level planner")` owns the bounded
fault report; only the narrow probes have their own catches.

## Diagnostics — `[CapDbg]` (`debug.log`)

Every reason in this file used to go to `ChallengeOverlay.Record` only — the in-app feed — so
`debug.log` carried nothing at all about the caps, while all of the deciding inputs (the AK
sufficiency test, the two gold-starvation probes, the solved Block/Wandoos stop levels) are
transient. `LogCapDbg`, called at the end of `Tick()`, renders one line with all of them and follows
`[TitanGoldDbg]`'s discipline (GoldDropAdvisor.md §Diagnostics): **60 s cadence cap checked BEFORE
rendering — `Tick()` runs on every advisor tick, so this is the one channel where that genuinely
matters — then emit only when the line CHANGED**, whole body wrapped so a logging fault cannot reach
the planner.

Carries: the active segment and `marathon=`; `at=FROZEN|free` with `atSufficient=` and `atBasis=`
(which path decided it: the AK requirement × 1.1 test naming the staged titan/version, "ladder
complete", or one of the two unreadable cases that fail closed); `tm=FROZEN|free` with `goldOk=` **and both gold-starvation inputs
separately — `augStarved=` and `digStarved=`** (the digger half was missing from the decision until
2026-08-10 and its absence was invisible precisely because nothing logged it, so the two are never
collapsed into one field again); the TM's basis and both TM level/target pairs; `purpose=` plus, per
AT slot, the current level, the current target and the solved stop (`block=`, `wanE=`, `wanM=`, with
`stop` = `unknown` when the factor could not be read, `hold` for the −1 hold-at-zero case and
`tempgear` when a temp loadout means the Wandoos stop is deliberately not solved), the remembered
`marathon` stop and the solve's inputs (`cap=`, `boot=`, `digger=`), and `wanCaps=` saying how this
segment applies the Wandoos stops — `applied`, `AT HOUR min(live, marathon)`,
`AT HOUR live (no marathon stop measured yet)`, `paused (not this segment)`, or
`held (<lock> loadout worn)`.

The gold probes are re-read inside the renderer rather than threaded out of the decision, so the
`goldOk` expression keeps its exact short-circuit — the channel must not change what is decided.

```
[CapDbg] seg=NGU MARATHON marathon=True at=FROZEN atSufficient=True atBasis=stats vs T6v2 auto-kill x1.1 tm=free goldOk=False augStarved=False digStarved=True tmBase=1.42e12 tmSpeed=lvl812/tgt0 tmMulti=lvl640/tgt0 purpose=on tough=lvl1200/tgt1200 power=lvl1200/tgt1200 block=lvl300/tgt327/stop327 wanE=lvl180/tgt244/stop244 wanM=lvl175/tgt244/stop244 wanCaps=applied
```

The channel is silent while `AutoProfile` is off (the planner holds nothing then and the two
sufficiency inputs are not computed — a line there would have to invent them).
