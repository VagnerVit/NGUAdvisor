# LoadoutManager (`Managers/LoadoutManager.cs`)

The equip executor: turns a list of item IDs into actual gear swaps via the game's
`InventoryController.swap*()` methods. Every optimizer/mode resolution ends here
(`GearOptimizer.OptimizeAndEquip`, `AdvisorApply` swaps). Main thread only.

## Equip mechanics (`ChangeGear`)

- **No-op guard**: if the requested id set equals the currently equipped set (order-insensitive,
  distinct, >0), nothing happens — prevents needless resource drops and log spam.
- **Resource drop before swapping**: `removeMostEnergy/removeMostMagic/removeAllRes3` — the game
  requires unallocated resources for cap-changing swaps; `UpdateResources()` at the end clamps
  cur back to the (possibly lower) new totals.
- Swap protocol per item: `Inventory.item2 = sourceSlot`, `Inventory.item1 = targetSlotCode`,
  then the matching `swapHead/Chest/Legs/Boots/Weapon/Weapon2/Acc()`. Target codes: −1 head,
  −2 chest, −3 legs, −4 boots, −5 weapon, −6 weapon2; accessories iterate from 10000.
- Each armor part swaps at most once per call; weapon2 only if `weapon2Unlocked()`.
- Items sitting in **daycare** (slot ≥ 100000) are pulled to a free inventory slot first
  (`InventoryManager.MoveFromDaycareToInventory`); failure (full inventory) skips the item.
- Missing ids are logged and skipped — the call never fails as a whole.
- Ends with `updateBonuses() + updateInventory()` — required, bonuses are cached by the game.

## Item resolution (`FindItemSlot`)

Searches equipped + inventory for the id; picks the copy by intent:
- normal equip: highest-level copy (`MaxItem()`); MacGuffins: LOWEST level (daycare wants the
  low copy so the high one stays equipped).
- `shockwave` (daycare-leveling): highest level BELOW 100 (level-100 gear can't gain; MacGuffins
  exempt — they don't hardcap at 100). May also pull from daycare when `MoneyPitDaycare` allows
  (completion below `DaycareThreshold`).

## Daycare round-trip (money-pit shockwave flow)

`SaveDaycare` snapshots ids → `FillDaycare` pushes `Settings.Shockwave` items in (evicting
non-shockwave items below the daycare completion threshold when `MoneyPitDaycare`) →
`RestoreDaycare` puts the original ids back, skipping items that hit level 100 meanwhile.

## Saved-loadout slots

Two independent stashes: `_savedLoadout` (`SaveCurrentLoadout`/`RestoreGear` — the "return to
what the user wore" pair used around temp swaps) and `_tempLoadout`
(`SaveTempLoadout`/`RestoreTempLoadout`). They are statics — advisor reload discards them.

## Gotchas

- `GetCurrentGear` orders head/boots/chest/legs/weapon(+w2)/accs — do not rely on slot order of
  the returned ids matching equip order elsewhere.
- `ChangeGear` with an EMPTY/null array returns silently (`gearIds?.Length > 0 == false`) — an
  optimizer returning no ids means "keep current gear", never "unequip".
