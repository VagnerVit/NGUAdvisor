# LevelPlanner (`Managers/LevelPlanner.cs`)

Level-cap manager: drives the GAME'S OWN target fields (`advancedTraining.levelTarget`,
`machine.speedTarget/multiTarget`), which allocation breakpoints already respect (a met target
drops out; ALLAT waterfill redistributes its share) and which display in the game's own AT/TM
boxes. Runs only under `AutoProfile`; turning it off thaws everything and restores the user's
snapshotted targets.

## Purpose-driven AT caps (slots 2–4, live every tick)

- **Block (2)**: stop at 99 % damage reduction. Game: `blockBonus = 0.5/(1 + f·L)` (remaining
  damage; tooltip reduction = 1 − that) → 99 % needs `f·L ≥ 49`.
- **Wandoos ATs (3=E, 4=M)**: stop once Wandoos' cap-speed dump costs ≤ 1 % of max E/M. Dump cost
  = `baseTime / totalWandoosSpeed`; solve `(1 + f·L)` from `baseTime / (0.01 × cap × speedOther)`.
  Recomputed live (OS levels raise baseTime during the run). **Segment-gated**: NGU MARATHON +
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
- **TM**: frozen while the TM holds gold AND augments are affordable
  (`GoldStarvedForAugs(c, 1.0)`); gold trouble thaws.

All freezes snapshot the user's targets first and log via `ChallengeOverlay.Record`.

## NGU track switch (`TickNguTrack`)

Guide ch5 24 h structure: Normal NGUs most of the run, EVIL NGUs the LAST N hours where
**N = T7 versions defeated** (1 h post-T7v1, 2 h post-v2 …). Applies ONLY in the Ch.5 24 h shape:
`StageDetector.Chapter == 5` (the boss-anchored chapter — this is one of its two sanctioned
consumers), boss ≥ 125, TIME-based rebirth target set. Elsewhere the profile's NGUDiff owns the
track. Flagged UNTESTED until Boss 125+ (T7-version read is a first cut).

R11 note: no outer whole-Tick catch — AdvisorApply's `RunStep("Level planner")` owns the bounded
fault report; only the narrow probes have their own catches.
