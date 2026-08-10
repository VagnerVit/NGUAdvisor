# AP purchase advisor

Date: 2026-08-10
Status: approved, not yet implemented

An advisory module that answers "what should I spend AP on next?", ordered by the community AP tier
list, and grounded in what the game actually reports as owned and as costing.

Advise-only by owner decision — it never buys. AP is not refundable and the ordering is one player's
opinion; an auto-buyer would spend the user's points against someone else's ranking.

---

## Game truth established (decomp, `Assembly-CSharp.dll`)

AP is called **Arbitrary Points** internally.

| Concern | Where |
|---|---|
| Spendable balance | `character.arbitrary.curArbitraryPoints` (long) |
| Lifetime earned | `character.arbitrary.curLifetimePoints` |
| Award path | `Character.addAP(...)` → `arbitrary.curArbitraryPoints += …` |
| Purchase state | typed fields on `Arbitrary` (flags and counts) |
| Heart ownership | `character.inventory.itemList.itemDropped[id]` — the hearts are accessories, not flags |
| Costs | `ArbitraryController` exposes **84 cost accessors as methods**, e.g. `lootFilterCost()`, `acc4Cost()`, `beardSlotCost()` |

Costs are read live from those methods rather than copied as constants, so any in-game scaling
(`beardSlotCost()` rises per slot) is tracked for free and cannot drift from the game.

**Yellow Heart's Tier 0 placement is verifiable, not just opinion:** `Character.addAP` branches on
`inventory.itemList.itemMaxxed[129]` — a maxxed Yellow Heart multiplies AP by 1.2 instead of applying
the gear AP bonus. Where the decomp supports a tier-list claim like this, the table's `Note` says so.

## The binding: ask the game, do not hand-map fields

`ArbitraryController` is a **per-shop-entry MonoBehaviour** carrying an `int id`, and it exposes two
id-keyed switches that together are the whole model:

- `cost()` — `id switch { 7 => lootFilterCost(), 21 => yggdrasilReminderCost(), … }`
- `shouldDisableBuyButton(int id)` — a pure **"already owned / already maxed"** predicate:
  `case 7: return character.arbitrary.lootFilter;` … `case 25: return curLoadoutSlots >= maxLoadoutSpaces();`
  It does **not** consider affordability, so it is exactly the ownership question and nothing else.

So an entry stores only its **shop id**, and ownership comes from the game's own answer. That removes
the hand-written field mapping entirely — the class of bug where a mapping is wrong today or goes
stale when the game updates. It is the same rule the repo already applies elsewhere: prefer the
game's own method over a reimplementation, and ask the owning module rather than inferring.

The id map, read off `shouldDisableBuyButton` (authoritative, 2026-08-10):

| id | owns | id | owns | id | owns |
|---|---|---|---|---|---|
| 7 | `lootFilter` | 40 | `diggerSlots ≥ max` | 64 | `purchases.hasCustomRes3Percent1` |
| 8 | `improvedAutoBoostMerge` | 41 | `macguffinSlots ≥ max` | 65 | `purchases.hasCustomRes3Percent2` |
| 9 | `instaTrain` | 47 | `hasQuestLight` | 66 | `purchases.hasCustomIdleRes3Percent1` |
| 12 | custom E/M % set 1 | 48 | `hasFasterQuests` | 67 | `res3NameGeneratorBought` |
| 13 | custom E/M % set 2 | 49 | `hasExtendedQuestBank` | 68 | `wishSpeedBoster` |
| 15 | `inventorySpaces ≥ max` | 54 | `hasAcc6` | 69 | `invMergeSlots ≥ max` |
| 17 | `hasAcc4` | 55 | custom idle E/M % set 1 | 71 | `advLightBought` |
| 21 | `hasYggdrasilReminder` | 56 | `boughtAutoNuke` | 72 | `advAdvancerBought` |
| 22 | `hasExtendedSpinBank` | 57 | `boughtDaycareArt` | 73 | `goToQuestZoneBought` |
| 25 | `curLoadoutSlots ≥ max` | 58 | `hasNGUCapModifier` | 74 | `hasAcc8` |
| 28 | `beardSlots ≥ max` | 62 | `hasAcc7` | 75 | `deckSpaceBought ≥ max` |
| 29 | `hasCubeFilter` | 32 | `hasDaycareSpeed` | 76 | `mayoGenSlots ≥ max` |
| 34 | `hasAcc5` | 39 | `boughtLazyITOPOD` | 77 | `gotTagslot1` |
| 81 | `hasAcc9` | | | | |

This also settles the two open questions from the first draft:

- **Custom E/M/R3 % buttons** live on `character.purchases` (`hasCustomEnergyPercent1/2`,
  `hasCustomMagicPercent1/2`, `hasCustomIdle*`, `hasCustomRes3*`) — not on `arbitrary`, which is why
  the field was not found. Ids 12, 13, 55, 64, 65, 66.
- **Yggdrasil Harvest Light (21) vs Quest Reminder (47)** — the original mapping was right, and is
  now evidenced rather than assumed.

**Three kinds of entry, and only three:**

1. **One-time** — has an id in `shouldDisableBuyButton`. Owned = the game's answer.
2. **Hearts** — accessories, absent from that switch because they can be bought repeatedly to raise
   their level. "Do I have one at all" = `inventory.itemList.itemDropped[itemId]`; they are never
   "done", so they leave the queue once owned but are still listed.
3. **Repeatable** — PP, EXP, potions, poop, pills. No owned state; never blocks the queue.

## Architecture — the repo's pure/live split

Mirrors `TitanTables` + `OptimizationAdvisor` and `ItopodRewards` + `ItopodFarmAdvisor`.

### `Managers/ApTierTable.cs` — pure data, Unity-free, linked into the test project

```csharp
public enum ApSource { ShopId, Heart, Repeatable }

public class ApItem
{
    public string Name;      // as the tier list names it
    public int Tier;         // 0..7
    public int Rank;         // order within the tier, 1-based
    public ApSource Source;
    public int Key;          // ShopId: the ArbitraryController id · Heart: the item id · Repeatable: 0
    public string Note;      // why it ranks here; cites decomp where one exists
}
```

The whole tier list in order, plus `NextUnowned(...)` taking an ownership predicate — pure, so it is
unit-testable without the game.

**Why a bare `int Key` and not a delegate:** a delegate would drag `Character` into this file and cost
it the Unity-free property that makes it testable. The key is meaningless here on purpose —
`ApPurchaseAdvisor` is the only thing that knows what a `ShopId` or an item id resolves to.

### `Managers/ApPurchaseAdvisor.cs` — live binding (Unity-dependent)

- `Balance()` → `arbitrary.curArbitraryPoints`
- `Owned(ApItem)` → `ShopId`: `shouldDisableBuyButton(Key)` on any live `ArbitraryController`
  instance · `Heart`: `itemList.itemDropped[Key]` · `Repeatable`: always false
- `Cost(ApItem)` → the shop entry's own `cost()`, found by locating the `ArbitraryController` whose
  `id == Key`. Both accessors are instance members, so the advisor caches the id→controller map once
  (the shop components are created with the scene and do not churn).
- `Next()` → `ApRec { Known, Item, Cost, Affordable, Balance }`
- `Queue(int n)` → the next n unowned items in tier/rank order

Every read is individually guarded (the pattern `OptimizationAdvisor` already uses): a read that
fails yields `Known = false` for that row rather than throwing or, worse, silently reporting "not
owned" and recommending something the user already has.

### UI — a panel registered through `SystemCatalog`

Balance · the next recommended buy with cost and affordability · the queue behind it, grouped by
tier. Read-only; no button buys anything.

## The tier list, mapped to shop ids

`ShopId` rows are owned via `shouldDisableBuyButton(id)`; `Heart` rows via `itemList.itemDropped[id]`;
`Repeatable` rows are never owned.

**Count-based entries appear ONCE, at their earliest tier.** The tier list splits them
("AP Beard Slot 1" in Tier 1, "2" in Tier 2, "3-4" in Tier 2), but the game's predicate only answers
"can I buy another" vs "maxed" — it does not expose how many are bought without going back to
hand-mapped fields. Collapsing them keeps the binding honest; the per-count guidance moves into the
row's `Note`.

