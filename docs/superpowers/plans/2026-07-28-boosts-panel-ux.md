# Boosts Panel UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the priority list the only source of boosting, replace ID typing with an inventory picker, and make reordering painless.

**Architecture:** `InventoryManager.GetBoostSlots` is reduced to a single rule (priority list only); the boost blacklist is retired from both the boost and merge paths; a one-time seed copies today's implicit targets (equipped + locked) into the list so behavior does not change silently; `BoostsPanel` gains a modal picker, block/keyboard/drag reordering, and a live "will boost now" readout fed by the same function the automation uses.

**Tech Stack:** C# 7.3 / net48 (Mono inside Unity), WinForms, xUnit on net9.0 for the Unity-free parts.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-07-28-boosts-panel-ux-design.md`. Every task below implements part of it; do not invent behavior it does not describe.
- **net48, C# 7.3.** No nullable reference types, no records, no target-typed `new`, no range/index operators. `var` is discouraged by the user's C# conventions — use explicit types in new code.
- **This repository is NOT a git repository** (no `.git`). Every task's final step is therefore a **verification checkpoint**, not a commit. If `git rev-parse --git-dir` succeeds when you run it, commit with the message given in the task; otherwise skip the commit and just confirm the checks pass.
- **DPI contract is mandatory for every control** (`docs/modules/ui-infra.md`): hand-placed dimensions go through `UiTheme.S(n)`; heights that hold text through `UiTheme.SText/SHead/SCtl`; lists sized in rows via `UiTheme.ListH(rows)`; `ComboBox`/`ListBox`/`NumericUpDown` through `UiTheme.StyleCombo/StyleList/StyleNum`; checkboxes are `ScaledCheckBox`. Never raw pixels.
- **Main thread only.** All new UI and game reads run on the Unity main thread. Nothing added here may be called from a `FileSystemWatcher` callback.
- **Never throw into the game loop.** New event handlers wrap their body in `try/catch` and report via `Main.LogDebug` (and `Activity.Failed` where the user triggered the action), exactly as `BoostsPanel.MkBtn` already does.
- **Build:** `dotnet build NGUAdvisor/NGUAdvisor.csproj -c Debug` — requires a local NGU Idle install (see BUILD.md). **Tests:** `dotnet test tests/NGUAdvisor.Tests`.
- **Baseline before you start:** 53 tests pass.

---

### Task 1: Pure seed helper + tests

The only piece of this change that can be unit-tested, and the only one where a mistake would be silent. Build it first, alone.

**Files:**
- Create: `NGUAdvisor/Managers/BoostSeed.cs`
- Modify: `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj` (add one `Compile Include`)
- Test: `tests/NGUAdvisor.Tests/BoostSeedTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `NGUAdvisor.Managers.BoostSeed.SeedPriorityBoosts(int[] current, int[] equippedInSlotOrder, int[] lockedInSlotOrder)` returning `int[]`. Task 4 calls it.

- [ ] **Step 1: Write the failing test**

Create `tests/NGUAdvisor.Tests/BoostSeedTests.cs`:

```csharp
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class BoostSeedTests
    {
        [Fact]
        public void Appends_equipped_then_locked_after_the_existing_list()
        {
            int[] result = BoostSeed.SeedPriorityBoosts(
                new[] { 110, 126 },
                new[] { 124, 123 },
                new[] { 122 });

            Assert.Equal(new[] { 110, 126, 124, 123, 122 }, result);
        }

        [Fact]
        public void Never_duplicates_an_id_already_in_the_list()
        {
            int[] result = BoostSeed.SeedPriorityBoosts(
                new[] { 124 },
                new[] { 124, 123 },
                new[] { 124, 122 });

            Assert.Equal(new[] { 124, 123, 122 }, result);
        }

        [Fact]
        public void Never_duplicates_an_id_present_in_both_groups()
        {
            int[] result = BoostSeed.SeedPriorityBoosts(
                new int[0],
                new[] { 200 },
                new[] { 200 });

            Assert.Equal(new[] { 200 }, result);
        }

        [Fact]
        public void Drops_non_positive_ids()
        {
            int[] result = BoostSeed.SeedPriorityBoosts(
                new int[0],
                new[] { 0, -1, 300 },
                new int[0]);

            Assert.Equal(new[] { 300 }, result);
        }

        [Fact]
        public void Handles_null_inputs_as_empty()
        {
            Assert.Equal(new[] { 400 }, BoostSeed.SeedPriorityBoosts(null, new[] { 400 }, null));
            Assert.Empty(BoostSeed.SeedPriorityBoosts(null, null, null));
        }

        [Fact]
        public void Preserves_the_existing_order_exactly()
        {
            int[] result = BoostSeed.SeedPriorityBoosts(
                new[] { 3, 1, 2 },
                new[] { 1 },
                new int[0]);

            Assert.Equal(new[] { 3, 1, 2 }, result);
        }
    }
}
```

- [ ] **Step 2: Wire the source file into the test project**

In `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj`, inside the existing `<ItemGroup>` that holds the linked `Compile` items, add as the last entry:

```xml
    <Compile Include="..\..\NGUAdvisor\Managers\BoostSeed.cs" Link="Linked\BoostSeed.cs" />
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~BoostSeedTests"`
Expected: BUILD FAILURE — `The type or namespace name 'BoostSeed' does not exist`.

- [ ] **Step 4: Write the implementation**

Create `NGUAdvisor/Managers/BoostSeed.cs`:

```csharp
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // One-time migration helper for the boost priority list (spec:
    // docs/superpowers/specs/2026-07-28-boosts-panel-ux-design.md §4).
    //
    // Boosting used to have three implicit sources: the priority list, every EQUIPPED item, and every
    // LOCKED inventory item. It now has one — the list. This copies the two implicit groups into the
    // list ONCE so the change is visible instead of silent.
    //
    // Deliberately Unity-free and game-free so it can be unit-tested: it is the only place in that
    // change where a wrong result would go unnoticed.
    public static class BoostSeed
    {
        // Appends equipped (then locked) ids that are not already present, preserving the caller's
        // order within each group and never reordering what the user already had.
        public static int[] SeedPriorityBoosts(int[] current, int[] equippedInSlotOrder, int[] lockedInSlotOrder)
        {
            List<int> result = new List<int>();
            HashSet<int> seen = new HashSet<int>();

            void Take(int[] source)
            {
                if (source == null) return;
                for (int i = 0; i < source.Length; i++)
                {
                    int id = source[i];
                    if (id <= 0) continue;
                    if (!seen.Add(id)) continue;
                    result.Add(id);
                }
            }

            Take(current);
            Take(equippedInSlotOrder);
            Take(lockedInSlotOrder);
            return result.ToArray();
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/NGUAdvisor.Tests`
Expected: `Passed! - Failed: 0, Passed: 59` (53 existing + 6 new).

