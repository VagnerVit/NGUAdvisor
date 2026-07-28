# Boosts panel UX — design

**Date:** 2026-07-28
**Status:** approved (user, this session)
**Scope:** `BoostsPanel`, `InventoryManager` boost/merge target selection, `InventoryAdvisor.AutoBoostPriority`,
`SavedSettings` (two new fields), one new form (`BoostPickerForm`).

## Problem

Three complaints about the BOOSTS page, in the user's words: reordering priorities up/down is painful,
adding items means typing item IDs, and the blacklist "isn't needed — if an item is removed from
priority boosts it won't be boosted".

The third premise was **false** when reported. `InventoryManager.GetBoostSlots` boosted three groups:

1. `Settings.PriorityBoosts` minus `Settings.BoostBlacklist`,
2. every EQUIPPED item not in the priority list and not blacklisted,
3. every LOCKED inventory item not in the priority list and not blacklisted.

So removing an item from the list did not stop it being boosted if it was worn or locked, and the
blacklist was the only "never boost this" lever. The blacklist also had a second, unrelated job:
blocking merges (`MergeBoosts`, `MergeBlocked`, `MergeBlockedId`) — a double purpose that had already
needed an exception carved out of it (`InventoryManager.cs:718`: "blacklisted Sir Lootys at lv 0/5/77
never merged").

The user's decision, once the finding was presented: **make the priority list the only source of
boosting** and drop the blacklist. That converts a false premise into a true one instead of papering
over it.

## Decisions

| Question | Decision |
|---|---|
| What drives boosting? | The priority list only. Equipped/locked auto-boosting is removed. |
| Blacklist | Removed from the UI and from both code paths. |
| Reordering | Drag & drop, built as a layer on top of buttons (user chose DnD knowing the Mono risk). |
| Adding items | Modal "Add from inventory" picker (mockup variant A). |
| Migration | One-time seed of the manual list from currently equipped **and locked-inventory** items. |

## 1. Boost target selection

`InventoryManager.GetBoostSlots(ih[] ci)` becomes:

- iterate `Settings.PriorityBoosts` **in list order** (order is the user's priority — unchanged
  semantics, the list has always been boosted top-down),
- resolve each id through `LoadoutManager.FindItemSlot`, keep it only if
  `equipment.isEquipment()`,
- exclude `TransformManager.Frozen(x)`,
- final filter stays: `GetNeededBoosts().Total() > 0`.

Groups 2 and 3 (equipped, locked inventory) are deleted.

`TransformManager.Frozen` exclusion is **retained and is not part of the blacklist cleanup**: a
maxed chain copy the user is holding back must not be boosted, because both the boost and the merge
path run the game's `checkItemTransform` and would trigger the transformation. It only ever shared a
function with the blacklist; it never shared a purpose.

`BoostInfinityCube` is untouched — the cube has its own `CubePriority` setting.

## 2. Merge path

`MergeBlocked(ih)`, `MergeBlockedId(int)` and `MergeBoosts` stop consulting the blacklist. Merging is
governed solely by the chain rules that actually govern it: `TransformManager.MergeAllowed(id)` and
`TransformManager.Frozen(x)`. Non-chain items always merge.

**Accepted consequence, stated explicitly:** it is no longer possible to stop a *boost item*
(ids 1–39) from merging. The user's blacklist is empty, so nothing is lost today, but this is
one-directional — restoring that capability would mean reintroducing a separate merge blocklist, not
reverting this change.

## 3. Settings

- **`BoostBlacklist` stays in `SavedSettings`** and stays persisted. It is no longer read. Rationale:
  removing a persisted property churns `MassUpdate`/validation and would drop user data that an older
  DLL still understands. Mark it unused in code with a pointer to this spec.
- **`bool BoostSeeded`** — makes the migration idempotent.
- **`bool BoostDragReorder`** (default `true`) — kill switch for drag & drop. If Mono's DnD
  misbehaves in the game, the user flips this in settings.json; buttons keep working. This exists
  because DnD is the one part of this design that cannot be verified outside the running game.

## 4. One-time migration

Runs once from `Main.Start`, on the main thread, **after** the settings round-trip
(`SaveSettings`/`FlushSettings`/`LoadSettings`) and before the automation loop starts — the item ids
are a live game read, so this cannot live in `SavedSettings`. If `!BoostSeeded`, append to
`PriorityBoosts` every id that is not already in the list, in this order:

1. **Equipped equipment** in slot order: weapon, weapon 2 (only when `weapon2Unlocked()`), head,
   chest, legs, boots, then accessories in slot order.
2. **Locked inventory equipment** (`!inventory[slot].removable && equipment.isEquipment()`) in slot
   order.

Then set `BoostSeeded = true` and log the resulting list, counting the two groups separately so the
log says where each entry came from.

Both groups are seeded so post-update boosting matches pre-update behavior exactly (user decision):
these are precisely the two implicit groups §1 removes. They are appended at the END, equipped before
locked, so existing priorities keep their ranking.

**Known wart, deliberately accepted:** the inventory padlock is an overloaded signal — the old boost
path read it as "boost this", while `MergeBoosts` reads it as "consolidate this"
(`IsBoost(x) && IsLocked(x) && !IsMaxxed(x)`). Seeding from it therefore imports items the user may
have locked for an unrelated reason. That is acceptable because the seed is a ONE-TIME snapshot into
an editable list: after it runs the padlock has no further influence on boosting, and the user prunes
what they don't want. This is the trade for a silent behavior change on update.

**Also clears `BoostBlacklist` (added 2026-07-28 during execution, user decision).** Task 3 revealed that
the blacklist has three consumers §2 did not account for: quest-item merging
(`InventoryManager.ManageQuestItems`) and MacGuffin merging (`MergeGuffs`, two call sites). Those keep
working as they are — but once §5 removes the blacklist from the UI, a non-empty array would keep
blocking those merges with no way for the user to see or edit it. So the migration empties the array and
logs what it contained, leaving the three consumers live but permanently unfed. Nobody is left with an
invisible active blacklist, and the old contents stay recoverable from inject.log.

The list transformation is extracted as a pure static helper with no game/Unity dependency:

```
static int[] SeedPriorityBoosts(int[] current, int[] equippedInSlotOrder, int[] lockedInSlotOrder)
```

so it can be unit-tested (dedup within and across both groups, order preservation, equipped-before-
locked, empty/null inputs, already-seeded no-op). It is the only place in this change where a mistake
would be silent.

## 5. `BoostPickerForm` (new)

Modal form owned by `SettingsForm`, opened from an `Add from inventory` button.

- **Search** box: substring match on name and on `#id`.
- **`Needs boosts only`** checkbox, default on — hides items with nothing left to receive.
- **List** (multi-select, owner-drawn via `UiTheme.StyleList`) with columns
  **Item / Level / Boosts left / Where**, where *Where* is `equipped` · `inventory` · `daycare`.
- Items already in the priority list render greyed and cannot be selected.
- **Sort:** equipped first, then descending `InventoryAdvisor.Last.Usage[id]` (how many
  objective-optimal loadouts include the item), then by name. Uses the CACHED verdict only — the
  picker must never trigger the ~60-pass optimizer sweep. **`Last` may be null** (advisor boost
  priority never ran this session): then the usage term is skipped and the sort is equipped-first,
  then name. No sweep is started to fill it.
- **`Boosts left`** column renders `GetNeededBoosts()` as `P <power> · T <toughness> · S <special>`,
  omitting zero components; an item with nothing left shows `—` and is only visible when
  `Needs boosts only` is off.
- Buttons: `Add selected` (appends to the end of the priority list, preserving picker order, and
  de-duplicating against the current list even though such rows are unselectable) and `Cancel`.
- Opening the picker is wrapped so a failure reports through `Activity.Failed` and cannot escape into
  the game loop.

All dimensions go through `UiTheme.S/SText/SCtl/ListH` per the DPI contract in
`docs/modules/ui-infra.md`; `UiLayout.Audit` must stay clean.

## 6. Reordering

**Buttons (the reliable path):** `Top`, `Up`, `Down`, `Bottom`, `Remove`. `SelectionMode.MultiExtended`
with contiguous-block moves. Keyboard: `Alt+↑/↓` move, `Alt+Home/End` to top/bottom. Selection is kept
visible by adjusting `TopIndex` after a move.

**Drag & drop (the layer on top):** `MouseDown` records the hit index, `DragOver` computes the
insertion index and draws an insertion line, `DragDrop` moves the selected block. Enabled only when
`BoostDragReorder`. Written so that any DnD failure leaves the button path intact.

**Live preview:** the MANUAL view gains the same "will boost now" readout the ADVISOR view has, filled
by calling **the same `GetBoostSlots`** the automation uses, so the panel cannot disagree with
behavior. Removing an item visibly removes it from what gets boosted.

## 7. Advisor mode fix (not optional)

`InventoryAdvisor.AutoBoostPriority` currently **excludes equipped items on purpose**, documented as
"Equipped gear is boosted first by the existing InventoryManager pass regardless of this list". That
assumption dies with §1, so in ADVISOR mode nothing worn would be boosted. The advisor's list must
therefore **lead with equipped items** (in slot order), followed by the existing ranking: KEEP items
by objective usage, then chain climbers.

## 8. Layout

Removing the blacklist list and its edit row frees vertical space; the priority list grows from 8 to
~14 rows, which addresses the scrolling half of the reordering complaint. Heights are re-derived from
content (`BoostsPanel` grows to its content inside the scrolling host — the one-scroll-owner rule).
The panel's header layout comment is updated to the new pre-flight numbers.

## 9. Verification

Game-coupled paths cannot be unit-tested; verification is explicit and manual:

1. `dotnet test` — 53 existing tests stay green, plus new tests for `SeedPriorityBoosts`.
2. In game: `debug.log` shows zero `UI AUDIT` lines and a sane `UI metrics:` line.
3. Manual pass: seed happens once and is logged; picker adds/filters/sorts; already-listed items are
   unselectable; buttons and keyboard reorder; DnD reorder; `BoostDragReorder=false` disables DnD only;
   removing an item drops it from the live readout; a maxed chain copy under "Keep max" is still not
   boosted.

## 10. Documentation to update

- `docs/modules/InventoryManager.md` — the "two DIFFERENT exclusion sets" section collapses to one.
- `docs/modules/InventoryAdvisor.md` — `AutoBoostPriority` now leads with equipped items.
- `docs/modules/ui-panels.md` — picker form + the panel's new interaction rules.
- `CHANGELOG.md` — behavior change: priority list is the only boost source; blacklist retired.

## Out of scope

Extracting a `BoostPlan` manager (considered as approach B and rejected: it rewrites parts of a
29 KB file full of invariants to solve a problem that was not reported). The live-preview benefit of B
is obtained here by having the panel call `GetBoostSlots` directly.
