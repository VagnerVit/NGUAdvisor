# ProgressionAnalyzer (`Managers/ProgressionAnalyzer.cs`)

**THE CANONICAL CHAPTER ENGINE** — chapter derived from actual TITAN KILLS (+difficulty+boss),
authoritative for HUD stage, perk plan, and profile recommendation. `StageDetector.Chapter`
(boss-threshold) is retained only for its two boss-anchored consumers; the two values
intentionally diverge — never substitute one for the other (full contrast in StageDetector.md).

## Chapter logic

Sadistic → 8. Evil: T8 beaten → 7, T7 beaten → 6, else 5. Normal: T6 beaten → 4, boss ≥ 100 → 3,
≥ 58 → 2, else 1. Titan-beaten reads: T5–T12 (idx 4–11) all via the all-time
`boss{N}Kills >= 1` counter (`ZoneHelpers.TitanKills`); T1–T4 inferred from boss thresholds.

### `GoalIsKill`, not a text match on `NextGoal` (2026-09-16)

`MilestoneGoal` returns display SHORTHAND — "Kill T6 v2", "B125 → kill T7", "Reach B300". The labels
have been reworded before, and `RecommendProfile`'s own note records the last time a text match on
them went wrong. `OptimizationAdvisor.Mode()` still matched `NextGoal` for the word **"Titan"**,
which no Normal or Evil label contains, so `Mode()` returned "farm" for every run in chapters 1–7:
the push digger set (Stats/Adv/PP/**Blood**/Wandoos) and the push beard set (Stats/Adv/Wandoos) were
unreachable outside chapter 8 — the wrong loadout on exactly the runs whose point is the kill.

`Progression.GoalIsKill` is now the structured answer, set beside the label in the same switch.
Consumers ASK; nobody parses the label. A challenge block is never a kill goal.

### `titan{N}Version` is NOT progress (user-reported 2026-09-12)

`TitanBeaten` used to read `ZoneHelpers.TitanVersion(idx) >= 2` for the versioned titans. That field
is written by exactly one thing — `AdventureController.changeTitanDifficulty`, the V1–V4 buttons in
the titan's own zone — so it is the difficulty the player has **selected**, free to change at any
time and 0 until they touch it. Killing the Beast does not move it.

The blast radius was much wider than one label, because `Chapter` gates plans across the advisor:

- the chapter stuck at 3, so `ExpBalancer` handed out the post-T5 5:1 E:M ratio for the rest of the
  game (`ExpRatioTables.For` only leaves that row at chapter ≥ 4);
- **`SpendPlanner` never bought a single quirk** — every entry in `QuirkPlan` is `MinChapter` 4 or 5,
  so the whole plan was skipped and QP banked forever.

Game truth, from the decompile: `boss{N}Kills++` fires in the zone's drop handler for every one of
`bigBoss{N}V1..V4`, and nothing resets it but a new game. Per-VERSION progress is recorded elsewhere
again — see `ZoneHelpers.TitanVersionsBeaten`.

## Outputs and caching

`Detect()` cached 750 ms (called from HUD paint paths). Heavier sub-answers throttled ~10 s
separately because they RUN THE OPTIMIZER:

- **OptimalFocus**: `CurrentScore` vs `Optimize().Score` for a stage objective (`Power` ≤ Ch.4,
  else `NGUs` — both base-100, never zero-scores); reports "+X % re-optimize gear" at ≥ 8 %
  headroom. Augment/NGU focus deliberately NOT re-recommended — the allocation engine
  auto-optimizes those (BestAug / NGU targets).
- **TitanPushInReach** (the LRB gate): recommend `Normal-LRB` ONLY when the next titan objective
  (`OptimizationAdvisor.NextObjective`) is NOT killable now but projected best gear
  (`ProjectedBestGear`) reaches ≥ 70 % (`LrbReachFactor`) of its requirement. Rationale: killable
  now → 24 h cadence takes it in stride; far off → compounding beats a stalled push.
  **History**: the old rule text-matched "Titan" in the milestone label — every Normal milestone
  names a titan, so it recommended LRB essentially always (user-reported).

## Profile recommendation

In a challenge block → keep the current profile. Non-Normal → `Goal-NGU` (difficulty presets
pending). Normal: LRB when push-in-reach, `Goal-Adventure` ≤ Ch.2, else `Normal-24hr` (the
guide's daily cadence). Activity string: current challenge > lock-mode name > challenge block >
"Farming / idle". `MilestoneGoal` strings are sized to the status strip's NEXT GOAL cell.

**The preset is the FALLBACK, not the answer.** It decides which KIND of run this is (no-rebirth
push vs. cadence); `ProfileScout` then looks on disk for a file of that same kind funding more of
the plan's NGU lanes, and its name wins when it finds one — with the lane count in the reason. A tie
leaves the preset standing (ProfileScout.md). The old `PresetOnlyCaveat` constant was a stand-in for
this and is gone.