- [ ] **Step 6: Verification checkpoint**

Tests green at 59. If this is a git repository:

```bash
git add NGUAdvisor/Managers/BoostSeed.cs tests/NGUAdvisor.Tests/BoostSeedTests.cs tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj
git commit -m "Add pure boost priority seed helper"
```

---

### Task 2: Settings fields

**Files:**
- Modify: `NGUAdvisor/SavedSettings.cs` (field block near line 176, property block near line 2087, `MassUpdate` near line 539)

**Interfaces:**
- Produces: `Main.Settings.BoostSeeded` (bool, false until Task 4 runs the seed) and `Main.Settings.BoostDragReorderOff` (bool, false = drag & drop ENABLED). Tasks 4, 7 and 8 read them.

**Why `BoostDragReorderOff` and not `BoostDragReorder`:** `MassUpdate` reads `other?.X ?? false`, and `JsonUtility` deserializes a MISSING bool as `false`, never `null` — so `?? true` would never fire and a default-true flag would read as false for every existing settings.json. Storing the negation keeps "absent = feature on" correct. Do not "clean this up" into a positive flag.

- [ ] **Step 1: Add the backing fields**

In `NGUAdvisor/SavedSettings.cs`, immediately after the existing line `[SerializeField] private bool _autoBoostPriority;` (~line 176):

```csharp
        // Boosts v4 (spec 2026-07-28): the priority list is the ONLY boost source. _boostSeeded makes the
        // one-time migration from equipped + locked idempotent. _boostDragReorderOff is stored NEGATED on
        // purpose: MassUpdate reads `other?.X ?? false` and JsonUtility turns a missing bool into false, so
        // "absent means enabled" is only expressible as an off-switch.
        [SerializeField] private bool _boostSeeded;
        [SerializeField] private bool _boostDragReorderOff;
```

- [ ] **Step 2: Add the properties**

In `NGUAdvisor/SavedSettings.cs`, immediately after the closing brace of the `AutoBoostPriority` property (~line 2091):

```csharp
        // Set once by the boost-list migration (Main.Start). Never set it from the UI.
        public bool BoostSeeded
        {
            get => _boostSeeded;
            set { if (value == _boostSeeded) return; _boostSeeded = value; SaveSettings(); }
        }

        // Kill switch for drag-and-drop reordering of the priority list. WinForms drag & drop is the one
        // part of the boosts UI that cannot be verified outside the running game; if Mono misbehaves, set
        // this true in settings.json and the button/keyboard path keeps working.
        public bool BoostDragReorderOff
        {
            get => _boostDragReorderOff;
            set { if (value == _boostDragReorderOff) return; _boostDragReorderOff = value; SaveSettings(); }
        }
```

- [ ] **Step 3: Add them to MassUpdate**

In `MassUpdate`, immediately after the existing line `_autoBoostPriority = other?.AutoBoostPriority ?? false;` (~line 539):

```csharp
            _boostSeeded = other?.BoostSeeded ?? false;
            _boostDragReorderOff = other?.BoostDragReorderOff ?? false;
```

- [ ] **Step 4: Mark the retired blacklist**

Find the `BoostBlacklist` property (~line 965) and put this comment directly above it:

```csharp
        // RETIRED as of the 2026-07-28 boosts change: nothing reads this any more (the priority list is the
        // only boost source, and merges answer to the transform-chain toggles). The field is KEPT so
        // settings.json round-trips unchanged and a rollback to an older DLL still finds the user's data.
        // See docs/superpowers/specs/2026-07-28-boosts-panel-ux-design.md §3.
```

- [ ] **Step 5: Build**

Run: `dotnet build NGUAdvisor/NGUAdvisor.csproj -c Debug`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 6: Verification checkpoint**

Build clean. Commit if this is a git repository:

```bash
git add NGUAdvisor/SavedSettings.cs
git commit -m "Add boost seed and drag-reorder settings flags"
```

---

### Task 3: Priority list becomes the only boost source

**Files:**
- Modify: `NGUAdvisor/Managers/InventoryManager.cs` — `GetBoostSlots` (lines 101-122), `MergeBoosts` (line 182), `MergeBlocked`/`MergeBlockedId` (lines 721-733), `IsBlacklisted` (lines 714-716)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `GetBoostSlots(ih[] ci)` keeps its signature `public static ih[] GetBoostSlots(ih[] ci)` — Task 7 calls it from the panel for the live readout.

- [ ] **Step 1: Rewrite GetBoostSlots**

Replace the whole body of `GetBoostSlots` (currently lines 101-122) with:

```csharp
        // THE priority list is the ONLY source of boosting (spec 2026-07-28). It used to be one of three:
        // the list, every equipped item, and every locked inventory item — so removing an item from the
        // list did not stop it being boosted, and the blacklist was the only "never boost this" lever.
        // Main.Start seeds the two implicit groups into the list ONCE (BoostSeed) so this is not a silent
        // behavior change; from then on the list is exactly what gets boosted, in its own order.
        //
        // `ci` is unused now and kept only because callers pass their existing snapshot.
        public static ih[] GetBoostSlots(ih[] ci)
        {
            List<ih> result = new List<ih>();
            int[] priority = Settings.PriorityBoosts;
            if (priority == null) return new ih[0];

            foreach (int id in priority)
            {
                ih f = LoadoutManager.FindItemSlot(id);
                if (f?.equipment.isEquipment() != true) continue;
                // Transform protection is NOT part of the retired blacklist: a maxed chain copy the user
                // holds back must never be boosted, because applying a boost runs the game's
                // checkItemTransform and would trigger the transformation.
                if (TransformManager.Frozen(f)) continue;
                result.Add(f);
            }

            return result.FindAll(x => x.equipment.GetNeededBoosts().Total() > 0).ToArray();
        }
```

- [ ] **Step 2: Drop the blacklist from the boost-item merge**

In `MergeBoosts` (line 182), change:

```csharp
            var grouped = Array.FindAll(ci, x => IsBoost(x) && !IsBlacklisted(x) && IsLocked(x) && !IsMaxxed(x));
```

to:

```csharp
            var grouped = Array.FindAll(ci, x => IsBoost(x) && IsLocked(x) && !IsMaxxed(x));
```

