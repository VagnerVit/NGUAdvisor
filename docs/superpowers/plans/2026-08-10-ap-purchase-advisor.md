# AP Purchase Advisor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An advise-only module that says what to spend AP on next, ordered by the community AP tier list, with ownership and cost read from the game rather than hand-mapped.

**Architecture:** The repo's pure/live split. `ApTierTable` is Unity-free ordered data (linked into the test project). `ApPurchaseAdvisor` binds it to the live game: ownership from `ArbitraryController.shouldDisableBuyButton(id)`, cost from that entry's `cost()`, balance from `arbitrary.curArbitraryPoints`. A read-only panel renders it.

**Tech Stack:** C# net48 (game DLL) / net9.0 xUnit (tests), WinForms.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-10-ap-purchase-advisor-design.md`. **Read it first** — it carries the full 50-row tier table with shop ids, and the decomp evidence behind every mapping.
- **net48 is mandatory** for `NGUAdvisor.csproj`. Never change `TargetFramework`.
- Build: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release` · Tests: `dotnet test tests/NGUAdvisor.Tests` (currently **183 passing**; keep them green).
- **DO NOT COMMIT.** No `git commit`, `git add`, `git push`, no PR. The repo owner forbids it. Leave changes in the working tree.
- **Advise-only.** Nothing in this module may buy anything, and no panel control may trigger a purchase.
- `ApTierTable.cs` must stay **Unity-free** — no `Main.`, `Character`, `UnityEngine`. It is linked into the net9.0 test assembly.
- **Main-thread rule:** panel handlers must not call game code that mutates state. Live reads follow the pattern the sibling panels already use.
- **DPI:** every hand-placed pixel via `UiTheme.S(n)`; anything holding text sized from `SText`/`SHead`/`SCtl`/`SLines`; button widths from `UiLayout.BtnWidth`. Read `docs/modules/ui-infra.md` §DPI calibration before placing a control.
- **The `UI AUDIT` baseline is 70 lines, not zero** (measured 2026-08-10 on the current build). Any UI check is a before/after comparison, never "expect zero".
- Match each file's existing style (`Managers/` uses `var` and expression-bodied members).

## File Structure

| File | Responsibility |
|---|---|
| `NGUAdvisor/Managers/ApTierTable.cs` | **Create** — `ApSource`, `ApItem`, the ordered table, `NextUnowned`. Unity-free |
| `NGUAdvisor/Managers/ApPurchaseAdvisor.cs` | **Create** — live binding: balance, ownership, cost, `Next()`, `Queue(n)` |
| `NGUAdvisor/ApPanel.cs` | **Create** — read-only panel |
| `NGUAdvisor/Managers/SystemCatalog.cs` + `Destinations.cs` | **Modify** — register the panel the way siblings are registered |
| `tests/NGUAdvisor.Tests/ApTierTableTests.cs` | **Create** |
| `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj` | **Modify** — link `ApTierTable.cs` |
| `docs/modules/ApPurchaseAdvisor.md` | **Create** |

---

### Task 1: `ApTierTable` — the ordered data (TDD)

**Files:**
- Create: `NGUAdvisor/Managers/ApTierTable.cs`
- Create: `tests/NGUAdvisor.Tests/ApTierTableTests.cs`
- Modify: `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: `ApSource { ShopId, Heart, Repeatable }`; `ApItem { string Name; int Tier; int Rank; ApSource Source; int Key; string Note; }`; `IReadOnlyList<ApItem> ApTierTable.Items`; `IEnumerable<ApItem> ApTierTable.Unowned(Func<ApItem,bool> owned)`; `ApItem ApTierTable.NextUnowned(Func<ApItem,bool> owned)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/NGUAdvisor.Tests/ApTierTableTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class ApTierTableTests
    {
        [Fact]
        public void EveryItemHasANameATierAndARank()
        {
            foreach (var i in ApTierTable.Items)
            {
                Assert.False(string.IsNullOrWhiteSpace(i.Name));
                Assert.InRange(i.Tier, 0, 7);
                Assert.True(i.Rank >= 1);
            }
        }

        [Fact]
        public void RanksAreUniqueAndContiguousWithinEachTier()
        {
            foreach (var g in ApTierTable.Items.GroupBy(x => x.Tier))
            {
                var ranks = g.Select(x => x.Rank).OrderBy(x => x).ToList();
                Assert.Equal(ranks.Distinct().Count(), ranks.Count);
                Assert.Equal(Enumerable.Range(1, ranks.Count).ToList(), ranks);
            }
        }

        [Fact]
        public void TiersZeroThroughSevenAreAllPresent()
        {
            var tiers = ApTierTable.Items.Select(x => x.Tier).Distinct().OrderBy(x => x).ToList();
            Assert.Equal(Enumerable.Range(0, 8).ToList(), tiers);
        }

        [Fact]
        public void ShopIdAndHeartRowsCarryAPositiveKeyAndRepeatableRowsDoNot()
        {
            foreach (var i in ApTierTable.Items)
            {
                if (i.Source == ApSource.Repeatable) Assert.Equal(0, i.Key);
                else Assert.True(i.Key > 0, $"{i.Name} has no key");
            }
        }

        [Fact]
        public void KeysAreUniqueWithinEachSource()
        {
            foreach (var g in ApTierTable.Items.Where(x => x.Source != ApSource.Repeatable)
                                               .GroupBy(x => x.Source))
            {
                var keys = g.Select(x => x.Key).ToList();
                Assert.Equal(keys.Distinct().Count(), keys.Count);
            }
        }

        [Fact]
        public void NextUnownedWalksTierThenRank()
        {
            var owned = new HashSet<string> { "ILF (improved loot filter)" };
            var next = ApTierTable.NextUnowned(i => owned.Contains(i.Name));
            Assert.Equal("Yellow Heart", next.Name);
        }

        [Fact]
        public void NextUnownedReturnsNullWhenEverythingIsOwned()
        {
            Assert.Null(ApTierTable.NextUnowned(_ => true));
        }

        [Fact]
        public void ARepeatableRowIsNeverConsideredOwnedSoItCannotBlockTheQueue()
        {
            // The caller reports Repeatable rows as not-owned; the table must still order them
            // normally rather than special-casing them out of the list.
            Assert.Contains(ApTierTable.Items, i => i.Source == ApSource.Repeatable);
            var all = ApTierTable.Unowned(_ => false).ToList();
            Assert.Equal(ApTierTable.Items.Count, all.Count);
        }

        [Fact]
        public void TheYellowHeartNoteRecordsItsDecompEvidence()
        {
            var yh = ApTierTable.Items.Single(i => i.Name == "Yellow Heart");
            Assert.Contains("129", yh.Note);
        }
    }
}
```

