# Native gear optimizer vs. reference (external/gear-optimizer)

Comparison of the native C# gear optimizer (`Managers/GearScorer.cs`, `GearObjectives.cs`,
`GameGearAdapter.cs`, `GearOptimizer.cs`) against the reference JS implementation
(`external/gear-optimizer/src/`). Audited 2026-07-28 against the cloned source; the chain / pins rows
and gaps 2–3 revised 2026-08-10 when `GearChain.cs` and `Settings.PinnedGearIds` landed.

## Identical (validated ports)

| Concern | Native | Reference | Status |
|---|---|---|---|
| Score formula | `GearScorer.ScoreVals` | `util.js score_vals` | Exact: product of `(total/100)^exponent` |
| Raw stat totals | `GearScorer.GetRawVals` | `util.js get_raw_vals` | Exact: base 0 for Respawn/Power/Toughness, base 100 otherwise; NaN skipped; first weapon = mainhand, later weapons scaled by `offhand/100` |
| Cube stats | `GameGearAdapter.BuildCubeItem` | `util.js cubeBaseItemData` | Exact tier formulas for Drop/Gold/Hack/Wish |
| Accessory uniqueness | `accSet` guard + pool dedup | implicit (item DB ids) | Same game rule: one copy per accessory id |

`GearOptimizer.ScoreContext.ScoreOf` is a dense-array rewrite of `GetRawVals + ScoreVals` for the hot
loop — documented as exact (missing stat = 0, NaN folded to 0 at build, literal offhand rule,
cube+base folded into a constant). **Any change to the scoring semantics must be made in BOTH
places.** The scorer moved into the nested `ScoreContext` class when the priority chain landed (one
context per priority, over shared candidate pools); the equivalence comment travelled with it and
sits above `ScoreContext`. `ScoreContext` is **not re-entrant** — see GearOptimizer.md.

## Different by design (native is game-truth; do not "fix" toward the site)

| Concern | Native | Reference |
|---|---|---|
| Item stats | Live `Equipment` via `getBonusFactor` (exact per-stat divisors), maxed by `CalcCap(cap, level)` | Static DB `src/assets/Items.js` with hand-maintained maxed values |
| Candidate set | Live inventory + equipped (you can only equip what you own) | Full item DB filtered by user-set zone/titan-version/looty/pendant limits (`util.js allowed_zone`) |
| Offhand % | Live `weapon2Factor() * 100` (wish 28+45 progress), 30 s cache | User input (`state.offhand * 5`) |
| Nude base stats | Live `adventureAttackBonus()` / `adventureDefenseBonus()` | User input (`basestats`) |
| Search | Coordinate ascent over main slots + greedy fill + local swap over accessories, ≤5 alternating rounds. Justified: NGU has no set bonuses → objective near-separable per slot | Per-slot Pareto filter (`dominates`/`pareto`) → cartesian layout expansion with Pareto pruning → accessory worst-replacement loop → alternatives + hardcap-driven drops |
| Priority chain | `GearChain` — ≤5 `GearPriority` steps, budget recomputed per step from the slots ACTUALLY filled; priority 0 owns the main slots, later steps are accessory-only. Chains are named presets in `GearChain.Presets`, in the same name namespace as objectives, plus a per-breakpoint `Priorities` list in the profile | `factorslist`/`maxslotslist` (≤5), one `compute_optimal` per priority, `count_accslots` for the budget — carried on a Pareto FRONT of base layouts rather than a single layout |
| Pins / locked items | ONE global `Settings.PinnedGearIds` list ("always wear these"), placed before every optimization; unowned pins skipped, duplicates refused, over-budget pins truncated — each logged. Dropped entirely on a live titan fight (`ResolveTitanGear`) | `state.locked` fed to `construct_base`, per item, edited in the site UI; plus a per-item disable flag native has no counterpart for |
| Respawn coverage | `forceTopRespawn` pin: if the merit loadout has no respawn, pin the max-respawn item whose pinned loadout scores best. Since the chain landed, the site's approach is *also* available natively — the `Adventure + Respawn` preset reserves a respawn slot unconditionally, where the pin fires only when there is no respawn at all | No `forceTopRespawn` equivalent; site users add Respawn as an extra priority with `maxslots` |