- [ ] **Step 3: Reduce the merge exclusions to the chain rules**

Replace `IsBlacklisted`, `MergeBlocked` and `MergeBlockedId` (lines 710-733) with:

```csharp
        // Frozen = transform-chain protection (TransformManager): a maxed chain item whose transform the
        // user is holding back (Keep max lvl, or Auto-climb off) must not be boosted or merged — both
        // paths run the game's checkItemTransform and would trigger the transformation.
        //
        // The boost blacklist that used to live here is RETIRED (spec 2026-07-28): boosting is driven by
        // the priority list alone, and merging is governed by the chain toggles that actually govern it.
        // Its second job — blocking merges — had already needed an exception carved out of it (blacklisted
        // Sir Lootys at lv 0/5/77 never merged), which is what a rule serving two purposes looks like.
        private static bool MergeBlocked(ih x)
        {
            var chain = TransformManager.MergeAllowed(x.id);
            if (chain.HasValue) return !chain.Value || TransformManager.Frozen(x);
            return false;
        }

        private static bool MergeBlockedId(int id)
        {
            var chain = TransformManager.MergeAllowed(id);
            if (chain.HasValue) return !chain.Value;
            return false;
        }
```

- [ ] **Step 4: Remove the now-unused priority helper**

`IsPriority` (line 708) has no callers left after Step 1. Delete this line:

```csharp
        private static bool IsPriority(ih x) => Settings.PriorityBoosts.Contains(x.id);
```

- [ ] **Step 5: Build**

Run: `dotnet build NGUAdvisor/NGUAdvisor.csproj -c Debug`
Expected: `Build succeeded`. If the compiler reports an unused-variable or unreachable-code warning for `ci` in `GetBoostSlots`, leave it — the parameter is deliberately kept.

- [ ] **Step 6: Verification checkpoint**

Build clean. Commit if this is a git repository:

```bash
git add NGUAdvisor/Managers/InventoryManager.cs
git commit -m "Make the priority list the only boost source, retire the blacklist"
```

---

### Task 4: One-time seed on startup

**Files:**
- Modify: `NGUAdvisor/Main.cs` (insert after the settings round-trip, ~line 356, before `ZoneStatHelper.CreateOverrides(_dir);`)

**Interfaces:**
- Consumes: `BoostSeed.SeedPriorityBoosts(int[], int[], int[])` from Task 1; `Settings.BoostSeeded` from Task 2.
- Produces: nothing later tasks call.

- [ ] **Step 1: Add the seed method**

In `NGUAdvisor/Main.cs`, add this private method directly above `private void QuickSave()` (~line 661):

```csharp
        // One-time migration for the boosts change (spec 2026-07-28 §4). Boosting used to include every
        // equipped item and every locked inventory item implicitly; now only the priority list is boosted.
        // Copy those two groups into the list ONCE so nobody's gear silently stops receiving boosts.
        // Main thread, after settings are loaded — the ids are live game reads.
        private static void SeedBoostPriorityOnce()
        {
            try
            {
                if (Settings == null || Settings.BoostSeeded) return;

                var equipped = new List<int>();
                var locked = new List<int>();
                var inv = Character.inventory;

                void AddEquip(Equipment e)
                {
                    if (e != null && e.id != 0 && e.isEquipment()) equipped.Add(e.id);
                }

                AddEquip(inv.weapon);
                if (InventoryController.weapon2Unlocked()) AddEquip(inv.weapon2);
                AddEquip(inv.head);
                AddEquip(inv.chest);
                AddEquip(inv.legs);
                AddEquip(inv.boots);
                if (inv.accs != null)
                    foreach (var a in inv.accs) AddEquip(a);

                if (inv.inventory != null)
                {
                    for (int i = 0; i < inv.inventory.Count; i++)
                    {
                        var e = inv.inventory[i];
                        if (e == null || e.id == 0 || !e.isEquipment()) continue;
                        if (e.removable) continue;   // locked = the game's inventory padlock
                        locked.Add(e.id);
                    }
                }

                int before = Settings.PriorityBoosts?.Length ?? 0;
                Settings.PriorityBoosts = Managers.BoostSeed.SeedPriorityBoosts(
                    Settings.PriorityBoosts, equipped.ToArray(), locked.ToArray());
                Settings.BoostSeeded = true;

                Log($"Boost priority seeded once: {before} existing + {equipped.Count} equipped + {locked.Count} locked " +
                    $"-> {Settings.PriorityBoosts.Length} entries. Boosting now follows this list only; edit it in Systems > Boosts.");
            }
            catch (Exception e)
            {
                LogDebug($"Boost priority seed failed: {e.Message}");
            }
        }
```

- [ ] **Step 2: Call it from Start**

In `Main.Start`, directly after the existing line `Settings.LoadSettings();` that closes the normalising round-trip (~line 356) and before `ZoneStatHelper.CreateOverrides(_dir);`, insert:

```csharp
                SeedBoostPriorityOnce();
```

- [ ] **Step 3: Build**

Run: `dotnet build NGUAdvisor/NGUAdvisor.csproj -c Debug`
Expected: `Build succeeded`.

- [ ] **Step 4: Verification checkpoint**

Build clean. The behavior is verified in game in Task 9's manual pass (the log line appears exactly once, and never again after a restart). Commit if this is a git repository:

```bash
git add NGUAdvisor/Main.cs
git commit -m "Seed the boost priority list once from equipped and locked items"
```

---

### Task 5: Advisor mode must lead with equipped items

**Files:**
- Modify: `NGUAdvisor/Managers/InventoryAdvisor.cs` — `AutoBoostPriority` (lines 116-136) and the comment above `NeedsBoosts` (lines 102-105)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: unchanged signature `public static int[] AutoBoostPriority(Verdict v)`.

**Why this is not optional:** the method currently EXCLUDES equipped items, documented as "Equipped gear is boosted first by the existing InventoryManager pass regardless of this list". Task 3 deleted that pass, so without this change ADVISOR mode would stop boosting worn gear entirely.

- [ ] **Step 1: Replace the comment block**

Replace lines 102-105 (the comment above `NeedsBoosts`) with:

```csharp
        // Advisor-driven boost priority: equipped gear FIRST (since the 2026-07-28 change the priority list
        // is the only boost source — the old "equipped is boosted anyway" pass no longer exists), then
        // unequipped KEEP items ranked by objective usage, then chain climbers.
        // Fully-boosted items have nothing left to receive — they neither rank nor display.
```

