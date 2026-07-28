# ExpBalancer (`Managers/ExpBalancer.cs`)

EXP purchase planner: walks purchased E/M power/cap/bars toward the guide's ratios.

## Ratio semantics — the most misread numbers in the guide (user-verified vs Blaze's Ratioz)

- "3:1 E:M" is a ratio of PURCHASED STAT VALUES, **not** an EXP split. Magic units cost exactly
  3× energy (game unit costs: pow 450 vs 150, cap 3-per-750 vs 1-per-250, bars 240 vs 80 EXP),
  so an EVEN 1:1 EXP split produces the 3:1 value ratio → pools are 50/50 in EXP-space.
  Treating it as an EXP split would drive values to 9:1 and waste EXP.
- "5:160k:4 pow:cap:bars" is also a UNIT ratio; in EXP-space a pool's shares are
  power:cap:bars = 750:640:320 (identical for magic — all its costs scale ×3).
- **D4 phase tweak**: post-T6v2 (or 24HR challenge ≥ 3 as the CBlock2-done proxy) the value
  ratio becomes 2:1 → EXP 0.4 E / 0.6 M; reverts at T6v4. Other guide phases not auto-detected.

## Walk-toward-ratio model (replaces "catch the runaway leader")

Each stat has a level `k = ExpSpent / TargetShare`; equal levels = perfect ratio. The old code
anchored targets to the single HIGHEST level — a stat left ahead by an earlier ratio phase (the
early 1:37.5k:1 pours EXP into CAP) demanded an astronomical catch-up lump you can't un-spend.
`BuyTick(fraction)` instead **waterfills** a small budget across the lagging stats — raise the
lowest levels to a common water line, never referencing the leader. Converges smoothly; once all
levels are within band (`BalanceTolerance = 0.75` of the max) it degrades to proportional
maintenance. `Analyze()` reports balance % (min/max level) + which stats the next chunk feeds.

## Game-gate handling

- Custom purchases unlock permanently at **all-time `highestBoss ≥ 17`** (never re-lock on Evil —
  one of the few CORRECT uses of raw highestBoss); magic stats at ≥ 37.
- Cap buys are game-gated until the cap crosses 100k — a gated stat is `Buyable=false` and
  **excluded from the balance** so it can't pin the percentage at 0 forever.
- `BuyStat` replicates the game's `buyCustom*` math exactly per stat: unit costs, cap rounded to
  250s, `hardCap()` clamp. `WriteCustomPlan` mirrors the reachable deficits into the game's
  custom-purchase boxes so the game's own "Buy ALL Custom" button reflects the walk.

Consumers: OptimizationAdvisor EXP row; AdvisorApply `exp` auto-toggle calls `BuyTick`.