- [ ] **Step 2: Link the table into the test project**

In `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj`, add to the existing `<Compile Include=...>` group:

```xml
    <Compile Include="..\..\NGUAdvisor\Managers\ApTierTable.cs" Link="Linked\ApTierTable.cs" />
```

- [ ] **Step 3: Run the tests and watch them fail for the right reason**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~ApTierTableTests"`
Expected: FAIL — compile error, `ApTierTable` does not exist.

- [ ] **Step 4: Write `ApTierTable.cs`**

Transcribe **the table in the spec** (`docs/superpowers/specs/2026-08-10-ap-purchase-advisor-design.md`, section "The tier list, mapped to shop ids") — all 50 rows, in order, keys exactly as given. Do not re-derive the ids; they were read off `ArbitraryController.shouldDisableBuyButton` and are recorded there.

Header comment must state: the ordering is OJ of Steel's AP Tier List, build 1.200 — one player's opinion, not decomp-derived game truth — while the ids and ownership semantics ARE decomp-derived. Notes carry the tier list's own per-item guidance; the Yellow Heart note cites `Character.addAP`'s `itemMaxxed[129]` branch (maxxed Yellow Heart multiplies AP by 1.2).

Class shape:

```csharp
public enum ApSource { ShopId, Heart, Repeatable }

public class ApItem
{
    public readonly string Name;
    public readonly int Tier;
    public readonly int Rank;
    public readonly ApSource Source;
    public readonly int Key;
    public readonly string Note;
    public ApItem(string name, int tier, int rank, ApSource source, int key, string note)
    { Name = name; Tier = tier; Rank = rank; Source = source; Key = key; Note = note; }
}

public static class ApTierTable
{
    public static readonly IReadOnlyList<ApItem> Items = new List<ApItem> { /* 50 rows */ };

    public static IEnumerable<ApItem> Unowned(Func<ApItem, bool> owned)
        => Items.Where(i => !owned(i));

    public static ApItem NextUnowned(Func<ApItem, bool> owned)
        => Unowned(owned).FirstOrDefault();
}
```