- [ ] **Step 2: Rewrite AutoBoostPriority**

Replace the whole `AutoBoostPriority` method body with:

```csharp
        public static int[] AutoBoostPriority(Verdict v)
        {
            var list = new List<int>();

            // Equipped first, in slot order: it is what the character is actually using right now.
            foreach (int id in LoadoutManager.CurrentGearIds())
                if (id > 0 && !list.Contains(id) && NeedsBoosts(id))
                    list.Add(id);

            var equipped = new HashSet<int>(LoadoutManager.CurrentGearIds());
            foreach (var kv in v.Keep
                .Where(kv => !equipped.Contains(kv.Key) && NeedsBoosts(kv.Key))
                .OrderByDescending(kv => v.Usage.TryGetValue(kv.Key, out var n) ? n : 0))
            {
                if (!list.Contains(kv.Key)) list.Add(kv.Key);
            }

            for (int i = 0; i < TransformManager.Chains.Length; i++)
            {
                try
                {
                    var s = TransformManager.Read(i);
                    if (s.OwnedTier >= 0 && s.NextId > 0 && s.Level < 100 && !list.Contains(s.OwnedId)
                        && NeedsBoosts(s.OwnedId))
                        list.Add(s.OwnedId);
                }
                catch { }
            }
            return list.ToArray();
        }
```

- [ ] **Step 3: Build**

Run: `dotnet build NGUAdvisor/NGUAdvisor.csproj -c Debug`
Expected: `Build succeeded`.

- [ ] **Step 4: Verification checkpoint**

Build clean. Commit if this is a git repository:

```bash
git add NGUAdvisor/Managers/InventoryAdvisor.cs
git commit -m "Advisor boost priority now leads with equipped gear"
```

---

### Task 6: The inventory picker window

**Files:**
- Create: `NGUAdvisor/BoostPickerForm.cs`

**Interfaces:**
- Consumes: `ih` (`NGUAdvisor.Managers`), `InventoryAdvisor.Last`, `UiTheme`, `UiLayout`.
- Produces: `public sealed class BoostPickerForm : Form` with `public static int[] Pick(IWin32Window owner, int[] alreadyInList)` returning the ids the user chose (empty array on cancel). Task 7 calls exactly this.

**Precedent:** `ProfileEditorForm` is the existing example of a second window in this app — follow its construction style (plain WinForms `Form`, `UiTheme` colors/fonts, no designer file).

- [ ] **Step 1: Create the form**

