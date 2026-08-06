# TransformManager (`Managers/TransformManager.cs`)

Transform-chain intelligence. Chain tables extracted VERBATIM from the decompiled
`InventoryController.checkItemTransform`: an item at level ≥ 100 transforms into the next tier.

## What actually triggers a transform (corrected — the old claim here was wrong)

This file used to say the trigger is "when a BOOST processes it". **It is not.** No boost path calls
`checkItemTransform` — verified by enumerating every call site in the decomp:

| trigger | where |
|---|---|
| a MERGE, gated on `mergeable()` | `swapHead/swapChest/swapLegs/swapBoots/swapWeapon/swapWeapon2/swapAcc/swapMacguffin/swapItems` |
| consuming the item directly | `ItemController.consumeItem()` |
| (display only) | `itemTooltipText` — the purple `TRANSFORMABLE` tag |

`applyAllBoosts`/`boostEquip` never call it, and they cannot: boosts raise `curAttack`/`curDefense`/
spec values, never `level`, and the transform is a level ≥ 100 test.

Two further game rules that matter here:

- **A padlocked at-100 copy can never transform.** `consumeItem()` requires `removable`, and
  `mergeable()` returns false when either side is at-100 and not removable. The transform CONSUMES the
  item (`deleteItem` + `makeLoot(next)` at level 1), so the game refuses to spend a protected one.
- **Equipped accessories cannot transform in place** — `swapAcc` shows "You need to move the equipped
  item into your inventory to transform it!". Equipped head/chest/legs/boots/weapons DO transform in
  place (`deleteHead(); head = genLoot(next)`).

## Chains (game data)

| Chain | Tiers |
|---|---|
| Pendant | 53, 76, 94, 142, 170, 229, 295, 388, 430, 504, 480 |
| Looty | 67, 128, 169, 230, 296, 389, 431, 505, 485 |
| Chain #120 | 120, 121 |
| Chain #154 | 154, 159 |
| Chain #195 | 195, 506 — **last hop requires Sadistic** (`SadisticGate`) |

`Read(chainIndex)` scans gear slots + inventory for the HIGHEST owned tier (keeping the
highest-level copy per id) and reports `OwnedTier/OwnedId/Level/NextId`; `NextId = −1` means top
of chain or gated.

## Freeze semantics (v2 — per COPY, not per ID)

v1 froze by item ID, which left spare copies of a held item unmerged forever (user-reported as
"3× Sir Looty"). The insight: the game's own `mergeAll` refuses to merge any at-100 equipment
(both sides must be < 100) and inventory merges never consume equipped items — so **merging is
natively safe** and freezing only needs to stop BOOSTS on at-100 copies (the transform trigger).

Per-chain modes, driven by the user toggles (Settings arrays indexed by chain — AUTO-CLIMB,
KEEP MAX LVL, FILTER LOWER):

- **Auto-climb OFF → HoldAll**: every at-100 copy is boost-frozen; sub-100 spares still merge.
- **Keep-max ON + climb ON → KeepOne**: exactly ONE at-100 copy stays frozen (equipped preferred,
  else lowest inventory slot); further at-100 copies get boosted and transform — the chain climbs
  while the kept copy keeps its stats. **KeepOne applies to the HIGHEST OWNED tier only**
  (`Read(i).OwnedTier`). Holding it on every tier was a bug: an at-100 Forest Pendant (#53) stayed
  frozen reading TRANSFORMABLE while the chain was already at Ascended x2, stranding 100 levels of
  merges at the bottom. A maxed copy is worth keeping for the stats of the tier you wear, never for
  an obsolete one. HoldAll still covers every tier — climb OFF means no transform anywhere.
- **FILTER LOWER** writes the game's own loot-filter list for tiers below the highest owned.

## `ActiveClimb` must respect the padlock (regression guard)

`ActiveClimb` is the executor: it scans INVENTORY slots only (equipped accessories can't transform
anyway) and does `deleteItem(slot)` + `makeLoot(next, slot)`.

**`if (!e.removable) continue;` is load-bearing.** It was missing, and that lost a user's padlocked
maxed Ascended Forest Pendant: `MaxItem()` biases merges toward the locked copy (`Extensions.cs`,
`locked => level + 101`) precisely so the protected one survives, which means the locked copy is the one
that reaches level 100 first — and then the climb consumed it and minted a level-1 Ascended x2 while a
lower unlocked copy was designated "kept". The padlock outranks the KeepOne slot heuristic: it is the
user's explicit veto, and the game itself honours it on every other path.

Note the KeepOne designation picks by SLOT INDEX (equipped first, else lowest inventory slot), not by
level or lock — so the padlock is the only way to pin a specific copy.

`ChainItem(id)` is the membership test `InventoryAdvisor` uses to exempt chain tiers from TRASH.
`Tick()` is driven from `AdvisorApply` (always, unthrottled by the toggle — the per-chain settings
are the gate).
