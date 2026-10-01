# YggdrasilManager (`Managers/YggdrasilManager.cs`)

Fruit activation + harvest. Harvest runs under the Yggdrasil mode lock (`LockManager`).

## THE critical invariant: the lock must survive a failed harvest

`HarvestAll` OWNS the Yggdrasil lock from the moment `TryYggdrasilSwap()` hands it over — every
caller (automatic pass, PreRebirth, both manual buttons) does all lock-held work here. A throw
used to walk out with the lock still held, and **nothing could take it back**: unharvested fruit
keeps `NeedsHarvest()` true, which is exactly the state where `TryYggdrasilSwap()` refuses to
restore → lock held for the session → `CanSwap()` false → `RebirthAvailable()` never returned →
**the run could not end**. So restoration is reached from BOTH exits
(`LockManager.RestoreYggdrasilSwap()` on success, `CleanupFailedYggdrasilHarvest()` on throw) and
the harvest fault is rethrown intact.

`CleanupFailedYggdrasilHarvest` order matters: **MacGuffins restore FIRST** (matching the success
path, while the harvest inventory context is still up, before gear restoration shuffles
daycare/inventory slots). Both cleanups are independently guarded — one failing must not skip the
other — and neither may replace the primary harvest exception.

## Fruit indices (decomp `FruitController.consumeFruit` switch)

0 Gold · 1 Power α · 2 Adventure · 3 Knowledge · 4 Pomegranate · 5 Luck · 6 Power β ·
7 Arbitrariness (AP) · 8 Numbers · **9 Rage = `consumePPFruit` (PP progress)** · 10 MacGuffin α ·
11 Power γ · 12 Watermelon · 13 MacGuffin β · **14 Quirks = `consumeQPFruit` (Quirk Points)** ·
15–20 Mayo. The "gain 0 Perk Points and … Progress" lines in yggdrasil.log are Fruit of Rage
(index 9), not the QP fruit; index 14 stays locked until beast quests unlock.

## Harvest triggers (`NeedsHarvest`)

`forced` → any harvestable fruit, immediately. Otherwise: any fruit maxed (`anyFruitMaxxed`) OR
`MacguffinFruit2Ready(PlannedSpecs())` OR `QPFruitReady()` — and then the companion deferral below.

- **Companion deferral** (`DeferForCompanionFruit`): fruits with different max tiers max seconds
  apart (log 17.09: Power β 10805 s, Arbitrariness 10815 s, Rage 10905 s — three passes, three full
  gear/beard/digger swaps; 22 % of passes started within 120 s of the previous one). A pass that
  comes due waits while another growing fruit (activated or perm, `maxTier ≥ 1`, not yet maxed)
  reaches max before a deadline fixed at first-due + `MaxHarvestDeferSeconds` (120). Time to max
  is game truth: `maxTier × tierThreshold() − seconds` (`AllYggdrasil.updateFruitTimers` adds 1 s/s;
  `tierThreshold()` = 3600 − min(quirk[13] × 60, 180)). Bounds: the deadline never moves, it resets
  only when the pass runs or nothing is due; a run clock (`rebirthTime`) that went backwards is a
  rebirth and restarts the window; no wait whose deadline reaches the profile's rebirth target
  (`NextRebirthTargetSeconds`); forced passes (PreRebirth, manual buttons) never wait. Lock safety:
  the deferral only returns false BEFORE `TryYggdrasilSwap` acquires, so it can delay an
  acquisition but never strand one. Cost: the due fruit sits at max for ≤ 120 s of lost growth.
  The first deferral of a window logs `harvest waits for <fruit> …`.
- **MacGuffin fruit 2 (index 13)**: eat-now-vs-wait math using the game's own yield chain —
  `tierFactor × 0.1 × poopModifier × yieldBonus × harvestBonus`, `yieldBonus` =
  `(1 + Yggdrasil spec) × beastQuestPerk.totalYggYieldBonus()` (= `Character.yggdrasilYieldBonus()`
  with the gear term swapped in). Eats when the per-tier value of harvesting NOW (plus tier-1
  harvests for the remaining tiers) beats the per-tier value of waiting for max tier. The gear is
  the set the harvest ACTUALLY runs in: `PlannedSpecs()` (pre-swap: the pass's swap set if
  `SwapGearForPass` returns one, else worn) and `WornSpecs()` inside `HarvestAll` (post-swap, so
  worn IS the harvest gear). It used to multiply an equip factor by the live
  `yggdrasilYieldBonus()`, which already contains the worn Yggdrasil spec — counted twice.
- **QP fruit (index 14)**: eaten only when the swap threshold is satisfied, poop is off, and the
  ITOPOD harvest bonus is 1 (no first-harvest bonus to waste). The threshold applies whenever a swap
  is configured from EITHER source (`SwapConfigured`: objective or static loadout); it used to check
  the static loadout only, so an objective-driven setup ate the fruit below the threshold.