Create `NGUAdvisor/BoostPickerForm.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;

namespace NGUAdvisor
{
    // "Add from inventory" picker for the boost priority list (spec 2026-07-28 §5). Replaces typing raw
    // item IDs. Modal, owned by SettingsForm; every dimension goes through the UiTheme DPI helpers.
    public sealed class BoostPickerForm : Form
    {
        private sealed class Row
        {
            public int Id;
            public string Name;
            public int Level;
            public string Where;      // equipped / inventory / daycare
            public float NeedTotal;
            public string NeedText;   // "P 12 · T 8", or "—"
            public bool AlreadyListed;
            public int Usage;         // objective-optimal loadouts containing it (0 when unknown)
            public bool Equipped;
        }

        private readonly List<Row> _all = new List<Row>();
        private readonly ListBox _list;
        private readonly TextBox _search;
        private readonly ScaledCheckBox _needsOnly;
        private readonly Label _count;
        private List<Row> _shown = new List<Row>();
        private int[] _result = new int[0];

        public static int[] Pick(IWin32Window owner, int[] alreadyInList)
        {
            try
            {
                using (var f = new BoostPickerForm(alreadyInList))
                    return f.ShowDialog(owner) == DialogResult.OK ? f._result : new int[0];
            }
            catch (Exception e)
            {
                Main.LogDebug($"Boost picker failed: {e}");
                Activity.Failed("Couldn't open the item picker", e.Message, true);
                return new int[0];
            }
        }

        private BoostPickerForm(int[] alreadyInList)
        {
            Text = "Add items to priority boosts";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = UiTheme.Ground;
            ClientSize = new Size(UiTheme.S(520), UiTheme.ListH(12) + UiTheme.SCtl(24) * 2 + UiTheme.S(56));

            BuildRows(alreadyInList ?? new int[0]);

            _search = new TextBox
            {
                Location = new Point(UiTheme.S(10), UiTheme.S(10)),
                Width = UiTheme.S(240),
                Font = UiTheme.Ui
            };
            _search.TextChanged += (s, e) => { try { Refill(); } catch (Exception ex) { Main.LogDebug($"Picker search: {ex.Message}"); } };
            Controls.Add(_search);

            _needsOnly = new ScaledCheckBox
            {
                Text = "Needs boosts only",
                Checked = true,
                Location = new Point(_search.Right + UiTheme.S(12), UiTheme.S(10)),
                AutoSize = true,
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Ink,
                BackColor = UiTheme.Ground
            };
            _needsOnly.CheckedChanged += (s, e) => { try { Refill(); } catch (Exception ex) { Main.LogDebug($"Picker filter: {ex.Message}"); } };
            Controls.Add(_needsOnly);

            int listTop = Math.Max(_search.Bottom, _needsOnly.Bottom) + UiTheme.S(8);
            _list = new ListBox
            {
                Location = new Point(UiTheme.S(10), listTop),
                Size = new Size(ClientSize.Width - UiTheme.S(20), UiTheme.ListH(12)),
                Font = UiTheme.Ui,
                BorderStyle = BorderStyle.FixedSingle,
                SelectionMode = SelectionMode.MultiExtended
            };
            UiTheme.StyleList(_list);
            _list.SelectedIndexChanged += (s, e) => { try { DropListedFromSelection(); UpdateCount(); } catch (Exception ex) { Main.LogDebug($"Picker select: {ex.Message}"); } };
            Controls.Add(_list);

            _count = new Label
            {
                Location = new Point(UiTheme.S(10), _list.Bottom + UiTheme.S(10)),
                AutoSize = true,
                Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted,
                BackColor = UiTheme.Ground,
                Text = "0 selected"
            };
            Controls.Add(_count);

            var cancel = MkButton("Cancel", () => { DialogResult = DialogResult.Cancel; Close(); });
            var add = MkButton("Add selected", () =>
            {
                _result = SelectedRows().Select(r => r.Id).ToArray();
                DialogResult = DialogResult.OK;
                Close();
            });
            cancel.Location = new Point(ClientSize.Width - UiTheme.S(10) - cancel.Width, _list.Bottom + UiTheme.S(6));
            add.Location = new Point(cancel.Left - UiTheme.S(8) - add.Width, cancel.Top);
            Controls.Add(cancel);
            Controls.Add(add);

            AcceptButton = add;
            CancelButton = cancel;
            ClientSize = new Size(ClientSize.Width, cancel.Bottom + UiTheme.S(10));

            Refill();
        }

        private Button MkButton(string text, Action onClick)
        {
            var b = new Button
            {
                Text = text,
                Font = UiTheme.Ui,
                Size = new Size(UiLayout.MeasureText(text, UiTheme.Ui) + UiTheme.S(24), UiTheme.SCtl(24))
            };
            UiTheme.StyleFlat(b);
            b.Click += (s, e) => { try { onClick(); } catch (Exception ex) { Main.LogDebug($"Picker button: {ex.Message}"); } };
            return b;
        }

        // Every owned equipment id, once, with the data the columns show.
        private void BuildRows(int[] alreadyInList)
        {
            var listed = new HashSet<int>(alreadyInList);
            var usage = InventoryAdvisor.Last?.Usage;   // cached only — never start the optimizer sweep here
            var seen = new HashSet<int>();
            var c = Main.Character;
            if (c == null) return;
            var inv = c.inventory;

            void Consider(Equipment e, string where, bool equipped)
            {
                if (e == null || e.id == 0 || !e.isEquipment()) return;
                if (!seen.Add(e.id)) return;
                var need = e.GetNeededBoosts();
                _all.Add(new Row
                {
                    Id = e.id,
                    Name = Main.ItemNameNice(e.id),
                    Level = e.level,
                    Where = where,
                    NeedTotal = need.Total(),
                    NeedText = FormatNeed(need),
                    AlreadyListed = listed.Contains(e.id),
                    Usage = usage != null && usage.TryGetValue(e.id, out int n) ? n : 0,
                    Equipped = equipped
                });
            }

            Consider(inv.weapon, "equipped", true);
            try { if (Main.InventoryController.weapon2Unlocked()) Consider(inv.weapon2, "equipped", true); } catch { }
            Consider(inv.head, "equipped", true);
            Consider(inv.chest, "equipped", true);
            Consider(inv.legs, "equipped", true);
            Consider(inv.boots, "equipped", true);
            if (inv.accs != null) foreach (var a in inv.accs) Consider(a, "equipped", true);
            if (inv.inventory != null) foreach (var e in inv.inventory) Consider(e, "inventory", false);
            if (inv.daycare != null) foreach (var e in inv.daycare) Consider(e, "daycare", false);

            _all.Sort((a, b) =>
            {
                if (a.Equipped != b.Equipped) return a.Equipped ? -1 : 1;
                if (a.Usage != b.Usage) return b.Usage.CompareTo(a.Usage);
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string FormatNeed(BoostsNeeded n)
        {
            var parts = new List<string>();
            if (n.power > 0) parts.Add($"P {n.power:0}");
            if (n.toughness > 0) parts.Add($"T {n.toughness:0}");
            if (n.special > 0) parts.Add($"S {n.special:0}");
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }

        private void Refill()
        {
            string q = _search.Text.Trim();
            bool needsOnly = _needsOnly.Checked;
            _shown = _all.Where(r =>
            {
                if (needsOnly && r.NeedTotal <= 0) return false;
                if (q.Length == 0) return true;
                if (r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return r.Id.ToString().IndexOf(q.TrimStart('#'), StringComparison.Ordinal) >= 0;
            }).ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var r in _shown)
                _list.Items.Add(r.AlreadyListed
                    ? $"{r.Name}  (#{r.Id})   —   already in list"
                    : $"{r.Name}  (#{r.Id})   ·   lvl {r.Level}/100   ·   {r.NeedText}   ·   {r.Where}");
            _list.EndUpdate();
            UpdateCount();
        }

        // Rows already in the list cannot be added twice: deselect them the moment they get selected.
        private void DropListedFromSelection()
        {
            for (int i = _list.SelectedIndices.Count - 1; i >= 0; i--)
            {
                int idx = _list.SelectedIndices[i];
                if (idx >= 0 && idx < _shown.Count && _shown[idx].AlreadyListed)
                    _list.SetSelected(idx, false);
            }
        }

        private IEnumerable<Row> SelectedRows()
        {
            foreach (int idx in _list.SelectedIndices)
                if (idx >= 0 && idx < _shown.Count && !_shown[idx].AlreadyListed)
                    yield return _shown[idx];
        }

        private void UpdateCount() => _count.Text = $"{SelectedRows().Count()} selected";
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build NGUAdvisor/NGUAdvisor.csproj -c Debug`
Expected: `Build succeeded`.

If the compiler reports that `inv.daycare` or `UiTheme.Ink`/`UiTheme.Ground`/`UiTheme.Muted`/`UiTheme.StyleFlat` does not exist under those names, read the actual member names in `NGUAdvisor/Managers/UiTheme.cs` and `LoadoutManager.cs` and use those — **do not** invent a replacement or drop the column.

- [ ] **Step 3: Verification checkpoint**

Build clean. The window is exercised in Task 7 (nothing opens it yet). Commit if this is a git repository:

```bash
git add NGUAdvisor/BoostPickerForm.cs
git commit -m "Add the boost inventory picker window"
```

---

### Task 7: Panel rework — picker, block reordering, live readout, no blacklist

**Files:**
- Modify: `NGUAdvisor/BoostsPanel.cs` — header comment (lines 14-25), fields (lines 43-48), manual view construction (lines 263-330), `EditList` (515-537), `MovePrio` (539-550), `SyncFromSettings` (552-596)

**Interfaces:**
- Consumes: `BoostPickerForm.Pick(IWin32Window, int[])` (Task 6), `InventoryManager.GetBoostSlots(ih[])` (Task 3), `Settings.BoostDragReorderOff` (Task 2).
- Produces: `private void MovePrioBlock(int dir, bool toEnd)` — Task 8's drag handler reuses the same list-write path.

- [ ] **Step 1: Remove the blacklist fields**

In the field block (lines 43-48), delete these two lines:

```csharp
        private ListBox _black;
        private TextBox _blackAdd;
```

and add:

```csharp
        private ListBox _manualReadout;
```

- [ ] **Step 2: Rebuild the manual view**

Replace lines 263-330 (from `// MANUAL view: editable priority + blacklist.` up to and including `_manualView.Controls.Add(_order);`) with:

