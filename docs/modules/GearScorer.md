# GearScorer (`Managers/GearScorer.cs`)

Phase 1 of the native gear optimizer: a faithful C# port of the reference scoring math —
`score_vals` / `get_raw_vals` from `external/gear-optimizer/src/util.js`. Pure and
game-independent: no Unity/game references, so it can be unit-tested and validated against the
website as an oracle.

## Model

- An **Item** is a `Dictionary<string, double>` of stat-name → bonus value, plus `IsWeapon`.
- An **objective** is a list of stat names + optional exponents (see `GearObjectives`).
- **Score** = product over stats of `(rawTotal / 100) ^ exponent`. Higher is better.

## Invariants (must match the JS oracle exactly)

1. **Base values**: `Respawn`, `Power`, `Toughness` accumulate from 0 (`BaseZero`); every other
   stat starts at 100 (it is a percentage multiplier). Exposed via `BaseValue(stat)` so
   `GearOptimizer`'s inline scorer starts from the same baseline.
2. **Offhand rule**: items are scored in slot order, weapons first. The FIRST weapon encountered
   is the mainhand; every later weapon's stat contribution is scaled by `offhandPercent / 100`.
   The mainhand flag flips on the first weapon **even if it doesn't carry the scored stat**
   (matches JS, where every item carries every stat as 0).
3. Missing stats contribute 0; `NaN` values are skipped.
4. **`CapValue(stat)` clamps the raw total before scoring.** Only `Respawn` has a cap (80). This is a
   GAME threshold, not a scoring preference — see below — and it is a deliberate divergence from the
   JS oracle, which has none.

## Thresholds are modelled as thresholds, not as exponents

The game floors the respawn factor at **0.2** (decomp `AdventureController.respawnTime` and
`NGUController.respawnBonus`, both `factor = 1 − bonuses[Respawn]` clamped up to `0.2`), so a gear
Respawn total past **80 %** buys nothing. `GameGearAdapter` feeds displayed percents
(`getBonusFactor(...) × 100`), so the threshold is the literal number 80.

Scoring it linearly told the search that the 81st point was worth as much as the first, and it paid
real accessory slots for it. The clamp is applied in `GetRawVals` (before `ScoreVals`) and mirrored
in `GearOptimizer.ScoreContext.ScoreOf` (before the exponent) — **both paths or neither**.

The cap belongs to the STAT, not to an objective: a per-objective knob would let one objective price
respawn above the game's own ceiling. It is also **not** the site's `hardcap`, which clamps relative
to the nude total; this one is absolute, because the game's floor is.

Pinned by `GearScorerTests` (`Respawn_total_is_capped_at_the_games_floor`,
`Respawn_below_the_cap_still_scores_linearly`, `Other_stats_are_not_capped`).

## Consumers

- `GearOptimizer` — inner-loop scoring is a dense-array rewrite of `GetRawVals + ScoreVals`, in the
  nested `GearOptimizer.ScoreContext` (see the equivalence comment above that class).
  **Semantics changes here must be mirrored there.**
- `GearOptimizerDiagnostic` — scores the current loadout per objective for site comparison.
- `GearOptimizer.CurrentScore` — "how good is my gear now" vs `Optimize().Score`.

## Known deviation from the site

The site's final score applies `hardcap` (`util.js`) — clamping each stat multiplier to
`100 * max(1, hardcap/nudeTotal)` using `capstats`. Native scoring is raw-only (the site's
`score_raw_equip`). Deferred deliberately; see gear-optimizer-comparison.md §Gaps.

## Reference counterpart

`external/gear-optimizer/src/util.js`: `get_raw_vals` (lines ~132–170), `score_vals` (~123–130),
`hardcap` (~172–185), `score_equip`/`score_raw_equip`.
