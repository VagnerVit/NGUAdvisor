# TransformManager (`Managers/TransformManager.cs`)

Transform-chain intelligence. Chain tables extracted VERBATIM from the decompiled
`InventoryController.checkItemTransform`: an item at level ≥ 100 transforms into the next tier
when a BOOST processes it.

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

`ChainItem(id)` is the membership test `InventoryAdvisor` uses to exempt chain tiers from TRASH.
`Tick()` is driven from `AdvisorApply` (always, unthrottled by the toggle — the per-chain settings
are the gate).