`Items` is already declared in tier-then-rank order, so `Unowned` must not re-sort — the declaration order IS the plan order, and a sort would silently hide a mis-ranked row that the tests above are meant to catch.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~ApTierTableTests"`
Expected: PASS, 9 tests.

- [ ] **Step 6: Full suite + game build**

Run: `dotnet test tests/NGUAdvisor.Tests` → 192 passing (183 + 9).
Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release` → 0 warnings, 0 errors.

- [ ] **Step 7: Stop for review.** Do not commit.

---

### Task 2: `ApPurchaseAdvisor` — the live binding

**Files:**
- Create: `NGUAdvisor/Managers/ApPurchaseAdvisor.cs`

**Interfaces:**
- Consumes: `ApTierTable.Items`, `ApItem`, `ApSource` (Task 1).
- Produces:
  - `struct ApRec { bool Known; ApItem Item; long Cost; bool CostKnown; bool Affordable; long Balance; }`
  - `long ApPurchaseAdvisor.Balance()`
  - `bool ApPurchaseAdvisor.Owned(ApItem)`
  - `ApRec ApPurchaseAdvisor.Next()`
  - `IReadOnlyList<ApRec> ApPurchaseAdvisor.Queue(int n)`

**Game truth to bind against** (all recorded in the spec, verified from `Assembly-CSharp.dll`):

- Balance: `Main.Character.arbitrary.curArbitraryPoints` (long).
- `ArbitraryController` is a per-shop-entry MonoBehaviour with a public `int id`, an instance
  `long cost()`, and `bool shouldDisableBuyButton(int id)`.
- `shouldDisableBuyButton` is a pure owned/maxed predicate — it does NOT consider affordability.
- Hearts are not in that switch: ownership is `Main.Character.inventory.itemList.itemDropped[itemId]`.

- [ ] **Step 1: Locate the live controllers**

The advisor needs an id→`ArbitraryController` map. Find how the game exposes the shop entries — check `AllArbitraryController` first (it is the plural/owning controller), and fall back to `UnityEngine.Object.FindObjectsOfType<ArbitraryController>()` only if there is no owning list. Cache the map; the shop components come with the scene and do not churn.

**If you cannot find a reliable way to reach them, STOP and report** — do not substitute a hardcoded cost table. Costs read live are the whole point; a copied constant would drift from the game and is exactly what this design rejects.

- [ ] **Step 2: Implement**

```csharp
public static long Balance()
{
    try { return Main.Character.arbitrary.curArbitraryPoints; } catch { return 0; }
}

public static bool Owned(ApItem item)
{
    try
    {
        switch (item.Source)
        {
            case ApSource.ShopId:  return ControllerFor(item.Key)?.shouldDisableBuyButton(item.Key) ?? false;
            case ApSource.Heart:   var l = Main.Character.inventory.itemList.itemDropped;
                                   return item.Key < l.Count && l[item.Key];
            default:               return false;   // Repeatable never completes
        }
    }
    catch { return false; }
}
```

**The `catch { return false; }` is a deliberate, documented risk** and must carry a comment: a failed
read reports "not owned", so a broken read makes the advisor recommend something already bought. That
is the lesser evil against throwing out of a 1 s UI refresh, but the panel must show `Known = false`
for a row whose cost could not be read, so the failure is visible rather than silent. Do not widen
this catch to swallow the cost read as well — track cost failure separately in `CostKnown`.

`Next()` = the first `ApTierTable.Items` row where `!Owned(row)`, wrapped with its cost and
affordability. `Queue(n)` = the first n such rows.

- [ ] **Step 3: Build**

Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release` → 0 warnings, 0 errors.
Run: `dotnet test tests/NGUAdvisor.Tests` → still 192, nothing regressed.