| Tier | # | Item | Source | Key |
|---|---|---|---|---|
| 0 | 1 | ILF (improved loot filter) | ShopId | 7 |
| 0 | 2 | Yellow Heart | Heart | 129 |
| 1 | 1 | Red Heart | Heart | 119 |
| 1 | 2 | AP Beard Slots | ShopId | 28 |
| 1 | 3 | Acc slot 1 | ShopId | 17 |
| 1 | 4 | Acc slot 2 | ShopId | 34 |
| 1 | 5 | Green Heart | Heart | 171 |
| 1 | 6 | Grey Heart | Heart | 297 |
| 1 | 7 | Pink Heart | Heart | 344 |
| 1 | 8 | Rainbow Heart | Heart | 390 |
| 2 | 1 | Digger slots | ShopId | 40 |
| 2 | 2 | Filter Boosts into Infinity Cube | ShopId | 29 |
| 2 | 3 | Blue Heart | Heart | 196 |
| 2 | 4 | Orange Heart | Heart | 293 |
| 2 | 5 | Faster Questing | ShopId | 48 |
| 2 | 6 | Faster Wishes | ShopId | 68 |
| 2 | 7 | Acc slot 3 | ShopId | 54 |
| 2 | 8 | Acc slot 4 | ShopId | 62 |
| 2 | 9 | MacGuffin slots | ShopId | 41 |
| 2 | 10 | Daycare Speed Boost | ShopId | 32 |
| 2 | 11 | Acc slot 5 (Evil) | ShopId | 74 |
| 2 | 12 | Acc slot 6 (Evil) | ShopId | 81 |
| 2 | 13 | Extra Tag Slot | ShopId | 77 |
| 3 | 1 | Insta Training Cap | ShopId | 9 |
| 3 | 2 | 1/2 Auto Merge and Boost Timers | ShopId | 8 |
| 3 | 3 | NGU Cap Modifier | ShopId | 58 |
| 3 | 4 | Extended Quest Bank | ShopId | 49 |
| 3 | 5 | Mayo Generator | ShopId | 76 |
| 3 | 6 | Extra Deck Size | ShopId | 75 |
| 4 | 1 | Loadout slots | ShopId | 25 |
| 4 | 2 | Lazy ITOPOD Floor Shifter | ShopId | 39 |
| 4 | 3 | Custom E/M % set 1 | ShopId | 12 |
| 4 | 4 | Custom E/M % set 2 | ShopId | 13 |
| 4 | 5 | Custom idle E/M % set 1 | ShopId | 55 |
| 4 | 6 | Custom R3 % set 1 | ShopId | 64 |
| 4 | 7 | Custom R3 % set 2 | ShopId | 65 |
| 4 | 8 | Custom idle R3 % set 1 | ShopId | 66 |
| 4 | 9 | Inventory Merge Slots | ShopId | 69 |
| 4 | 10 | Yggdrasil Harvest Light | ShopId | 21 |
| 4 | 11 | Adventure Light | ShopId | 71 |
| 5 | 1 | Brown Heart | Heart | 162 |
| 5 | 2 | PP | Repeatable | 0 |
| 5 | 3 | Inv. Space | ShopId | 15 |
| 5 | 4 | EXP | Repeatable | 0 |
| 6 | 1 | 'Go To Quest Zone' Button | ShopId | 73 |
| 6 | 2 | 7-Day Time Bank for Daily Spin | ShopId | 22 |
| 6 | 3 | Quest Reminder | ShopId | 47 |
| 6 | 4 | Auto Nuker | ShopId | 56 |
| 6 | 5 | Adventure Advancer | ShopId | 72 |
| 6 | 6 | Resource 3 Name Randomizer | ShopId | 67 |
| 7 | 1 | Purple Heart | Heart | 212 |

Not in the tier list but present in the shop, and therefore NOT in the table: `boughtDaycareArt` (57)
— the doc calls it out as unrateable ("A price cannot be put on the great Kitty").

Item ids come from `docs/ITEM-IDS.md`, which is generated from the game — re-check them against
`itemName[]` rather than trusting the table if a heart ever reads as owned when it is not.

## Provenance, stated in the UI and the docs

The ordering is **one player's opinion** — OJ of Steel's AP Tier List, build 1.200 — and its own
introduction admits uncertainty about late-game entries. The panel names the source and the build.
This is not decomp-derived game truth like the cadence rules, and the module must not present it as
though it were. Where a decomp fact does support a placement (Yellow Heart), the note says so.

## Testing

- `ApTierTable` is Unity-free and linked into `tests/NGUAdvisor.Tests`: every item has a tier, a rank
  and a cost accessor; ranks are unique within a tier; tiers span 0–7 with no gaps; `Item` rows carry
  a plausible id and `ArbitraryCount` rows a positive target; `NextUnowned` respects tier-then-rank
  order and skips owned entries; a repeatable entry never blocks the queue.
- `ApPurchaseAdvisor` has Unity dependencies and cannot be unit-tested. Verified by build plus a read
  against the live save: the balance matches the in-game number and already-owned entries do not
  appear as recommendations.
- Panel: `debug.log` must show no NEW `UI AUDIT` lines. **The baseline is 70, not zero** (measured
  2026-08-10 on the current build) — compare before/after, do not expect zero.

## Out of scope

Auto-buying. The PP and Advanced Training modules, which are separate deliverables.
