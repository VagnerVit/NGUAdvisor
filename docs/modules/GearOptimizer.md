# GearOptimizer (`Managers/GearOptimizer.cs`)

Phase 2 of the native gear optimizer: the search. Finds the loadout maximizing a
`GearObjectives.Objective` over the live inventory. **Main thread only** (reads inventory;
`OptimizeAndEquip` touches game/UI).

## Algorithm (vs. the reference's Pareto machinery)

NGU gear has no set bonuses, so the product objective is near-separable per slot. Instead of the
reference's per-slot Pareto filter + cartesian layout expansion (`Optimizer.js
optimize_layouts/pareto`), native runs:

1. **Main slots** — coordinate ascent (`MainAscent`, ≤8 iterations): re-pick each slot's best item
   with everything else fixed, repeat until stable. Mainhand and offhand exclude each other's pick.
2. **Accessories** (`AccessoryOptimize`) — greedy fill (best marginal item until no improvement or
   slots full), then local swap (≤50 sweeps re-picking each slot). Mirrors the spirit of the
   reference's `get_accs` + worst-replacement loop.
3. **Alternation** (`RunOptimize`) — main ascent ↔ accessory pass, ≤5 rounds, stop when the score
   stops improving (relative epsilon 1e-12).

Accessory uniqueness (`accSet`): one copy per accessory id — a real game rule, not an optimizer
limitation. During a slot's re-pick scan its own current item is removed from the set so it does
not veto candidates.

## Hot path — `ScoreOf`

An exact dense-array rewrite of `GearScorer.GetRawVals + ScoreVals`: each candidate gets a
`double[]` of just the objective's stats (`VecOf`, NaN→0 at build time), cube + nude base fold
into `constVals`, scoring is array adds into one reused buffer. The offhand rule is reproduced
literally, including "no mainhand → offhand takes full value". **Any scoring-semantics change in
`GearScorer` must be mirrored here** (and vice versa); the equivalence argument is in the long
comment above `ScoreOf`.

## TopRespawn pin (`forceTopRespawn`)

Pass 1 optimizes on pure merit. Only if the merit loadout carries NO respawn: pin one respawn item
and re-optimize around it. Winner rule: highest raw respawn wins outright; loadout score breaks
respawn ties (the Stapler-vs-Ring-of-Greed rule). A cheap pre-pass finds `maxResp` first so only
tied candidates get a full `RunOptimize`. The reference has no equivalent feature.

## Mode resolution entry points

- `ResolveModeGear(objectiveName, forceRespawn, fallback)` — objective set → optimize live;
  unknown/empty → static loadout IDs. Never throws.
- `ResolveTitanGear()` — **safety override**: if any targeted titan spawns soon and is NOT
  auto-killable, the loot objective is replaced by "Adventure" (kill set) — the loot set on a real
  fight was a user-reported death loop. AK-trivial spawns honor the configured loot objective.
- `ResolveGoldGear()` — nothing configured → optimize for "Gold Drops" (data-driven default).
- `OffhandPercent` — live `weapon2Factor() * 100`, cached 30 s (scoring reads it thousands of
  times per pass). 0 while the second weapon slot is locked.

## Known gaps vs. reference

No hard caps, no multi-priority chains with per-priority accessory budgets, no locked/disabled
items, no alternatives detection — see gear-optimizer-comparison.md §Gaps before extending.

## Reference counterpart

`external/gear-optimizer/src/Optimizer.js` — `compute_optimal` (driver), `pareto`/`dominates`
(candidate pruning), `optimize_layouts` (main slots), `get_accs` + the worst-replacement loop
(accessories), `sort_accs`, hardcap drop pass.
