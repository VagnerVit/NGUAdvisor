# InventoryManager (`Managers/InventoryManager.cs`)

Inventory automation: boost application, merging, quest/MacGuffin/convertible handling, loot
filtering. Operates on `ih[]` inventory-helper snapshots (`GetConvertedInventory`).

## Item-class predicates (game id ranges — the vocabulary everything else uses)

| Predicate | Rule |
|---|---|
| `IsBoost` | id 1–39 |
| `IsQuest` | id 278–287 |
| `IsCooking` | id 367–372 |
| `IsGuff` | in `macguffinList` |
| `IsMaxxed` | `itemList.itemMaxxed[id]` |
| `IsLocked` | `!inventory[slot].removable` |

## Boost targets: the priority list, minus the blacklist

`GetBoostSlots` returns exactly `Settings.PriorityBoosts`, in list order, filtered to equipment that
still needs boosts, is not `TransformManager.Frozen`, and is not on `Settings.BoostBlacklist`. It used
to also include every equipped item and every locked inventory item implicitly — see
`docs/superpowers/specs/2026-07-28-boosts-panel-ux-design.md` for why that went away and how existing
lists were seeded.

### The blacklist (retired 2026-07-28, restored 2026-08-26 on user request)

**Two readers, and both are needed.** `GetBoostSlots` is the hard gate — an id on the blacklist is
never boosted, whatever put it in the priority list. `InventoryAdvisor.AutoBoostPriority` filters the
same list out of the order it returns, because in ADVISOR ACTIVE mode
`AdvisorApply.ApplyBoostPriority` rewrites `PriorityBoosts` from it every 10 minutes: without that
filter, removing an item by hand comes back on its own — the user-reported reason the blacklist was
brought back at all. `InventoryManager.BoostBlacklisted(int)` is the public reader for both.

**It is boost-only. Do not wire it back into merging.** `MergeBlocked`/`MergeBlockedId` consult
`TransformManager.MergeAllowed` + `Frozen`; non-chain items always merge. Serving merges too is what
once forced an exception out of it (blacklisted Sir Lootys at lv 0/5/77 never merged). `IsBlacklisted`
is also still read by quest-item merging (`ManageQuestItems`) and MacGuffin merging (`MergeGuffs`, two
call sites) — deliberately left alone through both the retirement and the restore.

**`Frozen` is a different lever.** Frozen is the advisor protecting a transform chain (applying a boost
runs the game's `checkItemTransform`); the blacklist is the user saying "never this item". Both gate
`GetBoostSlots`, neither replaces the other.

**Mutually exclusive with the priority list.** `BoostsPanel` keeps an id out of both: blacklisting it
drops it from `PriorityBoosts`, re-adding it to `PriorityBoosts` drops it from the blacklist. Not
cosmetic — `BoostSinks`/`BoostFarmAdvisor` price the priority list without consulting the gate, so an
id on both lists would make their boost-value estimate wrong.

**`Main.SeedBoostPriorityOnce()` must not clear it.** It did during the retirement migration; that
code is gone. The seed is deferred until the inventory is populated, so it can still run for the first
time long after the restore, and clearing would delete a live setting.

## Main passes

- `GetBoostSlots` / `BoostInventory` — apply boosts, page-aware (`ChangePage`); the priority list,
  in list order, is the only source (see above).
- `BoostInfinityCube` — cube feeding (see BoostFarmAdvisor.md for when it's worthwhile: the game
  CLAMPS effective cube stats at base + gear).
- `MergeEquipped` / `MergeInventory` / `MergeBoosts` / `MergeGuffs` — merge passes per class.
- `ManageQuestItems` — quest item handling (they keep dropping past target and flood slots — see
  QuestManager's capstone-hold inventory guard).
- `ManageConvertibles`, `ManageBoostConversion`, `ShowBoostProgress` — boost conversion + the
  progress readout (F-key / panel). **`ManageBoostConversion` no longer picks the auto-transform
  type.** It used to — locked boost, then `BoostPriority` against the gear's need, then `CubePriority`,
  all through the game's `selectAuto*Transform()` setters — and that made it a second, unnamed owner
  of `settings.autoTransform`. When `TransformManager` gained the user-facing P/T/S/X control the two
  fought every ~30 s. The decision moved to `TransformManager.AdvisedType` (TransformManager.md); all
  that remains here is unlocking maxed padlocked boosts. `BoostPriority` and `CubePriority` no longer
  influence the transform type — `BoostSinks` prices gear headroom and cube softcap directly.
- `ManageFavoredMacguffin` / `RestoreMacguffins` — the favored-MacGuffin swap used by
  YggdrasilManager's harvest and blood spells. **`RestoreMacguffins` is self-guarding on
  `_savedMacguffins`** — it no-ops unless a swap is outstanding, which is what makes the harvest
  cleanup path safe to call unconditionally.
- `MoveFromDaycareToInventory` / `MoveFromMacguffinsToInventory` — slot moves (daycare slots are
  encoded as index + 100000; see LoadoutManager.md).
- `EnsureFiltered` / `FilterItem` / `FilterEquip` — writes the game's loot filter. **A filtered
  item never drops** — GearFarmAdvisor and QuestManager's capstone hold both check
  `itemFiltered` for exactly this reason.

`NeededBoosts.Total() = power + toughness + special` — the "does this item still want boosts"
signal used by BoostFarmAdvisor's demand gate and InventoryAdvisor's boost priority.
