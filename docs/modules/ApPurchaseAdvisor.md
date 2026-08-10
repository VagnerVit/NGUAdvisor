# ApPurchaseAdvisor + ApTierTable + ApPanel (`Managers/ApPurchaseAdvisor.cs`, `Managers/ApTierTable.cs`, `ApPanel.cs`)

"What should I spend AP on next?" — a plan ordered by a community tier list, priced and gated by what
the running game actually reports. `ApTierTable` is the Unity-free plan (linked into
`tests/NGUAdvisor.Tests`); `ApPurchaseAdvisor` is the live binding; `ApPanel` is the read-only view on
the Economy page. Same pure/live split as `ItopodRewards` + `ItopodFarmAdvisor`.

> **Nothing here has been run against the game.** Every runtime check — live balance, ownership
> against a real save, the `UI AUDIT` pass — was deferred by the owner. What is proven is that the
> project compiles against the game's `Assembly-CSharp.dll` (so every member named below exists with
> the stated type), that 12 unit tests pin the table, and that the Release build is clean. Behaviour
> at runtime is unverified. Treat a first live run as the missing verification step, not as a
> regression hunt.

## AP is "Arbitrary Points"

Nothing in the game is called AP. The internal names:

| Concern | Where |
|---|---|
| Spendable balance | `character.arbitrary.curArbitraryPoints` (long) |
| Lifetime earned | `character.arbitrary.curLifetimePoints` |
| Award path | `Character.addAP(...)` |
| The shop | the `Arbitrary` system; one `ArbitraryController` MonoBehaviour **per shop entry**, each with a public `int id` |
| The pod list | `character.allArbitrary.arbitraryPods` (`List<ArbitraryController>`) — the same list `ConsumablesManager` already uses |

Grep for `arbitrary`, not for `ap`.

## The binding rule — ask the game, never hand-map fields

This is the whole design, and it is the one thing not to undo.

- **Ownership is `ArbitraryController.shouldDisableBuyButton(int id)`.** It is a pure
  *already-owned / already-maxed* predicate — case by case it returns the ownership flag
  (`case 7: return character.arbitrary.lootFilter;`) or a `count >= max()` check
  (`case 25: return curLoadoutSlots >= maxLoadoutSpaces();`). **It does not consider affordability**,
  which is exactly why it is the right ownership read and nothing else is needed.
- **Cost is that pod's own instance `long cost()`** — an id-keyed switch over the per-entry accessors
  (`lootFilterCost()`, `beardSlotCost()`, …). Read live, never copied as constants, so in-game
  scaling (`beardSlotCost()` rises per slot) tracks for free.

**A hand-mapped table of `Arbitrary` fields was considered and rejected.** It would have been one more
thing to drift: wrong the day the game adds or renumbers an entry, and wrong silently — a stale
mapping reads as "not owned" and recommends something already bought. Asking the game's own predicate
removes that entire class of bug. It is the repo's standing rule (game-truth first; ask the owning
module, do not reimplement it) applied to a shop.

Two facts the decomp settled that guesswork had got wrong, worth keeping written down:

- The **custom E/M/R3 % buttons** live on `character.purchases` (`hasCustomEnergyPercent1/2`,
  `hasCustomIdle*`, `hasCustomRes3*`), **not** on `arbitrary` — which is why searching `arbitrary` for
  them finds nothing. Ids 12, 13, 55, 64, 65, 66.
- **Yggdrasil Harvest Light is 21, Quest Reminder is 47** — evidenced off the switch, not assumed.

## Three kinds of entry, and only three

`ApSource` (`ApTierTable.cs:43`):

| Kind | Ownership | Why |
|---|---|---|
| `ShopId` | `shouldDisableBuyButton(Key)` | the entry is in that switch; the game answers |
| `Heart` | `inventory.itemList.itemDropped[Key]` | hearts are **accessories**, absent from the switch because they can be re-bought to raise their level — they are never "done", so the game has no owned/maxed answer to give |
| `Repeatable` | always `false` | PP and EXP have no owned state at all |

Current shape: 51 rows — 39 `ShopId`, 10 `Heart`, 2 `Repeatable`.

### `Key` vs `CostId` — ownership and pricing are different questions

`ApItem` carries **two** keys (`ApTierTable.cs:56`, `:71`). This is the one place the design spec
(`docs/superpowers/specs/2026-08-10-ap-purchase-advisor-design.md`) is stale: it used a single `Key`
for both, and the code no longer does.

- `Key` — the ownership key. A shop id for `ShopId`, an **item** id for `Heart`, `0` for `Repeatable`.
- `CostId` — the `ArbitraryController` id whose `cost()` prices the row. `0` means "use `Key`".

