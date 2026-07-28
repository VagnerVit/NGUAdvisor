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
| `IsPriority` | in `Settings.PriorityBoosts` |

## Two DIFFERENT exclusion sets — do not conflate them

- **Boost path** (`IsBlacklisted`): `Settings.BoostBlacklist` **+ `TransformManager.Frozen(x)`** —
  the per-COPY chain freeze on kept at-100 copies (boosting one would transform it).
- **Merge path** (`MergeBlocked`): chain items answer to their CLIMB toggle, NOT to the boost
  blacklist — a user who blacklists a chain item from boosting still wants spare copies merged.

## Main passes

- `GetBoostSlots` / `BoostInventory` — apply boosts, page-aware (`ChangePage`); priority items
  first, then equipped, per `Settings`.
- `BoostInfinityCube` — cube feeding (see BoostFarmAdvisor.md for when it's worthwhile: the game
  CLAMPS effective cube stats at base + gear).
- `MergeEquipped` / `MergeInventory` / `MergeBoosts` / `MergeGuffs` — merge passes per class.
- `ManageQuestItems` — quest item handling (they keep dropping past target and flood slots — see
  QuestManager's capstone-hold inventory guard).
- `ManageConvertibles`, `ManageBoostConversion`, `ShowBoostProgress` — boost conversion + the
  progress readout (F-key / panel).
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
