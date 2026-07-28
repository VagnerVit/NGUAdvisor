# GearHunter (`Managers/GearHunter.cs`)

GEAR HUNT (user feature 2026-07-11): camp a user-chosen stage for its drops. Two halves:
**ZONE** (the picked stage outranks automatic gear/boost farms in `AdvisorApply.ApplyZones`; mode
locks still win) and **GEAR** (the hybrid "Loot Hunter" loadout, `AdvisorApply.ApplyGearRefresh`).

## Game-truth constant: the respawn floor

```
respawnSec(set) = nonGearRespawn × max(0.2, 1 − ΣRespawn/100)
```

**The 0.2 floor is the game's own hard cap** (decomp `AdventureController.respawnTime`: gear
respawn factor = `1 − bonuses[Respawn]`, floored at 0.2). Respawn stacked past 80% total
reduction from gear is wasted. This module is currently the ONLY place in the codebase that
models the floor — `GearScorer`/`GearOptimizer`'s "Respawn" objective scores respawn linearly
without it (see gear-optimizer-comparison.md §Respawn cap). The reference site doesn't model it
either (its `capstats` has no Respawn entry).

`NonGearRespawnSec()` derives the non-gear share (NGU/clock/perk/wish factors) from the live
total by dividing out the current gear factor: `respawnTime() / max(0.2, 1 − bonuses[Respawn])`.
Fallback 3.5 s on any read failure.

## Why not a product objective (base-zero trap)

`LootScore` scores one accessory as `(100 + DC) × (100 + Respawn)` — both from base 100 ON
PURPOSE. `GearScorer` treats Respawn as base-zero, so a product objective would let ANY respawn
item outrank ALL drop-chance items (the base-zero explosion the "Adventure" objective comment
documents). Set-level AUTO mode instead scores the actual drops/hour shape:

```
score = (100 + ΣDC) / (attackSec + respawnSec(set)),   attackSec = 1.0 (relative weight)
```

so DC and Respawn trade off honestly instead of by per-item rank.

## Loadout resolution (`ResolveLoadout`)

1. Non-accessory slots: `GearOptimizer.Optimize("Adventure")` best Power/Toughness (kills must
   keep landing).
2. Accessories from the user-curated pool (`Settings.LootHunterAccessories`); empty pool = whole
   inventory. `OwnedAccessories` keeps the best-scoring copy per item id (dupes at different
   levels exist).
3. Two modes:
   - **AUTO** (both quota settings 0): `OptimizeAccessorySubset` — greedy fill + local swap (≤20
     sweeps) on the set-level score above. A candidate adding nothing (no DC, no respawn past the
     floor) never improves → slot left for the P/T top-up.
   - **QUOTAS** (`LootHunterRespawnCount` / `LootHunterDropCount`): respawn count first (ranked by
     Respawn), then DC count (ranked by DC). Quota shortfalls fall back from the pool to the whole
     owned inventory (user-reported: a one-item pool made the hunt look inert — the demand is the
     quota, the pool is only the preference).
4. Remaining slots top up with the Adventure result's best P/T accessories.

Returns empty array when nothing resolves (caller skips the pass). Main thread only; callers
throttle. Never throws — failures land in `debug.log`.