**Absence from `shouldDisableBuyButton` is an ownership fact and says nothing about pricing.** Hearts
and repeatables do have shop pods; they are simply owned one way and priced another. The 39 `ShopId`
rows leave `CostId` at 0 because for them the two keys genuinely are the same number. The heart pods
are Red 11, Yellow 14, Brown 31, Green 33, Blue 38, Purple 42, Orange 50, Grey 63, Pink 70,
Rainbow 80; PP 51, EXP 23. `EveryRowWhosePriceIsNotItsOwnKeyCarriesAnExplicitCostId` pins the split
bidirectionally, so neither a new heart without a price nor a `ShopId` row with a redundant `CostId`
can drift in.

Heart item ids were re-checked against `docs/ITEM-IDS.md` (all ten present as `accessory / HEART`).
If a heart ever reads as owned when it is not, re-check there rather than trusting this file.

### PP and EXP are priced at their cheapest tier

The game sells PP in three bundles (25 / 100 / 500 → pods 51 / 52 / 53) and EXP in three
(200 / 500 / 2K → pods 23 / 10 / 24). The rows price the **cheapest** so the panel shows an entry
price instead of nothing; the other tiers are named in the row's `Note` so the panel is not silently
quoting the smallest bundle as if it were the only one.

Consequence to expect on a live run: because `Repeatable` is never owned, the PP and EXP rows sit in
the queue permanently once tier 5 is reached, and tiers 6–7 always render behind them. That is
correct — they are never finished — not a bug to "fix" by hiding them.

## Count-based entries are collapsed to one row

Beard slots, digger slots, MacGuffin slots, inventory space, loadout slots, deck size, merge slots,
mayo generators — the tier list splits them ("AP Beard Slot 1" in Tier 1, "2" in Tier 2, "3-4" in
Tier 2). **The table carries one row, at the entry's earliest tier.**

The reason is the binding rule: the game's predicate answers *"can I buy another, or am I maxed"* and
does **not** expose how many are bought. Splitting a count entry across tiers would have meant going
back to hand-mapped `Arbitrary` count fields — the exact thing this design rejected — to tell row 2
from row 3. Collapsing keeps the binding honest; the per-count guidance moves into the row's `Note`.
Do not re-split these rows without first accepting a hand map.

## `Note` is a QUOTE, never our own words

`Note` is guidance transcribed **verbatim** from OJ of Steel's AP Tier List, build 1.200. Rows the
source says nothing about carry the **empty string**, and `ARowWithNoSourceGuidanceHasAnEmptyNote`
pins one of them (`Acc slot 1`).

This is deliberate and it is a correctness rule, not a style rule: the panel renders `Note` directly
under a sourced provenance line, so an invented note reads to the user as sourced advice from the tier
list. The first implementation pass filled every empty note with a plausible one-line description of
what the item does; all of them were removed. **Do not paraphrase into this field, and do not "fill in"
a missing note.**

The one carve-out is the marker `· [advisor] `. Everything after it is ours; everything before it is
the quote. Advisor text may never precede the marker and the quoted part is never edited. It currently
exists only on the PP and EXP rows, to name the bundle tiers the quote does not carry.

## Provenance — opinion and game truth on the same screen

Two different kinds of fact meet in this module and **a reader must not mistake one for the other**:

- **The ORDERING (tier and rank) is one player's opinion** — OJ of Steel's AP Tier List, build 1.200,
  whose own introduction admits uncertainty about late-game entries. It is a plan, not game truth.
- **The IDS and the ownership semantics ARE decomp-derived game truth**, transcribed off
  `ArbitraryController.shouldDisableBuyButton` / `cost()`. They must not be re-derived, re-ordered or
  "corrected" in the table.

The panel prints the provenance line (`ApPanel.cs:32`) for exactly this reason. It is data, not
decoration; do not drop it to save a row.

**Where decomp does back a placement, say so.** It backs exactly one: the **Yellow Heart's Tier 0**.
`Character.addAP` branches on `inventory.itemList.itemMaxxed[129]` — a maxxed Yellow Heart multiplies
AP itself by 1.2 instead of applying the usual gear AP bonus, so it compounds into every later
purchase. Its `Note` cites id 129 and `TheYellowHeartNoteRecordsItsDecompEvidence` pins the citation.

One shop entry is deliberately **absent** from the table: `boughtDaycareArt` (57). The source declines
to rate it ("A price cannot be put on the great Kitty"), so there is no ranking to transcribe.

## Failure behaviour — the deliberate risk

Every read is individually guarded and logs one `Main.LogDebug` line. The degradations:

| Guard | Degrades to |
|---|---|
| `Balance()` | `0` — zero balance, every row unaffordable |
| `Pods()` | `null` map; **the cache is left unset so the next call retries** |
| `ControllerFor` | `null` pod on an unknown id (no throw) |
| `Owned()` | `false` — **the accepted risk** |
| `Owned()` heart bounds check | `false` before indexing `itemDropped` |
| `TryCost()` | `CostKnown = false`, `Cost` forced to `0` |

**The accepted risk, stated plainly: every failure path in `Owned()` reports "not owned", so a broken
read makes the advisor recommend something the user has already bought.** That is the lesser evil
against throwing out of a once-per-second UI refresh — but it is a real failure mode, not a harmless
default, and it must stay visible. That visibility is `CostKnown`.