- [ ] **Step 4: Stop for review.** Do not commit.

---

### Task 3: `ApPanel` — the read-only view

**Files:**
- Create: `NGUAdvisor/ApPanel.cs`
- Modify: `NGUAdvisor/Managers/SystemCatalog.cs`, `NGUAdvisor/Managers/Destinations.cs` (register as the siblings are — read one, e.g. how `PitPanel` or `YggPanel` is wired, and follow it exactly)

**Interfaces:**
- Consumes: `ApPurchaseAdvisor.Balance/Next/Queue`, `ApRec`, `ApItem` (Tasks 1–2).
- Produces: nothing consumed downstream.

- [ ] **Step 1: Read the contract before placing a control**

Read `docs/modules/ui-infra.md` §DPI calibration and `docs/modules/ui-panels.md`. Then read one existing simple panel end to end (`PitPanel.cs` is a good size) and follow its shape: section heads via the panel's `MkHead`, labels via `MkLbl`, rows via `UiLayout.Row`, numbers through `NumberFormatter.Abbrev`.

- [ ] **Step 2: Build the panel**

Content, top to bottom:
- Head `AP PURCHASES`
- Balance line: `NumberFormatter.Abbrev(ApPurchaseAdvisor.Balance())` AP
- Next buy: name, tier, cost, and whether it is affordable. If `CostKnown` is false say `cost unknown` — never render a zero as though it were a price.
- The queue behind it (`Queue(8)`), one row each, tier-labelled.
- A provenance line, verbatim: `Order: OJ of Steel's AP Tier List (build 1.200) — one player's ranking, not game truth.`

**No control may buy anything.** Read-only.

- [ ] **Step 3: Build and check the audit**

Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release` → clean.

The `UI AUDIT` check needs the game and is deferred by owner decision — record in your report that it was NOT run, and note that the comparison baseline is **70 lines, not zero**.

- [ ] **Step 4: Stop for review.** Do not commit.

---

### Task 4: Documentation

**Files:**
- Create: `docs/modules/ApPurchaseAdvisor.md`
- Modify: `CLAUDE.md` module-doc table if the naming needs an entry (grouped-doc row)

- [ ] **Step 1: Write the module doc**

Cover, in the voice of the existing module docs (dense, file:line citations, why over what):
- What AP is internally (`arbitrary.curArbitraryPoints`) and that the shop is the `Arbitrary` system.
- **The binding rule**: ownership is `ArbitraryController.shouldDisableBuyButton(id)` — the game's own owned/maxed predicate, which does not consider affordability — and cost is that entry's `cost()`. Say explicitly that hand-mapping `Arbitrary` fields was rejected, and why (drift across game versions).
- The three entry kinds (ShopId / Heart / Repeatable) and why hearts are not in the disable switch.
- That count-based entries are collapsed to one row at their earliest tier, and why.
- **Provenance**: the ordering is one player's opinion (OJ of Steel, build 1.200); the ids and semantics are decomp-derived. Do not let a future reader mistake the first for the second.
- The deliberate `catch → not owned` risk and why the panel surfaces `Known = false` instead of hiding it.

- [ ] **Step 2: Stop for review.** Do not commit.

---

## Self-Review

**Spec coverage:** game-truth table → Task 2 · pure/live split → Tasks 1–2 · id binding → Task 2 · the 50-row table → Task 1 · three entry kinds → Tasks 1–2 · panel + provenance → Task 3 · testing → Task 1 (unit), Tasks 2–3 (build + deferred game check) · docs → Task 4. Nothing unassigned.

**Placeholders:** none — every code step carries real code or names the exact spec section to transcribe.

**Type consistency:** `ApSource`/`ApItem`/`ApTierTable.Items`/`Unowned`/`NextUnowned` defined in Task 1 and used under those names in Tasks 2–3; `ApRec` defined in Task 2 and consumed in Task 3. `Key` means a shop id for `ShopId`, an item id for `Heart`, and is 0 for `Repeatable` — stated identically in both tasks.

**Known deferral:** every runtime check (panel audit, live balance, ownership against a real save) is deferred by owner decision. This plan produces code verified by build and unit tests only.
