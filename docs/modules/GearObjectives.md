# GearObjectives (`Managers/GearObjectives.cs`)

Phase 1b: the scoring vocabulary — stat names, the game's `specType` → stat mapping, and the
selectable objective presets. Pure data, no game references.

## Contents

- **`Stat`** — string constants for every scoreable stat. Names are IDENTICAL to the reference's
  `Stat` map (`external/gear-optimizer/src/assets/ItemAux.js` line ~152) — e.g. `"Raw NGU Speed"`,
  `"Yggdrasil Yield"`. Keep them in sync; `GearScorer.Item.Stats` is keyed by these strings.
- **`SpecTypeToStats`** — game `specType` enum int → stat name(s). Tiered specs (…2, …3 variants)
  map to the same stat; composite specs (27 AllPower, 28 AllPerBar, 29 AllCap) map to multiple
  stats. specTypes 0 (None), 10 (BoostRecycle), 46 (Blood) are not scored.
- **`Objectives`** — named presets: stat list + optional exponents (null = all 1.0). Consumed by
  `GearOptimizer.FindObjective` (name match is what profiles/settings store — renaming an
  objective breaks saved configs).

**Objective CHAINS are not here.** Ordered multi-objective presets live in `GearChain.Presets`
(GearChain.md) — deliberately, because `GearOptimizerDiagnostic` iterates `Objectives` and optimizes
every entry as the optimizer's regression harness; a chain in this list would change its baseline.
`GearChain.Resolve(name)` tries a preset first and then an objective, so **a chain name and an
objective name share ONE namespace**: a preset must never be named after an objective (guarded by
`GearChainTests.PresetNamesDoNotCollideWithObjectiveNames`), and the never-rename rule below applies
to chain names exactly as it does to objective names.

## Divergences from the reference `Factors` (intentional until decided otherwise)

See gear-optimizer-comparison.md §Objective-set divergences for the full table. Highlights:

- **Advanced Training** = `[ATSpeed^1, EPower^0.5]` — the site's `AT` factor also carries
  `ECap^1`. Omitted deliberately (decided 2026-07-28): the advisor's allocator BBs AT (full
  bars), where extra cap adds no training speed; ECap only matters under manual, under-fed play.
- **Augments / Beards / Wandoos** score raw speed only; the site mixes in E/M cap/power/bars.
- **Adventure** (`Power^1 × Toughness^0.5`) and **Yggdrasil** (descending-exponent harvest priority
  4/4/3/2/1) are native extensions with no site counterpart. The 0.5 is deliberate: damage is
  `(attack − enemyDefense/2) × multiplier`, so kill rate is linear in Power and Toughness adds
  nothing to it — Toughness only clears a survival threshold. The game's own bars agree (autokill
  gates UUG `800K/400K`, Walderp `13M/7M`; Beardverse manual gate `1.3M/550K` = 2.36:1). Equal
  exponents mean "+1 % Power == +1 % Toughness", which at a typical 2.5:1 stat spread prices a
  *point* of Toughness ~2.5× above a point of Power — backwards. A threshold model would be
  correct and a product cannot express one; 0.5 is the closest this form gets.
- **Adventure excludes Respawn on purpose**: base-0 stats explode the product at low totals
  (16→36 respawn would "double" the score). Respawn coverage is the TopRespawn pin's job.

## Adding an objective

1. Use existing `Stat` constants (or add one that matches the site's name exactly).
2. Express priority as exponents (weights in the product), not stat order.
3. Beware base-0 stats (Power/Toughness/Respawn) in composites — they dominate at low values.
4. Name it once and never rename (persisted verbatim in settings/profiles) — and check it does not
   collide with a `GearChain.Presets` name, which resolves first.