`Known` and `CostKnown` are separate facts produced by separate code paths that never share a `catch`.
`Known` = "we resolved which row this is"; `CostKnown` = "we got a price". A row can be perfectly
identified while its pod is missing (typically an entry the account has not unlocked yet, or the
window before the scene is up). **`Cost` is 0 whenever `CostKnown` is false, and that zero is absence
of data, not a free purchase** — the panel prints `cost unknown` and never renders it. `Affordable` is
also forced false without a price, and the panel prints the affordability verdict only inside the
`CostKnown` branch: "keep saving" beside a missing read would dress a data gap up as a verdict about
the balance.

**An empty pod map is never cached** (`Pods()` returns `null` and leaves `_pods` unset when the list is
null or yields zero entries). A cached empty map would answer "no controller" for every id, which
degrades to "nothing is owned", which would recommend the entire tier list from the top with every
price unknown. Null pods inside the list are skipped; duplicate ids are last-write-wins via the
indexer so one oddity cannot throw during map construction.

## The panel

`ApPanel` is Economy > AP PURCHASES: balance, a NEXT PURCHASE card, seven queue rows behind it, the
provenance line.

- **It calls `Queue(8)` once and renders row 0 as the card**, rows 1–7 as the queue.
  `ApPurchaseAdvisor.Next()` is not called from here. Calling both would take two independent balance
  snapshots and two independent ownership sweeps that can disagree; `Next()` is by construction
  `Queue(n)[0]`. This is why the list shows seven rows for a depth of eight.
- **Read-only, and there is nothing to click** — no button, no double-click handler, no context menu.
  That absence is the rule, not an omission.
- **Height is derived, never tuned**: the card's height is set from `_cardNote.Bottom` *after* its
  children exist, and `ContentHeight` from `provenance.Bottom`. The provenance text is constant and is
  laid out once in the constructor, so no refresh can change the panel's extent. It does not scroll —
  the Economy `ScrollPanel` owns that.
- **Main-thread rule**: the only live reads are in `SyncFromSettings()`, called from
  `SettingsForm.UpdateFromSettings` (the deferred ≤1/s Unity-main-thread pass) and from
  `VisibleChanged`. The whole body is one `try/catch` that logs, because a throwing assignment in that
  pass aborts every panel after it.

### Registered as a `SettingsIndex` Reference, not a tenth System

`SettingsIndex.cs:417` adds a `Ref`, not a `Sys`. It owns no setting, has no automation and no
advisor/manual choice, so a System entry would promise state and a gate that do not exist and
`SysCard` would have to invent AUTOMATION/ADVISOR chips for it. It would also break
`SystemIndexPanel`'s audited parity check — "all NINE catalogue systems render" — by making it nine of
ten. `Destinations.ApPurchases = "Economy"`, sharing Gold's and Pit's route, which that file already
allows.

## Advise-only, permanently

Nothing in these three files buys anything, and nothing in them may be made to — not behind a flag.
AP is **not refundable** and the ordering is one player's opinion, so an auto-buyer would spend the
user's points against someone else's ranking, irreversibly. The panel advises; the player spends.

## Tests

`tests/NGUAdvisor.Tests/ApTierTableTests.cs`, 12 facts: every row has a name/tier/rank; ranks are
unique and contiguous within a tier; tiers 0–7 with no gaps; `ShopId`/`Heart` rows have a positive
`Key` and `Repeatable` rows have 0; keys unique within a source; `NextUnowned` walks tier-then-rank
and returns null when everything is owned; a `Repeatable` row never blocks the queue; the Yellow Heart
note keeps its decomp citation; the `Key`/`CostId` split; the empty-note rule.

`ApPurchaseAdvisor` and `ApPanel` are Unity-dependent and cannot be unit-tested — build only.

The `UI AUDIT` oracle has never been run against this panel. Note that it could not have been at
first: `ApPanel` was missing from `SettingsForm`'s audit list entirely and was only added later,
alongside `PpPanel` — so an "audit is clean" reading before that change meant "not checked", not
"no defects".

> **MEASURED 2026-08-10 against build `260810-1616`: `UI AUDIT` reports ZERO issues across all 26
> audited panels**, this module's included. The docs' standing "the audit must be zero" rule holds —
> earlier notes in this repo claiming a non-zero baseline of 70 were wrong, and are corrected here.
>
> Those 70 came from a run that calibrated at `scale 1.00` (`UI metrics: … line 25, head 22`), where
> every single finding was the same class: `CONTROL TOO SHORT FOR TEXT 'ComboBox:<text>' h=24 < 25`.
> On the real display calibration (`scale 1.52`, `line 38, head 33`) they all fit and the audit is
> clean. **So read the `UI metrics` line BEFORE believing a dirty audit** — a wall of
> `CONTROL TOO SHORT` lines is far more likely to mean the advisor calibrated on an unscaled context
> than that the panels are broken.