```csharp
            // MANUAL view: the editable priority list IS the boost list (spec 2026-07-28 — the blacklist is
            // retired and equipped/locked are no longer boosted implicitly), plus a live readout of what
            // will actually be boosted, filled by the same GetBoostSlots the automation uses so the panel
            // cannot disagree with behavior.
            _manualView = new Panel { Location = new Point(0, UiTheme.S(44)), Size = new Size(_pw - 0, UiTheme.S(268)), BackColor = UiTheme.Ground, Visible = false };
            _boostPage.Controls.Add(_manualView);

            // ROWS, NOT OFFSETS (see the note this replaced): the lists are asked for a row count so the
            // usable space is what is specified, and a running cursor keeps everything below them honest.
            // The blacklist's rows went to the priority list, which is why it is 14 now.
            const int PrioRows = 14, ReadoutRows = 4;
            int listW = _pw - UiTheme.S(30);
            int y = 0;

            _manualView.Controls.Add(new Label { Text = "PRIORITY BOOSTS (boosted top-down — this list is the only thing boosted)", Location = new Point(UiTheme.S(10), y), AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground });
            y += UiTheme.HeadPitch;
            _prio = new ListBox { Location = new Point(UiTheme.S(10), y), Size = new Size(listW, UiTheme.ListH(PrioRows)), Font = UiTheme.Ui, BorderStyle = BorderStyle.FixedSingle, SelectionMode = SelectionMode.MultiExtended };
            UiTheme.StyleList(_prio);
            _prio.KeyDown += PrioKeyDown;
            _manualView.Controls.Add(_prio);
            y = _prio.Bottom + UiTheme.S(8);

            int wPick = MeasureBtn("Add from inventory"), wRem = MeasureBtn("Remove");
            int wTop = MeasureBtn("Top"), wUp = MeasureBtn("Up"), wDown = MeasureBtn("Down"), wBottom = MeasureBtn("Bottom");
            int bx = UiTheme.S(10);
            _manualView.Controls.Add(MkBtn("Add from inventory", bx, y, wPick, AddFromInventory)); bx += wPick + UiTheme.S(6);
            _manualView.Controls.Add(MkBtn("Remove", bx, y, wRem, RemoveSelectedPrio));
            y += UiTheme.SCtl(24) + UiTheme.S(6);

            bx = UiTheme.S(10);
            _manualView.Controls.Add(MkBtn("Top", bx, y, wTop, () => MovePrioBlock(-1, true))); bx += wTop + UiTheme.S(6);
            _manualView.Controls.Add(MkBtn("Up", bx, y, wUp, () => MovePrioBlock(-1, false))); bx += wUp + UiTheme.S(6);
            _manualView.Controls.Add(MkBtn("Down", bx, y, wDown, () => MovePrioBlock(1, false))); bx += wDown + UiTheme.S(6);
            _manualView.Controls.Add(MkBtn("Bottom", bx, y, wBottom, () => MovePrioBlock(1, true)));
            y += UiTheme.SCtl(24) + UiTheme.S(4);

            _manualView.Controls.Add(new Label { Text = "Alt+↑/↓ moves the selection · Alt+Home/End sends it to the ends", Location = new Point(UiTheme.S(10), y), AutoSize = true, Font = UiTheme.Chip, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground });
            y += UiTheme.HeadPitch + UiTheme.S(6);

            _manualView.Controls.Add(new Label { Text = "WILL BOOST NOW (live, in order)", Location = new Point(UiTheme.S(10), y), AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground });
            y += UiTheme.HeadPitch;
            _manualReadout = new ListBox { Location = new Point(UiTheme.S(10), y), Size = new Size(listW, UiTheme.ListH(ReadoutRows)), Font = UiTheme.Ui, BorderStyle = BorderStyle.FixedSingle, SelectionMode = SelectionMode.None };
            UiTheme.StyleList(_manualReadout);
            _manualView.Controls.Add(_manualReadout);
            y = _manualReadout.Bottom + UiTheme.S(10);

            // Boost APPLICATION order (Power/Toughness/Special) — six permutations in a combo, Mono-safe.
            var ordLbl = new Label { Text = "Apply order", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(UiTheme.S(10), y + UiTheme.S(4)) };
            _manualView.Controls.Add(ordLbl);
            _order = new ComboBox { Width = UiTheme.S(170), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui, Location = new Point(UiTheme.S(10) + UiLayout.MeasureText("Apply order", UiTheme.Ui) + UiTheme.S(8), y) };
            UiTheme.StyleCombo(_order);
            foreach (var p in OrderPerms)
                _order.Items.Add(string.Join(" → ", p));
            _order.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing || Settings == null || _order.SelectedIndex < 0) return;
                Settings.BoostPriority = (string[])OrderPerms[_order.SelectedIndex].Clone();
            };
            _manualView.Controls.Add(_order);
```

- [ ] **Step 3: Replace EditList and MovePrio**

Replace `EditList` (515-537) and `MovePrio` (539-550) with:

