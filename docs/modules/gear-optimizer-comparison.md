# Native gear optimizer vs. reference (external/gear-optimizer)

Comparison of the native C# gear optimizer (`Managers/GearScorer.cs`, `GearObjectives.cs`,
`GameGearAdapter.cs`, `GearOptimizer.cs`) against the reference JS implementation
(`external/gear-optimizer/src/`). Audited 2026-07-28 against the cloned source.

## Identical (validated ports)

| Concern | Native | Reference | Status |
|---|---|---|---|
| Score formula | `GearScorer.ScoreVals` | `util.js score_vals` | Exact: product of `(total/100)^exponent` |
| Raw stat totals | `GearScorer.GetRawVals` | `util.js get_raw_vals` | Exact: base 0 for Respawn/Power/Toughness, base 100 otherwise; NaN skipped; first weapon = mainhand, later weapons scaled by `offhand/100` |
| Cube stats | `GameGearAdapter.BuildCubeItem` | `util.js cubeBaseItemData` | Exact tier formulas for Drop/Gold/Hack/Wish |
| Accessory uniqueness | `accSet` guard + pool dedup | implicit (item DB ids) | Same game rule: one copy per accessory id |

`GearOptimizer.ScoreOf` is a dense-array rewrite of `GetRawVals + ScoreVals` for the hot loop —
documented as exact (missing stat = 0, NaN folded to 0 at build, literal offhand rule, cube+base
folded into a constant). Any change to the scoring semantics must be made in BOTH places.

## Different by design (native is game-truth; do not "fix" toward the site)

| Concern | Native | Reference |
|---|---|---|
| Item stats | Live `Equipment` via `getBonusFactor` (exact per-stat divisors), maxed by `CalcCap(cap, level)` | Static DB `src/assets/Items.js` with hand-maintained maxed values |
| Candidate set | Live inventory + equipped (you can only equip what you own) | Full item DB filtered by user-set zone/titan-version/looty/pendant limits (`util.js allowed_zone`) |
| Offhand % | Live `weapon2Factor() * 100` (wish 28+45 progress), 30 s cache | User input (`state.offhand * 5`) |
| Nude base stats | Live `adventureAttackBonus()` / `adventureDefenseBonus()` | User input (`basestats`) |
| Search | Coordinate ascent over main slots + greedy fill + local swap over accessories, ≤5 alternating rounds. Justified: NGU has no set bonuses → objective near-separable per slot | Per-slot Pareto filter (`dominates`/`pareto`) → cartesian layout expansion with Pareto pruning → accessory worst-replacement loop → alternatives + hardcap-driven drops |
| Respawn coverage | `forceTopRespawn` pin: if the merit loadout has no respawn, pin the max-respawn item whose pinned loadout scores best | No equivalent; site users add Respawn as an extra priority with `maxslots` |

## Gaps in native (reference has it, native does not)

1. **Hard caps** (`util.js hardcap` + `capstats`): the site clamps each stat multiplier to
   `100 * max(1, hardcap/nudeTotal)` — matters when a stat is hard-capped in game (e.g. Move
   Cooldown floor). Native scores raw only; deliberately deferred ("rarely bind",
   `GearOptimizerDiagnostic` note). If a capped stat ever misranks gear, port `hardcap` into
   `GearScorer` and feed live cap stats.
2. **Multi-priority chains**: the site runs up to 5 factors sequentially (`compute_optimal` per
   priority, each with a `maxslots` accessory budget); native optimizes ONE objective (multi-stat
   via exponents). The Yggdrasil objective emulates a priority chain with descending exponents.
3. **Locked slots / disabled items**: site supports user-locked items (`construct_base`) and a
   per-item disable flag. Native's only pin is the TopRespawn pin.
4. **Negative exponents**: site has cap-speed factors (`ECAPSPEED` = Cap^-1 × Bars). Native
   `ScoreVals` handles any exponent, but no such objective is defined in `GearObjectives`.
5. **Alternatives / tie detection** and "drop gear that stops contributing due to hard caps":
   site-only post-passes.

## Objective-set divergences (verify intent before copying either way)

Reference factors live in `external/gear-optimizer/src/assets/ItemAux.js` (`single_factors`,
`multiple_factors`, auto-generated `remaining_factors`).