- **Tiers come from `_fc.harvestTier(i)`, never `Fruit.harvestTier()`.** The `Fruit` method divides by a
  hardcoded 3600 s; the controller's divides by the quirk-aware `tierThreshold()` the game eats with
  (decomp `FruitController.harvestTier(int)`), so the Fruit one under-reads by up to a tier.

## Gear swap gate (`SwapGearForPass`)

Returns the Yggdrasil set to equip, or null (pass runs in worn gear). Null when swap is off, the
`YggSwapThreshold` is unmet (unforced: a maxed fruit at tier ≥ threshold), the resolved set equals
the worn set, or the set raises **no payout** of any fruit this pass takes. Decomp truth on which
outputs read gear (`InventoryController.bonuses`):

| Output | Gear term | Fruits |
|---|---|---|
| Seeds (`seedReward`/`harvestSeedReward`) | `1 + Seed Gain` | **every fruit**, eaten or harvested |
| Fruit effect via `yggdrasilYieldBonus()` | `1 + Yggdrasil Yield` | 1, 2, 3, 5, 6, 8, 9, 10, 11, 13, 14 |
| EXP via `addExp` | `1 + EXP` (fixed 1.1 once item 119 is maxxed) | 3 Knowledge |
| AP via `addAP` | `1 + AP` (fixed 1.2 once item 129 is maxxed) | 7 Arbitrariness |
| none | — | 0 Gold (`grossGoldPerSecond` has no gear spec), 4, 12, 15–20 |

So because Seed Gain multiplies every fruit's seeds, a fruit-name gate would be wrong; the gate
compares each taken fruit's payout (seeds, and the eaten effect) under worn vs swapped specs with
the game's own formulas, ceilings and floors (`SeedPayout`, `FruitPayout`, `KnowledgeExp`,
`ArbitrarinessAp`, side-effect-free `PoopFactor`). Swapped specs (`SpecsAfterSwap`) model where
`ChangeGear` puts each item (first per part, second weapon × `weapon2Factor()`, accessories into
slots 0.. by swap, uncovered slots keep the worn item) and sum the game's `equipSpecBonus` →
`getBonusFactor`. Worn specs are the game's `specBonus` → `getBonusFactor`. "Taken" mirrors
`HarvestAll`: forced = every fruit with a tier; normal = maxed + 13/14 on their own triggers. A
valuation throw falls back to swapping (the pre-gate behaviour) — it must never block harvesting.

## HarvestAll details

- Favored-MacGuffin dance: swap the favored MacGuffin in (`ManageFavoredMacguffin`), consume fruit
  10, then `RestoreMacguffins()`. `tierOver1` (manual "harvest all tiers") consumes at any tier;
  the normal path requires the MacGuffin fruit to be at max tier.
- `ReadTooltipLog(false)` before / `(true)` after: the game's tooltip event log is marked with a
  `<b></b>` sentinel so only NEW harvest lines get written to **yggdrasil.log**
  (`Main.LogYggdrasil`). Each tooltip is one record: multi-line tooltips (Adventure's stat list,
  "You also gain:") are joined on one line. The digger restore's per-level
  `AllGoldDiggerController.upgradeMaxLevel` tooltips ("raised this digger's max level to N", up to
  30 per Gold pass) collapse into one `Digger max level raised Nx (to …)` line.
- Header (`LogHarvestHeader`, written after the swap): `Yggdrasil set` / `no gear swap`
  (`LockManager.YggdrasilGearSwapped`), the live `yield x` and `seeds x` factors, the worn gear, and
  the ready list; a fruit set to Harvest in game is marked `(harvest: seeds only)`.
- **No "You gained … Gold" line is not a bug**: with the Gold fruit toggled to Harvest in game
  (`eatFruit = false`, never written by the advisor) `consumeAll` calls `FruitController.harvest(0)`
  — doubled seeds, no gold, tooltip "You gained N Seeds!". yggdrasil.log shows exactly that from
  22.09 on.

## Manual-rebirth reminder (`RebirthHarvestReminder`)

`HarvestAll(true)` before a rebirth runs only from `BaseRebirth.PreRebirth` (AutoRebirth) or the
manual buttons; the advisor has no rebirth action of its own, and a rebirth by hand runs
`Yggdrasil.reset()` (resetFactor 0) — every sub-max tier is lost. With AutoRebirth off and
ManageYggdrasil on, any fruit holding a tier below max produces a reminder, shown at the head of
the Yggdrasil panel's advice line (Danger colour once the profile's `NextRebirthTargetSeconds` is
reached, muted otherwise — LRB profiles have no target). Once per run, when due, it is also written
to yggdrasil.log. It never harvests on its own: a guess that the player is about to rebirth would
throw away partial tiers just as surely.

## CheckFruits (activation)

Gated on `Settings.ActivateFruits`. Skips inactive (maxTier 0), permed (`permCostPaid`) and
already-active fruits. Activation needs the resource dumped first
(`removeMostEnergy/removeMostMagic`) because the game charges from the idle pool. Page math:
9 fruits/page — `ChangePage(slot)` switches page and returns the on-page index; the original page
is restored at the end.
