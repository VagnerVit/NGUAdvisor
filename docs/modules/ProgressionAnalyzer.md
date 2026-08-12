# ProgressionAnalyzer (`Managers/ProgressionAnalyzer.cs`)

**THE CANONICAL CHAPTER ENGINE** — chapter derived from actual TITAN KILLS (+difficulty+boss),
authoritative for HUD stage, perk plan, and profile recommendation. `StageDetector.Chapter`
(boss-threshold) is retained only for its two boss-anchored consumers; the two values
intentionally diverge — never substitute one for the other (full contrast in StageDetector.md).

## Chapter logic

Sadistic → 8. Evil: T8 beaten → 7, T7 beaten → 6, else 5. Normal: T6 beaten → 4, boss ≥ 100 → 3,
≥ 58 → 2, else 1. Titan-beaten reads: versioned titans (idx 5–11) beaten iff
`ZoneHelpers.TitanVersion(idx) >= 2` (TitanVersion is version+1); T5 via `boss5Kills >= 1`;
T1–T4 inferred from boss thresholds.

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