| Objective | Native (`GearObjectives`) | Reference (`Factors`) | Note |
|---|---|---|---|
| Advanced Training | `[ATSpeed^1, EPower^0.5]` | `AT: [EPower^0.5, ECap^1, ATSpeed^1]` | Native omits ECap^1 **deliberately** (decided 2026-07-28): the advisor's allocator BBs AT (full bars), where extra cap adds no training speed. ECap only matters when AT runs under-fed — the site's manual-play assumption. |
| Augments | `[AugSpeed]` | `AUGMENTATION: [ECap, EPower, AugSpeed]` | Native scores speed only. |
| Beards | `[BeardSpeed]` | `BEARD: [EPower^¼, EBars^½, MPower^¼, MBars^½, BeardSpeed]` | Native scores speed only. |
| Wandoos (combined) | `[WandoosSpeed]` | `WANDOOS: [ECap^½, WSpeed^½, MCap^½, WSpeed^½]` | Native E/M Wandoos variants DO match the site's EWANDOOS/MWANDOOS. |
| Adventure | `[Power, Toughness]` product | no composite (separate Power / Toughness factors) | Native extension. Respawn deliberately excluded (base-0 stat explodes the product; TopRespawn pin covers it). |
| Yggdrasil | `[SeedGain^4, YggYield^4, Exp^3, Gold^2, AP^1]` | single-stat factors only | Native extension expressing the guide's harvest priority as soft weights. |
| NGUs, Wishes, Hacks, E/M NGU, TM variants, Blood Rituals | match | match | Exponents identical (NGUs ½/½/½/½/1; Wishes 0.17×6/1). |
| not in native | — | NGUSHACK, NGUWISH, WISHHACK, E/M/X CAPSPEED, EMPC, E/M Beards | Add on demand. |

## Respawn cap — a game rule NEITHER optimizer models in generic scoring (found 2026-07-28)

The game floors the gear respawn factor at **0.2** (decomp `AdventureController.respawnTime`:
factor = `1 − bonuses[Respawn]`, min 0.2) — gear respawn past 80% total reduction is wasted.

- Reference site: does NOT model it (`capstats` has no Respawn entry; respawn scored linearly).
- Native `GearScorer`/"Respawn" objective: also linear, no floor.
- Native `GearHunter` (Loot Hunter set): DOES model it — set-level score
  `(100+ΣDC) / (attack + nonGear × max(0.2, 1−ΣRespawn/100))`, see GearHunter.md.

**Improvement candidate**: clamp the Respawn stat total at the floor in scoring (a `hardcap`-style
clamp keyed on the game read, not on site capstats). Matters for any composite objective carrying
Respawn and for multi-respawn accessory sets (the guide recommends ⅓ of acc slots respawn for PP
farming from Evil on); the single-item TopRespawn pin is unaffected.

## GO hardcap data point (confirms "rarely bind")

The site's `capstats` defaults (`src/reducers/Items.js` ~line 424) are the game's absolute numeric
caps: E/M/R3 Cap 9e18 (≈ long.MaxValue), Power/Bars 1e18. They bind only at sadistic endgame —
the native decision to defer the `hardcap` clamp is evidence-backed, not a guess.

## Reference-only calculators (not gear, but reusable formulas)

`external/gear-optimizer/src/` also ships time-to-level simulators driven by the same factor
scoring: `NGU.js` (NGU bonus curves incl. softcap `1 + level^scexponent × scbonus × bonus`, per
normal/evil/sadistic), `Augment.js` (aug/upgrade cost + gold-limited reachable-level tick sim),
`Hack.js` (hack milestones, `1.0078^level` cost growth), `Wish.js` (E/M/R3 split optimizer for
wish completion). Useful as oracles for future advisor planners (LevelPlanner, SpendPlanner).

## Validation workflow

`GearOptimizerDiagnostic.Run()` (Settings → diagnostic) dumps per-item stat maps and per-objective
current/optimized scores to `logs/gearopt-diagnostic.log` — compare against the site with the same
save (F3 quicksave writes `NGUSave.json`, which loads into the site). Pure scoring math is also
unit-testable: `GearScorer` has no game dependencies (same pattern as the linked-file tests in
`tests/NGUAdvisor.Tests`).
