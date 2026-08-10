# GearOptimizer (`Managers/GearOptimizer.cs`)

Phase 2 of the native gear optimizer: the search. Finds the loadout maximizing a
`GearObjectives.Objective` — or a whole `GearChain` of them — over the live inventory.
**Main thread only** (reads inventory; `OptimizeAndEquip` touches game/UI).

## What one `Optimize` call does, in order

1. **Pins** (`PlacePins`) — the user's "always wear these" ids are placed FIRST, into their own
   slots, before any optimization. Port of `construct_base(state.locked, state.equip)`
   (`external/gear-optimizer/src/Optimizer.js:26`).
2. **The chain** (`RunChain`) — each priority in order, claiming a budget of the accessory slots
   still free, its filled slots frozen for every later priority. See GearChain.md for the chain
   layer and the budget rule.
3. **The TopRespawn pin** — unchanged in meaning: if the finished loadout carries no respawn at all,
   re-run the WHOLE chain around one pinned respawn candidate.

Priority 0 owns the main slots and runs the full alternation; every later priority is
accessory-only — which *is* "freeze the main slots after priority 0", expressed by simply not
calling `MainAscent` again.

## Algorithm (vs. the reference's Pareto machinery)

NGU gear has no set bonuses, so the product objective is near-separable per slot. Instead of the
reference's per-slot Pareto filter + cartesian layout expansion (`Optimizer.js
optimize_layouts/pareto`), native runs, per priority:

1. **Main slots** — coordinate ascent (`MainAscent`, ≤8 iterations): re-pick each slot's best item
   with everything else fixed, repeat until stable. Mainhand and offhand exclude each other's pick.
   Slots frozen by a pin are skipped (`pinnedMain`, `mainWeaponPinned`, `offWeaponPinned`).
2. **Accessories** (`AccessoryOptimize(ctx, cap, firstFree)`) — greedy fill up to `cap`, then local
   swap (≤50 sweeps) re-picking each slot from `firstFree` upward. `firstFree` is the first index
   this priority OWNS; everything below it is frozen by a pin or an earlier priority and is never
   re-picked — but frozen accessories still score through the normal add path, which is what makes
   the greedy fill "best marginal accessory *given what is already worn*". Mirrors the spirit of the
   reference's `get_accs` + worst-replacement loop.
3. **Alternation** (`RunOptimize`) — main ascent ↔ accessory pass, ≤5 rounds, stop when the score
   stops improving (relative epsilon 1e-12).

Accessory uniqueness (`accSet`): one copy per accessory id — a real game rule, not an optimizer
limitation. During a slot's re-pick scan its own current item is removed from the set so it does
not veto candidates.

`OffhandPercent` is read **once per `Optimize` call** into `offhandFactor` and handed to every
`ScoreContext`. It is a 30 s TTL cache over the live `weapon2Factor()`; letting it refresh mid-call
would score different priorities — or different TopRespawn trials — under different offhand factors
and make their scores incomparable.

## Hot path — `ScoreContext.ScoreOf`

The per-objective scoring state (`_statNames`, `_exponents`, `_constVals`, `_idToVec`, `_scratch`)
lives in the private nested `ScoreContext`, built per objective over the SHARED candidate pools;
the search helpers take one as a parameter. `ScoreOf(Result)` is an exact dense-array rewrite of
`GearScorer.GetRawVals + ScoreVals`: each candidate gets a `double[]` of just the objective's stats
(`VecOf`, NaN→0 at build time), cube + nude base fold into `_constVals`, scoring is array adds into
one reused buffer. The offhand rule is reproduced literally, including "no mainhand → offhand takes
full value". **Any scoring-semantics change in `GearScorer` must be mirrored here** (and vice
versa); the equivalence argument is in the long comment above `ScoreContext`, and
gear-optimizer-comparison.md §Identical records the same requirement.

### `ScoreContext` is NOT re-entrant

`_scratch` is a **single buffer reused by every `ScoreOf` call on that instance**. So `ScoreOf` must
never run while another `ScoreOf`-driven loop is mid-flight *on the same instance* — nesting two
scoring loops that share a context silently corrupts both scores, and **no test would catch it**
(the result is still a plausible number).

The rule: **one context per (chain run, priority)**. `RunChain` constructs a fresh `ScoreContext` for
every step (`GearOptimizer.cs:576`) and no priority's scoring loop ever runs inside another's. If you
add a pass that wants a second objective's score mid-search, build it its own context — do not reach
for the one the loop is already using.

## `Result.Score` is priority 0's objective score

A chain has no single score, so `RunChain` sets `r.Score` from the **lead** context — priority 0's
objective (`GearOptimizer.cs:600-603`). `CurrentScore(chain)` scores the *same* entry, resolved
through the *same* filter (`LeadObjective`: `Take(MaxPriorities)` then first non-null objective, the
identical filter `Optimize` uses to build `steps`).

Both sides must agree because `AdvisorApply.ApplyGearRefresh` compares them behind a **5 %
re-equip bar** (`AdvisorApply.cs:954-965`): `CurrentScore(chain)` vs `Optimize(chain, …).Score`. If
one side measured the whole chain and the other the lead, the bar would be comparing two different
quantities and would fire (or never fire) at random. The 5 % bar is bypassed on an objective switch,
detected via `GearChain.Describe` — see GearChain.md §Describe.

## Pins — `ActivePins()` and `PlacePins`

`ActivePins()` returns `Main.Settings.PinnedGearIds` — **one global list**, not one per breakpoint
("Ring of Greed should be in every inventory"), so every entry point honors it. `Optimize`'s
`pinnedIds` parameter is `null` = "caller didn't specify" → fall through to the global setting; a
caller that wants **no** pins must pass an **empty list**, not null.

Edge handling, all three reported once per `Optimize` call (never once per chain run, and never for
the TopRespawn candidate, which rides in as an unreported extra pin):

| Case | Behavior | Log |
|---|---|---|
| Pinned id not in the pools (no longer owned) | skipped | `Gear pins not in inventory, skipped: …` |
| Pin beyond the accessory slots / no free main slot | truncated | `Gear pins dropped, no free slot: …` |
| Duplicate pin (same id twice) | refused | `Gear pins dropped, already pinned (one copy per item): …` |

Why each matters:

- **The pin list outlives the item.** An id the player no longer owns is simply absent from the
  pools — skip and say so rather than throw.
- **Silent truncation reads to the user as "the optimizer ignored my pin."** That is the whole reason
  the drop path logs instead of just dropping.
- **A duplicate weapon pin must not land in both hands.** `ScoreOf` would then add it at full value
  AND at the offhand factor, inflating every score in the run.

## TopRespawn pin (`forceTopRespawn`)

Pass 1 runs the chain on pure merit (user pins still apply). Only if the merit loadout carries NO
respawn: the candidate joins the pin list and the **whole chain re-runs** around it. Winner rule:
highest raw respawn wins outright; loadout score breaks respawn ties (the Stapler-vs-Ring-of-Greed
rule). A cheap pre-pass finds `maxResp` first so only tied candidates get a full chain run. The
reference has no equivalent feature.

## Entry points

- `Optimize(obj, forceTopRespawn = false, pinnedIds = null)` — a one-element unlimited chain, so every
  pre-chain caller is unaffected by the CHAIN layer. It is **not** pin-free: `pinnedIds = null` means
  the global `Settings.PinnedGearIds`, because the callers that reach this overload to *equip* must
  honour them. **Valuation callers must pass `new int[0]`** — a score that moves with the user's pin
  list is not comparable against `CurrentScore` (which knows nothing about pins), and it makes the
  `GearOptimizerDiagnostic` regression baseline a function of the user's settings.
  - **Pinned** (they equip): `GearHunter.ResolveLoadout` (the hunt set is worn),
    `LoadoutsPanel`'s swap preview (it previews what the swap will equip), `OptimizeAndEquip`.
  - **Unpinned** (`new int[0]`, they only value): `GearOptimizerDiagnostic` (the regression baseline),
    `ProgressionAnalyzer.GetOptimalFocus`, `OptimizationAdvisor.ProjectedBestGear`,
    `GoldDropAdvisor.GoldGearFactor`, `InventoryAdvisor`'s keep/trash sweep — which instead adds
    `PinnedGearIds` to its keep set outright, since a pin must never be trashed.
- `Optimize(chain, pinnedIds, forceTopRespawn = false)` — the chain-aware overload above. Same
  `null` = global pins rule.
- `OptimizeIds(obj, forceTopRespawn, pinnedIds = null)` / `OptimizeIds(chain, pinnedIds, forceTopRespawn)`
  — the same, returning deduped positive item ids for writing into a loadout/profile.
- `CurrentScore(obj)` — scores the CURRENTLY-equipped loadout for one objective.
  `CurrentScore(chain)` — the chain overload; scores the chain's lead objective (see above).
- `FindObjective(name)` — objective lookup by the name profiles/settings store. Note that name
  resolution that must also accept a **chain preset** goes through `GearChain.Resolve`, not here.
- `ResolveModeGear(objectiveName, forceRespawn, fallback, pinnedIds = null)` — objective set →
  optimize live (as a one-element unlimited chain); unknown/empty → static loadout IDs. Never throws.
- `ResolveTitanGear()` — **safety override**: if any targeted titan spawns soon and is NOT
  auto-killable, the loot objective is replaced by "Adventure" (kill set) — the loot set on a real
  fight was a user-reported death loop (twice: empty loadout, then drop gear on a live T6v2). On that
  path it also passes `new int[0]` for `pinnedIds`, i.e. **the real-fight override drops pins too**:
  pinned loot/utility gear equipped into a live titan is the same death loop the override exists for.
  AK-trivial spawns honor the configured loot objective *and* the global pins.
- `ResolveGoldGear()` — nothing configured → optimize for "Gold Drops" (data-driven default).
- `OptimizeAndEquip(obj, forceTopRespawn)` — optimize + equip live.
- `OffhandPercent` — live `weapon2Factor() * 100`, cached 30 s (scoring reads it thousands of
  times per pass). 0 while the second weapon slot is locked.

## Known gaps vs. reference

No hard caps, no negative-exponent objectives, no per-item disable flag, no alternatives detection —
see gear-optimizer-comparison.md §Gaps before extending. Multi-priority chains and locked/pinned
items now exist; the search inside a priority is still coordinate ascent, not Pareto expansion.

## Reference counterpart

`external/gear-optimizer/src/Optimizer.js` — `construct_base` (pins), `compute_optimal` (per-priority
driver), `count_accslots` (the budget), `pareto`/`dominates` (candidate pruning), `optimize_layouts`
(main slots), `get_accs` + the worst-replacement loop (accessories), `sort_accs`, hardcap drop pass;
`src/sagas/optimize.worker.js:24-40` — the pins-then-priorities sequence native reproduces.
