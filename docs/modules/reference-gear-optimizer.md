# Reference implementation map (`external/gear-optimizer/`)

Clone of [gmiclotte/gear-optimizer](https://github.com/gmiclotte/gear-optimizer) — the community
web Gear Optimizer whose scoring the native optimizer reimplements. React app; all math lives in
`src/`. Treat it as a read-only oracle: never edit, re-pull to update.

## Core modules

| File | What it holds |
|---|---|
| `src/util.js` | THE scoring math: `get_raw_vals` (stat totals, base-0 vs base-100, offhand rule), `score_vals` (product of `(v/100)^exp`), `hardcap` + `score_equip` (cap-clamped score), `cubeBaseItemData` (Infinity Cube tier formulas, nude base as pseudo-items 1000/1001), `allowed_zone` (zone/titan/looty/pendant availability filter), `speedmodifier` (loadout-ratio × potion effects, feeds the calculators) |
| `src/Optimizer.js` | The search: `dominates`/`pareto` (per-slot dominance pruning with exponent handling incl. negative), `optimize_layouts` (cartesian main-slot expansion with Pareto pruning), `get_accs` (leave-one-out accessory ranking), `compute_optimal` (per-priority driver: greedy top-N accs + iterative worst-replacement, alternatives detection, hardcap-driven drop pass). Multi-priority: chained over `factorslist` (≤5) with per-priority `maxslots` accessory budgets |
| `src/assets/ItemAux.js` | `Stat` (canonical stat names — `GearObjectives.Stat` mirrors these), `Factors` (= objective presets: `single_factors` + `multiple_factors` with exponents + auto `remaining_factors`), `Slot`, `SetName` (zone list), item/EmptySlot constructors |
| `src/assets/Items.js` | The static item database (per-item maxed stat values, zones, looty/pendant lists). Native replaces this with live reads (`GameGearAdapter`) |

## Time-to-level calculators (not gear search — reusable formulas)

| File | What it computes |
|---|---|
| `src/NGU.js` | NGU bonus curves per difficulty: linear `1 + level*bonus` up to `softcap`, then `1 + level^scexponent * scbonus * bonus`; Respawn special-cased; NGU speed via `speedmodifier` |
| `src/Augment.js` | Augment/upgrade costs (`base * a^min(e,idx) * b^max(0,idx-4)`, versioned bases incl. the `/1.2` sadistic hack), gold costs, `reachable(idx)` — tick-level sim of levels reachable in a time window incl. gold-limited growth |
| `src/Hack.js` | Hack bonus `(level*perLevel + 100) * milestoneMult^milestones`, milestone reducers, `reachable`/`time` sims with `1.0078^level` cost growth (hack 13 feeds its own speed) |
| `src/Wish.js` | Wish completion optimizer: splits E/M/R3 across wishes (`spread_res`/`save_res`/`optimize`), wish score `(cost-progress)` vs resources^exponent with the −0.17 level penalty |

These are candidate oracles for native planners (`LevelPlanner`, `SpendPlanner`, `WandoosAdvisor`)
— when porting a formula, cite the JS file+function in a comment, as `GearScorer` does.

## Gotchas when reading the JS

- `factors` = `[displayName, [statNames], [exponents?]]` — exponents optional, may be negative.
- `state.offhand * 5`: the UI stores offhand in steps of 5% ÷ 25 → multiply by 5 for percent.
- Equip layouts exist in two shapes (`old2newequip`/`new2oldequip`): item-object lists vs
  per-slot id arrays; `other: [1000, 1001]` is always cube + base.
- Everything runs in a web worker; `console.log` lines in `compute_optimal` are progress traces.
