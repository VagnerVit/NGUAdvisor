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

## Harvest triggers (`NeedsHarvest`)

`forced` → any harvestable fruit. Otherwise: any fruit maxed (`anyFruitMaxxed`) OR
`MacguffinFruit2Ready()` OR `QPFruitReady()`.

- **MacGuffin fruit 2 (index 13)**: eat-now-vs-wait math using the game's own yield chain —
  `tierFactor × 0.1 × poopModifier × equipYggYield × yggdrasilYieldBonus × harvestBonus`. Eats
  when the per-tier value of harvesting NOW (plus tier-1 harvests for the remaining tiers) beats
  the per-tier value of waiting for max tier. `EquipYggdrasilYield` reads the CONFIGURED ygg
  loadout's Yggdrasil specs (`spec*Cur / 1e7`), not equipped gear.
- **QP fruit (index 14)**: eaten only when the swap threshold is satisfied, poop is off, and the
  ITOPOD harvest bonus is 1 (no first-harvest bonus to waste).
- `NeedsSwap()` (gear swap worthwhile): a maxed fruit at or above `YggSwapThreshold`.

## HarvestAll details

- Favored-MacGuffin dance: swap the favored MacGuffin in (`ManageFavoredMacguffin`), consume fruit
  10, then `RestoreMacguffins()`. `tierOver1` (manual "harvest all tiers") consumes at any tier;
  the normal path requires the MacGuffin fruit to be at max tier.
- `ReadTooltipLog(false)` before / `(true)` after: the game's tooltip event log is marked with a
  `<b></b>` sentinel so only NEW harvest lines get written to pitspin.log (never overwritten
  across sessions).

## CheckFruits (activation)

Gated on `Settings.ActivateFruits`. Skips inactive (maxTier 0), permed (`permCostPaid`) and
already-active fruits. Activation needs the resource dumped first
(`removeMostEnergy/removeMostMagic`) because the game charges from the idle pool. Page math:
9 fruits/page — `ChangePage(slot)` switches page and returns the on-page index; the original page
is restored at the end.