## Gaps in native (reference has it, native does not)

1. **Hard caps** (`util.js hardcap` + `capstats`): the site clamps each stat multiplier to
   `100 * max(1, hardcap/nudeTotal)` — matters when a stat is hard-capped in game (e.g. Move
   Cooldown floor). Native scores raw only; deliberately deferred ("rarely bind",
   `GearOptimizerDiagnostic` note). If a capped stat ever misranks gear, port `hardcap` into
   `GearScorer` and feed live cap stats.
2. ~~**Multi-priority chains**~~ — **CLOSED** (`GearChain` + the `Optimize(chain, pins, force)`
   overload). Native now runs up to 5 priorities sequentially, each claiming at most its budget of
   the accessory slots still free, its filled slots frozen for later priorities — the same
   `construct_base` → `compute_optimal`-per-priority sequence as
   `sagas/optimize.worker.js:24-40`, with the budget recomputed per priority via the
   `count_accslots` rule (`Optimizer.js:135`). **Still different:** the search *inside* one priority
   is native's coordinate ascent + greedy fill + local swap, so a priority hands the next one a
   SINGLE locked layout, where the reference carries a Pareto FRONT of base layouts forward and only
   collapses it at the end. Native can therefore lock a slot that a later priority would have
   preferred differently; the reference can back out of that choice. Also unchanged: the objective's
   own multi-stat exponents (Yggdrasil's descending 4/4/3/2/1) remain the way to express *soft*
   weights inside one priority — the chain is for hard slot reservations.
3. ~~**Locked slots**~~ — **CLOSED** for locked/pinned items: `Settings.PinnedGearIds` is a global
   "always wear these" list, placed by `PlacePins` before any optimization, the port of
   `construct_base(state.locked, state.equip)` (`Optimizer.js:26`). **Still different:** the site's
   locks are per-optimization UI state, native's are one persisted global list applied by every entry
   point (and deliberately dropped for a live titan fight). The **per-item disable flag** has no
   native counterpart — native's candidate set is the live inventory, so "disable this item" is not
   expressible; it remains open.
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

## Respawn cap — a game rule native models and the site does not (found 2026-07-28, closed 2026-08-12)

The game floors the gear respawn factor at **0.2** (decomp `AdventureController.respawnTime` and
`NGUController.respawnBonus`: factor = `1 − bonuses[Respawn]`, min 0.2) — gear respawn past 80% total
reduction is wasted.

- Reference site: does NOT model it (`capstats` has no Respawn entry; respawn scored linearly).
- Native `GearHunter` (Loot Hunter set): has always modelled it — set-level score
  `(100+ΣDC) / (attack + nonGear × max(0.2, 1−ΣRespawn/100))`, see GearHunter.md.
- Native `GearScorer`/`GearOptimizer`: **now clamps the Respawn total at 80** —
  `GearScorer.CapValue`, applied in `GetRawVals` and mirrored in `ScoreContext.ScoreOf`. It is an
  ABSOLUTE cap keyed on the game read, not the site's nude-total-relative `hardcap`.

**This is a DELIBERATE DIVERGENCE from the oracle**: a native score for an objective carrying
Respawn will read lower than the site's whenever the total exceeds 80, and the two loadouts can
differ. That is native being right and the site being linear, not a port defect — do not "fix" it
back by removing the clamp when comparing against the site. It matters for any composite objective
carrying Respawn and for multi-respawn accessory sets (the guide recommends ⅓ of acc slots respawn
for PP farming from Evil on); the single-item TopRespawn pin is unaffected.

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