```csharp
        private void AddFromInventory()
        {
            if (Settings == null) return;
            int[] picked = BoostPickerForm.Pick(FindForm(), Settings.PriorityBoosts ?? new int[0]);
            if (picked == null || picked.Length == 0) return;

            var cur = (Settings.PriorityBoosts ?? new int[0]).ToList();
            foreach (int id in picked)
                if (id > 0 && !cur.Contains(id)) cur.Add(id);
            Settings.PriorityBoosts = cur.ToArray();
            SyncFromSettings();
            Activity.Completed($"Added {picked.Length} item(s) to priority boosts");
        }

        private void RemoveSelectedPrio()
        {
            if (Settings == null) return;
            var cur = (Settings.PriorityBoosts ?? new int[0]).ToList();
            var indices = _prio.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToArray();
            if (indices.Length == 0) return;
            foreach (int idx in indices)
                if (idx >= 0 && idx < cur.Count) cur.RemoveAt(idx);
            Settings.PriorityBoosts = cur.ToArray();
            SyncFromSettings();
        }

        // Moves the selected block one step (toEnd = false) or all the way to an end (toEnd = true).
        // dir < 0 is towards the top. The selection is preserved and kept visible — losing the selection
        // after every click is what made the old single-step buttons unusable for a long list.
        private void MovePrioBlock(int dir, bool toEnd)
        {
            if (Settings == null) return;
            var cur = (Settings.PriorityBoosts ?? new int[0]).ToList();
            var sel = _prio.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
            if (sel.Count == 0 || cur.Count == 0) return;
            if (dir < 0 && sel[0] == 0) return;
            if (dir > 0 && sel[sel.Count - 1] == cur.Count - 1) return;

            var moved = sel.Select(i => cur[i]).ToList();
            for (int i = sel.Count - 1; i >= 0; i--) cur.RemoveAt(sel[i]);

            int insertAt;
            if (toEnd) insertAt = dir < 0 ? 0 : cur.Count;
            else insertAt = dir < 0 ? sel[0] - 1 : sel[0] + 1;
            if (insertAt < 0) insertAt = 0;
            if (insertAt > cur.Count) insertAt = cur.Count;

            cur.InsertRange(insertAt, moved);
            Settings.PriorityBoosts = cur.ToArray();
            SyncFromSettings();
            ReselectRange(insertAt, moved.Count);
        }

        private void ReselectRange(int start, int count)
        {
            _prio.ClearSelected();
            for (int i = 0; i < count; i++)
            {
                int idx = start + i;
                if (idx >= 0 && idx < _prio.Items.Count) _prio.SetSelected(idx, true);
            }
            if (start >= 0 && start < _prio.Items.Count) _prio.TopIndex = Math.Max(0, start - 2);
        }

        private void PrioKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (!e.Alt) return;
                if (e.KeyCode == Keys.Up) { MovePrioBlock(-1, false); e.Handled = true; }
                else if (e.KeyCode == Keys.Down) { MovePrioBlock(1, false); e.Handled = true; }
                else if (e.KeyCode == Keys.Home) { MovePrioBlock(-1, true); e.Handled = true; }
                else if (e.KeyCode == Keys.End) { MovePrioBlock(1, true); e.Handled = true; }
            }
            catch (Exception ex) { LogDebug($"Boosts key: {ex.Message}"); }
        }
```

- [ ] **Step 4: Update SyncFromSettings**

In `SyncFromSettings`, replace the `_black` block (lines 586-590):

```csharp
                _black.BeginUpdate();
                _black.Items.Clear();
                foreach (var id in Settings.BoostBlacklist ?? new int[0])
                    _black.Items.Add($"{ItemNameNice(id)}  (#{id})");
                _black.EndUpdate();
```

with:

```csharp
                RefreshManualReadout();
```

and add this method directly below `SyncFromSettings`:

```csharp
        // The live "what will actually be boosted" list. Calls the SAME function the automation calls, so
        // the panel and the behavior cannot drift apart. Cheap: it walks the priority list only.
        private void RefreshManualReadout()
        {
            if (_manualReadout == null) return;
            _manualReadout.BeginUpdate();
            _manualReadout.Items.Clear();
            try
            {
                if (Main.Character != null)
                {
                    var converted = Main.Character.inventory.GetConvertedInventory().ToArray();
                    var slots = InventoryManager.GetBoostSlots(converted);
                    foreach (var s in slots)
                        _manualReadout.Items.Add($"{ItemNameNice(s.id)}  (#{s.id})   lvl {s.level}/100");
                    if (slots.Length == 0)
                        _manualReadout.Items.Add("(nothing — add items above)");
                }
            }
            catch (Exception e)
            {
                Main.LogDebug($"Boost readout: {e.Message}");
                _manualReadout.Items.Add("(readout unavailable)");
            }
            _manualReadout.EndUpdate();
        }
```

- [ ] **Step 5: Update the header comment**

Replace the layout pre-flight paragraph (lines 20-25) with:

```csharp
    // Layout pre-flight: everything below the top row is derived from measured rows, not tuned pixels —
    // priority list ListH(14), two button rows at SCtl(24), a hint line, the live readout ListH(4), then
    // the Apply-order row. The page grows to its content inside the scrolling host (one scroll owner per
    // screen), so no fixed height may be reintroduced here.
    // BLACKLIST REMOVED 2026-07-28: the priority list is the only boost source; merges answer to the
    // transform-chain toggles. Spec: docs/superpowers/specs/2026-07-28-boosts-panel-ux-design.md
```

- [ ] **Step 6: Build**

Run: `dotnet build NGUAdvisor/NGUAdvisor.csproj -c Debug`
Expected: `Build succeeded`. Any error naming `_black`, `_blackAdd` or `EditList` means a reference was missed — delete it; the blacklist has no UI any more.

- [ ] **Step 7: Verification checkpoint**

Build clean. Commit if this is a git repository:

```bash
git add NGUAdvisor/BoostsPanel.cs
git commit -m "Boosts panel: inventory picker, block reordering, live readout, no blacklist"
```

---

### Task 8: Drag & drop reordering

Built LAST and as a layer, so the panel is fully usable before this exists and stays usable if Mono's drag & drop misbehaves.

**Files:**
- Modify: `NGUAdvisor/BoostsPanel.cs` (add drag wiring next to the `_prio` construction from Task 7, plus the handlers)

**Interfaces:**
- Consumes: `MovePrioBlock`/`ReselectRange` (Task 7), `Settings.BoostDragReorderOff` (Task 2).
- Produces: nothing.

- [ ] **Step 1: Add the drag state fields**

Next to the other private fields in `BoostsPanel`, add:

```csharp
        private int _dragFrom = -1;
        private int _dragInsert = -1;
```

- [ ] **Step 2: Wire the events**

In the manual-view construction from Task 7, directly after `_prio.KeyDown += PrioKeyDown;`, add:

```csharp
            if (Settings == null || !Settings.BoostDragReorderOff)
            {
                _prio.AllowDrop = true;
                _prio.MouseDown += PrioMouseDown;
                _prio.MouseMove += PrioMouseMove;
                _prio.DragOver += PrioDragOver;
                _prio.DragDrop += PrioDragDrop;
                _prio.DragLeave += (s, e) => { _dragInsert = -1; _prio.Invalidate(); };
            }
```

- [ ] **Step 3: Add the handlers**

Add below `PrioKeyDown`:

