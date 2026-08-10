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
  = `baseTime / totalWandoosSpeed`; solve `(1 + f·L)` from `baseTime / (0.01 × cap × speedOther)`.
  Recomputed live (OS levels raise baseTime during the run). **Gated on the worn gear being the gear
  we keep** (2026-08-10): the solve reads live gear-derived caps and speeds, but `Tick()` runs
  OUTSIDE `LockManager.CanSwap()`, so with a temp loadout equipped (gold set, titan set, pit, ygg,
  cooking — or the quest gear `CanSwap()` deliberately lets through, hence
  `!CanSwap() || HasQuestLock()`) slots 3/4 were solved from the WRONG gear and the wrong targets
  PERSISTED after the loadout was restored. While a temp loadout is worn the two `ApplyPurpose` calls
  are skipped entirely — "leave the target alone" is already the contract for no answer
  (`long.MinValue`) — and `[CapDbg]` reports `wanCaps=held (<lock> loadout worn)` with
  `stop=tempgear` rather than printing a number solved from gear we are not keeping. Do not "restore"
  the ungated calls: they look harmless because they self-correct next tick, but only *after* the
  restore, and the marathon spends AT against them meanwhile. **Segment-gated**: NGU MARATHON +
  (Evil) EVIL CLIMB / AUGMENTATION — NOT AT HOUR (its weaker caps inflate targets and steal AT
  from P/T — user-caught). Extending to the Evil climb fixed a stale −1: the marathon never runs
  during the climb, so Wandoos ATs kept the Normal-era pause and never boosted E/M Wandoos
  (user-caught 2026-07-17). `ApplyPurpose` SETS (doesn't ratchet) — overshoot self-corrects.
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

Guide ch5 24 h structure: Normal NGUs most of the run, EVIL NGUs the LAST N hours where
**N = T7 versions defeated** (1 h post-T7v1, 2 h post-v2 …). Applies ONLY in the Ch.5 24 h shape:
`StageDetector.Chapter == 5` (the boss-anchored chapter — this is one of its two sanctioned
consumers), boss ≥ 125, TIME-based rebirth target set. Elsewhere the profile's NGUDiff owns the
track. Flagged UNTESTED until Boss 125+ (T7-version read is a first cut).

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
`tempgear` when a temp loadout means the Wandoos stop is deliberately not solved) and `wanCaps=`
saying whether this segment applies the Wandoos stops at all — `applied`, `not this segment`, or
`held (<lock> loadout worn)`.

The gold probes are re-read inside the renderer rather than threaded out of the decision, so the
`goldOk` expression keeps its exact short-circuit — the channel must not change what is decided.

```
[CapDbg] seg=NGU MARATHON marathon=True at=FROZEN atSufficient=True atBasis=stats vs T6v2 auto-kill x1.1 tm=free goldOk=False augStarved=False digStarved=True tmBase=1.42e12 tmSpeed=lvl812/tgt0 tmMulti=lvl640/tgt0 purpose=on tough=lvl1200/tgt1200 power=lvl1200/tgt1200 block=lvl300/tgt327/stop327 wanE=lvl180/tgt244/stop244 wanM=lvl175/tgt244/stop244 wanCaps=applied
```

The channel is silent while `AutoProfile` is off (the planner holds nothing then and the two
sufficiency inputs are not computed — a line there would have to invent them).
