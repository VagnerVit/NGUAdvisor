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

## Boost targets: the priority list, and nothing else

`GetBoostSlots` returns exactly `Settings.PriorityBoosts`, in list order, filtered to equipment that
still needs boosts and is not `TransformManager.Frozen`. It used to also include every equipped item
and every locked inventory item implicitly — see
`docs/superpowers/specs/2026-07-28-boosts-panel-ux-design.md` for why that went away and how existing
lists were seeded.

**Merge exclusions are chain rules only.** `MergeBlocked`/`MergeBlockedId` consult
`TransformManager.MergeAllowed` + `Frozen`; non-chain items always merge. The retired boost blacklist
used to serve here too, which is why it once needed an exception carved out of it (blacklisted Sir
Lootys at lv 0/5/77 never merged).

`IsBlacklisted` itself is not dead: quest-item merging (`ManageQuestItems`) and MacGuffin merging
(`MergeGuffs`, two call sites) still consult `Settings.BoostBlacklist` — those were deliberately left
alone. `Main.SeedBoostPriorityOnce()` empties the blacklist during its one-time migration (and logs
what it contained) so nobody is left with an invisible active blacklist; `SavedSettings.BoostBlacklist`
stays a persisted field so `settings.json` round-trips and a rollback to an older DLL still finds the
data.

## Main passes

- `GetBoostSlots` / `BoostInventory` — apply boosts, page-aware (`ChangePage`); the priority list,
  in list order, is the only source (see above).
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
