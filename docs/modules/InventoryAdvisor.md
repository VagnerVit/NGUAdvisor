# InventoryAdvisor (`Managers/InventoryAdvisor.cs`)

KEEP/TRASH verdicts for owned equipment, plus the advisor's boost-priority list.

## Verdict logic

**KEEP** = the item earns a slot in at least one gear objective's optimal loadout (runs
`GearOptimizer.OptimizeIds` for EVERY objective, both with and without the respawn pin — ~60
optimizer passes, hence the cached `Last`), OR appears in a configured static loadout
(Titan/Gold/Quest/Ygg/Cooking), OR is currently worn.

**TRASH** = owned equipment that wins nothing anywhere at max level — with two user-rule
exemptions that are relabeled KEEP instead:

- `[chain]` — a `TransformManager.ChainItem` tier: consolidation/climb fodder, never trash.
- `[max first]` — not yet `itemMaxxed`: the item still owes its permanent item-list max bonus.

Verdicts are per item ID — duplicate copies of a KEEP item are merge fodder, not trash (the UI
carries that caveat). `Usage[id]` counts how many objective-optimal loadouts include the item.

## AutoBoostPriority

**Equipped gear first** (in slot order), then unequipped KEEP items that still need boosts
(`GetNeededBoosts().Total() > 0`), ranked by objective `Usage`, then transform-chain climbers. Equipped
items lead because since 2026-07-28 the priority list is the ONLY boost source — the old "equipped is
boosted by the manager pass regardless" assumption is gone. Fully-boosted items neither rank nor
display. Written into `Settings.PriorityBoosts` by `AdvisorApply.ApplyBoostPriority` (10-min throttle —
this is the expensive sweep).

Equipped gear here is ordered by `LoadoutManager.CurrentGearIds()` (head, boots, chest, legs, weapon,
weapon 2, accessories), which differs from the one-time migration's weapon-first seed order (weapon,
weapon 2, head, chest, legs, boots, accessories) — see `BoostSeed.SeedPriorityBoosts`. Both are stable;
no spec pins a single permutation, so this is not a bug.