```csharp
        // Drag & drop is a LAYER over the buttons, never a replacement: WinForms DnD is the part of this
        // panel that cannot be verified outside the running game, so every handler is guarded and the
        // whole thing is skippable via Settings.BoostDragReorderOff. The list write goes through the same
        // path the buttons use.
        private void PrioMouseDown(object sender, MouseEventArgs e)
        {
            try { _dragFrom = _prio.IndexFromPoint(e.Location); }
            catch (Exception ex) { LogDebug($"Boosts drag start: {ex.Message}"); _dragFrom = -1; }
        }

        private void PrioMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (e.Button != MouseButtons.Left || _dragFrom < 0) return;
                if (!_prio.SelectedIndices.Cast<int>().Contains(_dragFrom)) return;
                _prio.DoDragDrop(_dragFrom, DragDropEffects.Move);
            }
            catch (Exception ex) { LogDebug($"Boosts drag move: {ex.Message}"); }
        }

        private void PrioDragOver(object sender, DragEventArgs e)
        {
            try
            {
                e.Effect = DragDropEffects.Move;
                Point p = _prio.PointToClient(new Point(e.X, e.Y));
                int idx = _prio.IndexFromPoint(p);
                _dragInsert = idx < 0 ? _prio.Items.Count : idx;
            }
            catch (Exception ex) { LogDebug($"Boosts drag over: {ex.Message}"); }
        }

        private void PrioDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (Settings == null || _dragInsert < 0) return;
                var cur = (Settings.PriorityBoosts ?? new int[0]).ToList();
                var sel = _prio.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
                if (sel.Count == 0) return;

                var moved = sel.Select(i => cur[i]).ToList();
                int target = _dragInsert;
                // Removing the block shifts everything after it left; correct the insertion point first.
                foreach (int i in sel) if (i < target) target--;
                for (int i = sel.Count - 1; i >= 0; i--) cur.RemoveAt(sel[i]);
                if (target < 0) target = 0;
                if (target > cur.Count) target = cur.Count;

                cur.InsertRange(target, moved);
                Settings.PriorityBoosts = cur.ToArray();
                SyncFromSettings();
                ReselectRange(target, moved.Count);
            }
            catch (Exception ex) { LogDebug($"Boosts drag drop: {ex.Message}"); }
            finally { _dragFrom = -1; _dragInsert = -1; }
        }
```

- [ ] **Step 4: Build**

Run: `dotnet build NGUAdvisor/NGUAdvisor.csproj -c Debug`
Expected: `Build succeeded`.

- [ ] **Step 5: Verification checkpoint**

Build clean. Commit if this is a git repository:

```bash
git add NGUAdvisor/BoostsPanel.cs
git commit -m "Add drag-and-drop reordering to the boost priority list"
```

---

### Task 9: Documentation, changelog, and the in-game verification pass

**Files:**
- Modify: `docs/modules/InventoryManager.md`, `docs/modules/InventoryAdvisor.md`, `docs/modules/ui-panels.md`, `CHANGELOG.md`

- [ ] **Step 1: Fix InventoryManager.md**

Replace the section "## Two DIFFERENT exclusion sets — do not conflate them" with:

```markdown
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
```

- [ ] **Step 2: Fix InventoryAdvisor.md**

In the `AutoBoostPriority` section, replace the sentence beginning "Unequipped KEEP items that still need boosts" with:

```markdown
**Equipped gear first** (in slot order), then unequipped KEEP items that still need boosts
(`GetNeededBoosts().Total() > 0`), ranked by objective `Usage`, then transform-chain climbers. Equipped
items lead because since 2026-07-28 the priority list is the ONLY boost source — the old "equipped is
boosted by the manager pass regardless" assumption is gone.
```

- [ ] **Step 3: Add the picker to ui-panels.md**

In the panel table, replace the `BoostsPanel` row's entry with:

```markdown
| `BoostsPanel` + `BoostPickerForm` | `InventoryManager.GetBoostSlots` (live readout), `InventoryAdvisor`, `TransformManager` |
```

- [ ] **Step 4: Add the changelog entry**

At the top of `CHANGELOG.md`, under a new unreleased heading:

```markdown
### Boosts

- The priority list is now the **only** thing that gets boosted. Equipped and locked-inventory items are
  no longer boosted implicitly, and the boost blacklist is gone. On first run your list is seeded once
  from what you currently have equipped and locked, so nothing changes silently — check the log line and
  prune the list to taste.
- New **Add from inventory** picker: search, multi-select, and see level, remaining boosts and where each
  item is. No more typing item IDs.
- Reordering: multi-select, Top/Up/Down/Bottom, Alt+↑/↓ and Alt+Home/End, and drag & drop. The list keeps
  your selection visible after a move, and shows a live "will boost now" readout.
- Merging is now governed only by the transform-chain toggles.
```

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test tests/NGUAdvisor.Tests`
Expected: `Passed! - Failed: 0, Passed: 59`.

- [ ] **Step 6: In-game verification pass**

Build, deploy over `injector/NGUAdvisor.dll`, restart NGU Idle, run `Run NGU Advisor.bat`, then check each of these:

1. `logs/debug.log` — first `UI metrics:` line present; **zero** `UI AUDIT` lines.
2. `logs/inject.log` — the `Boost priority seeded once: …` line appears exactly once. Restart the game and confirm it does NOT appear again.
3. Systems → Boosts → MANUAL: the seeded list contains your equipped gear; the blacklist is gone; the WILL BOOST NOW readout is populated.
4. **Add from inventory**: search filters; `Needs boosts only` hides maxed items; rows already in the list read "already in list" and cannot be selected; Add appends them.
5. Reordering: Top/Up/Down/Bottom with a multi-selection; Alt+↑/↓ and Alt+Home/End; the selection stays visible.
6. Drag one row and a multi-selection. **If either misbehaves**, set `"_boostDragReorderOff": true` in `settings.json`, confirm the buttons still work, and report the behavior.
7. Remove an item and confirm it disappears from WILL BOOST NOW.
8. A maxed chain copy under "Keep max lvl" must NOT appear in WILL BOOST NOW.

- [ ] **Step 7: Verification checkpoint**

All eight checks pass. Commit if this is a git repository:

```bash
git add docs CHANGELOG.md
git commit -m "Document the boosts priority-only change"
```

---

## Self-review notes

- **Spec coverage:** §1 → Task 3; §2 → Task 3; §3 → Task 2; §4 → Tasks 1 + 4; §5 → Task 6; §6 → Tasks 7 + 8; §7 → Task 5; §8 → Task 7 (layout) ; §9 → Task 9; §10 → Task 9.
- **Naming consistency:** `SeedPriorityBoosts` (Task 1) is the name called in Task 4; `BoostPickerForm.Pick` (Task 6) is the name called in Task 7; `MovePrioBlock`/`ReselectRange` (Task 7) are the names used in Task 8; `BoostDragReorderOff` (Task 2) is the name read in Task 8.
- **Known deviation from the skill's template:** the repository has no git, so "Commit" steps are conditional. Do not run `git init` — that is the user's call.
