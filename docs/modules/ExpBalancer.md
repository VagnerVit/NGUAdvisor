# ExpBalancer (`Managers/ExpBalancer.cs` + `Managers/ExpRatioTables.cs`)

EXP purchase planner: walks purchased E/M power/cap/bars toward the guide's ratios.
`ExpRatioTables` is the Unity-free, unit-tested half (which ratio does this phase want);
`ExpBalancer` reads the live character, picks the phase, and does the walking.

## Ratio semantics — the most misread numbers in the guide (user-verified vs Blaze's Ratioz)

- "3:1 E:M" is a ratio of PURCHASED STAT VALUES, **not** an EXP split. Magic units cost exactly
  3× energy (game unit costs: pow 450 vs 150, cap 3-per-750 vs 1-per-250, bars 240 vs 80 EXP),
  so a target value ratio of `r:1` needs an EXP split of `PoolE = r/(r+3)` — 3:1 lands on an EVEN
  50/50 split. Treating "3:1" as the EXP split drives the values to 9:1 and wastes EXP.
- "5:160k:4 pow:cap:bars" is also a UNIT ratio; in EXP-space a pool's shares are
  power:cap:bars = 750:640:320 (identical for magic — all its costs scale ×3).

## Phase table (`ExpRatioTables.For`, transcribed from `external/ngu-guide` ch.1-7)

| Phase (ProgressionAnalyzer.Chapter) | guide P:C:B | EXP-space shares | E:M values | EXP pool E/M |
|---|---|---|---|---|
| ch.1-2 | 1:37.5k:1 | 150:150:80 | energy only | 1.0 / 0 |
| ch.3, T5 not beaten | 5:160k:4 | 750:640:320 | energy only | 1.0 / 0 |
| ch.3, T5 beaten (CBlock1) | 5:160k:4 | 750:640:320 | 5:1 | 0.625 / 0.375 |
| ch.4-6 | 5:160k:4 | 750:640:320 | 3:1 | 0.5 / 0.5 |
| ch.4, T6v2..v3 or CBlock2 done | 5:160k:4 | 750:640:320 | 2:1 | 0.4 / 0.6 |
| ch.7+ | 4:150k:1 | 600:600:80 | 3:1 | 0.5 / 0.5 |

- Chapter 0 (unknown) falls back to the ch.3-6 row — the guide's longest stretch.
- Magic locked (all-time `highestBoss < 37`) forces energy-only regardless of phase.
- The guide's ch.1-3 "only buy magic cap for Ygg unlocks/auto-activations" is a manual one-off
  call, so those phases are modelled as energy-only rather than reserving a magic share.
- Not auto-detected: the pre-T7 "energy only once you can BB Normal NGU Ygg/EXP" window, the
  ch.6+ R3 phases, and the T9 absolute targets (3M/1M power etc. — a different model entirely).
- A phase change makes `WriteCustomPlan` zero the boxes a phase doesn't want (e.g. the magic
  custom-purchase boxes in ch.1-2), which also switches `Main`'s `AutoBuyEM` path off for them.

## Walk-toward-ratio model (replaces "catch the runaway leader")

Each stat has a level `k = ExpSpent / TargetShare`; equal levels = perfect ratio. The old code
anchored targets to the single HIGHEST level — a stat left ahead by an earlier ratio phase (the
early 1:37.5k:1 pours EXP into CAP) demanded an astronomical catch-up lump you can't un-spend.
`BuyTick(fraction)` instead **waterfills** a small budget across the lagging stats — raise the
lowest levels to a common water line, never referencing the leader. Converges smoothly; once all
levels are within band (`BalanceTolerance = 0.75` of the max) it degrades to proportional
maintenance. `Analyze()` reports balance % (min/max level), which stats the next chunk feeds, and
the phase the targets came from.

## No dead zone at small banks (user-reported: "nothing is ever bought")

Two guards used to make a small EXP bank permanently unspendable, and they compounded:

1. A flat `budget < 100 → skip`. At 959 banked EXP the 10% tick budget is 95, so it bought
   nothing — every minute, forever, while EXP trickled in at +144/hr. Nothing was spent, so the
   bank never grew past the floor either. The floor is now the **cheapest unit actually on offer**
   (`UnitCost`, mirroring `BuyStat`'s rounding: power/bars per-unit, cap 1 EXP / 3 for magic),
   clamped to the bank. Waiting has no upside — a purchase is an instant permanent stat.
2. Even with a budget, the waterfill can slice it into per-stat crumbs that each round down to
   zero units, so every `BuyStat` returned 0. If the walk bought nothing, the whole budget now
   goes to the most-behind stat that one unit is affordable for.

## Game-gate handling

- Custom purchases unlock permanently at **all-time `highestBoss ≥ 17`** (never re-lock on Evil —
  one of the few CORRECT uses of raw highestBoss); magic stats at ≥ 37.
- Cap buys are game-gated until the cap crosses 100k — a gated stat is `Buyable=false` and
  **excluded from the balance** so it can't pin the percentage at 0 forever.
- `BuyStat` replicates the game's `buyCustom*` math exactly per stat: unit costs, cap rounded to
  250s, `hardCap()` clamp. `WriteCustomPlan` mirrors the reachable deficits into the game's
  custom-purchase boxes so the game's own "Buy ALL Custom" button reflects the walk.

Consumers: OptimizationAdvisor EXP row; AdvisorApply `exp` auto-toggle calls `BuyTick`.
