# Gear Priority Chain + Pinned Items — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let one gear breakpoint express an ordered chain of objectives with per-priority accessory budgets, plus a global "always wear these items" pin list — and fix `Settings.ITOPODCombatMode` silently resetting to Idle.

**Architecture:** Port the reference optimizer's chain semantics (`sagas/optimize.worker.js:29` driver + `Optimizer.js:135 count_accslots` budgeting + `construct_base` locks) while keeping the native per-priority search (coordinate ascent + greedy fill + local swap). The chain arithmetic and the profile/validation surface are extracted into Unity-free files so they are unit-tested; only the live-inventory wiring is verified by build + diagnostic diff.

**Tech Stack:** C# net48 (game DLL) / net9.0 xUnit (tests), WinForms, SimpleJSON.

## Global Constraints

- **net48 is mandatory** for `NGUAdvisor.csproj`. Never change `TargetFramework`.
- **Build:** `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release`. **Tests:** `dotnet test tests/NGUAdvisor.Tests`.
- **Do NOT commit.** The repo owner's git rules forbid commits unless explicitly asked. Each task ends by stopping for review with the working tree dirty. (This deliberately replaces the writing-plans skill's default "commit" step.)
- **Files linked into the test project must stay Unity-free** — no `Main.`, no `Character`, no `UnityEngine`. This applies to `GearChain.cs`, `GearObjectives.cs`, `GearScorer.cs`, `ProfileModel.cs`, `ProfileValidator.cs`.
- **Style:** match the surrounding file. `Managers/` uses `var` and expression-bodied members throughout; follow it rather than the global "no `var`" preference, per this repo's "Match existing patterns" instruction. Flag to the owner if they'd rather have explicit types.
- **Main-thread rule:** anything reading live inventory or equipping runs on the Unity main thread. WinForms handlers set flags; they never call allocation/game code.
- **DPI:** every hand-placed pixel goes through `UiTheme.S(n)`; anything holding text takes its height from `SText`/`SHead`/`SCtl`/`SLines`. After any UI change `debug.log` must show zero `UI AUDIT` lines.
- **Never rename a shipped objective or chain name** — profiles and settings persist the name (`GearObjectives.md`).
- Spec: `docs/superpowers/specs/2026-08-10-gear-priority-chain-design.md`.

## File Structure

| File | Responsibility |
|---|---|
| `NGUAdvisor/SavedSettings.cs` | **Modify** — fix `_itopodCombatMode` predicate; add `PinnedGearIds` |
| `NGUAdvisor/Managers/GearChain.cs` | **Create** — `GearPriority`, named chain presets, slot budgeting. Unity-free, unit-tested |
| `NGUAdvisor/Managers/GearOptimizer.cs` | **Modify** — extract per-objective scoring context; pins + chain execution |
| `NGUAdvisor/Managers/ProfileModel.cs` | **Modify** — `Priorities` round-trip on gear breakpoints |
| `NGUAdvisor/Managers/ProfileValidator.cs` | **Modify** — warnings for malformed chains |
| `NGUAdvisor/AllocationProfiles/Breakpoints/GearBreakpoints.cs` | **Modify** — parse `Priorities`, resolve chain, expose active chain |
| `NGUAdvisor/Managers/AdvisorApply.cs` | **Modify** — re-optimize path uses the chain; score compared on priority 0 |
| `NGUAdvisor/GearEditorPanel.cs` | **Modify** — chain rows in the gear card |
| `NGUAdvisor/BasicSettingsPanel.cs` | **Modify** — pinned-items list |
| `tests/NGUAdvisor.Tests/*` | **Modify/Create** — link the new pure files; chain, round-trip, validator tests |

**Ordering note:** Tasks 2–4 are pure and fully testable. Task 5 is a behavior-preserving refactor gated on a diagnostic diff. Tasks 6–9 build on it. Task 1 is independent and can land first.

---

### Task 1: Fix ITOPOD combat mode resetting to Idle

**Files:**
- Modify: `NGUAdvisor/SavedSettings.cs:447`
- Test: none possible — `SavedSettings` is not Unity-free and is not linked into the test project.

**Interfaces:**
- Consumes: nothing.
- Produces: nothing. Self-contained one-line fix.

**Background:** `AdventurePanel.cs:364` offers four ITOPOD combat modes (`Idle=0, Snipe=1, Defensive=2, Offensive=3`). `AssignValue` (`SavedSettings.cs:290`) assigns `defaultValue` — not the stored value — when its predicate fails, so any mode above 1 is discarded on load and the dropdown snaps back to Idle.

- [ ] **Step 1: Read the surrounding block to confirm the sibling convention**

Read `NGUAdvisor/SavedSettings.cs` lines 436–452. Confirm `_combatMode` (:440), `_questCombatMode` (:475) and `_titanCombatMode` (:436) all use `mode >= 0 && mode <= 4`, and that `_itopodOptimizeMode` (:450) uses `<= 3` for its four items.

- [ ] **Step 2: Widen the predicate**

Change line 447 from:

```csharp
            AssignValue(ref _itopodCombatMode, other?.ITOPODCombatMode, (mode) => mode >= 0 && mode <= 1);
```

to:

```csharp
            AssignValue(ref _itopodCombatMode, other?.ITOPODCombatMode, (mode) => mode >= 0 && mode <= 4);
```

Do not touch `_itopodOptimizeMode` on line 450 — it is already correct.

- [ ] **Step 3: Build**

Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release`
Expected: build succeeds, 0 errors.

- [ ] **Step 4: Manual verification (requires the game running)**

Deploy the DLL, open Combat → ADVENTURE → ITOPOD, set **Combat** to `Offensive`. Confirm `%UserProfile%\AppData\LocalLow\NGUAdvisor\settings.json` contains `"_itopodCombatMode": 3`. Touch `settings.json` to trigger the `FileSystemWatcher` reload, then reopen the panel: the dropdown must still read `Offensive`.

- [ ] **Step 5: Stop for review**

Do not commit. Report the build result and, if the game was available, the manual check.

---

### Task 2: `GearChain` — priority model, presets, slot budgeting

**Files:**
- Create: `NGUAdvisor/Managers/GearChain.cs`
- Modify: `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj`
- Test: `tests/NGUAdvisor.Tests/GearChainTests.cs` (create)

**Interfaces:**
- Consumes: `GearObjectives.Objective`, `GearObjectives.Objectives` (from `Managers/GearObjectives.cs`, already Unity-free).
- Produces:
  - `class GearPriority { GearObjectives.Objective Objective; int MaxAccessorySlots; }`
  - `const int GearChain.Unlimited = int.MaxValue`
  - `const int GearChain.MaxPriorities = 5`
  - `IReadOnlyList<GearChain.Preset> GearChain.Presets` where `class Preset { string Name; IReadOnlyList<GearPriority> Priorities; }`
  - `GearChain.Preset GearChain.FindPreset(string name)` — case-insensitive, null if unknown
  - `int[] GearChain.SlotBudget(int totalAccSlots, int pinnedAccCount, IReadOnlyList<GearPriority> chain)`

**Why a separate file and not `GearObjectives.Objectives`:** `GearOptimizerDiagnostic.Run()` iterates `GearObjectives.Objectives` and optimizes each one. That loop is the regression harness for Task 5. Adding chain presets to that same list would change the diagnostic's output and destroy the comparison.

**Budget rule** — a direct port of `Optimizer.js:135 count_accslots`:

```js
let accslots = this.accslots - base_layout.counts['accessory'];
accslots = this.maxslots < accslots ? this.maxslots : accslots;
```

i.e. each priority in order gets `min(itsBudget, slotsStillFree)`, and consumes what it gets.

- [ ] **Step 1: Write the failing tests**

Create `tests/NGUAdvisor.Tests/GearChainTests.cs`:

```csharp
using System.Collections.Generic;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class GearChainTests
    {
        private static GearPriority P(string objective, int slots)
            => new GearPriority { Objective = GearChain.FindObjective(objective), MaxAccessorySlots = slots };

        [Fact]
        public void SlotBudget_SplitsFreeSlotsInPriorityOrder()
        {
            var chain = new List<GearPriority> { P("Adventure", 3), P("Energy NGU", 2), P("Respawn", 1) };
            Assert.Equal(new[] { 3, 2, 1 }, GearChain.SlotBudget(6, 0, chain));
        }

        [Fact]
        public void SlotBudget_LaterPrioritiesStarveWhenSlotsRunOut()
        {
            var chain = new List<GearPriority> { P("Adventure", 3), P("Energy NGU", 2), P("Respawn", 1) };
            Assert.Equal(new[] { 3, 1, 0 }, GearChain.SlotBudget(4, 0, chain));
        }

        [Fact]
        public void SlotBudget_PinnedAccessoriesReduceTheFreePool()
        {
            var chain = new List<GearPriority> { P("Adventure", 3), P("Respawn", 1) };
            Assert.Equal(new[] { 2, 0 }, GearChain.SlotBudget(4, 2, chain));
        }

        [Fact]
        public void SlotBudget_UnlimitedTakesEverythingLeft()
        {
            var chain = new List<GearPriority> { P("Respawn", 1), P("Adventure", GearChain.Unlimited) };
            Assert.Equal(new[] { 1, 5 }, GearChain.SlotBudget(6, 0, chain));
        }

        [Fact]
        public void SlotBudget_MorePinsThanSlotsYieldsNoBudget()
        {
            var chain = new List<GearPriority> { P("Adventure", GearChain.Unlimited) };
            Assert.Equal(new[] { 0 }, GearChain.SlotBudget(2, 5, chain));
        }

        [Fact]
        public void FindPreset_IsCaseInsensitiveAndReturnsNullForUnknown()
        {
            Assert.NotNull(GearChain.FindPreset("adventure + respawn"));
            Assert.Null(GearChain.FindPreset("no such chain"));
        }

        [Fact]
        public void EveryPresetResolvesItsObjectivesAndRespectsTheLengthCap()
        {
            foreach (var preset in GearChain.Presets)
            {
                Assert.NotEmpty(preset.Priorities);
                Assert.True(preset.Priorities.Count <= GearChain.MaxPriorities);
                foreach (var priority in preset.Priorities)
                    Assert.NotNull(priority.Objective);
            }
        }

        [Fact]
        public void PresetNamesDoNotCollideWithObjectiveNames()
        {
            foreach (var preset in GearChain.Presets)
                Assert.Null(GearOptimizer_FindObjectiveByName(preset.Name));
        }

        private static GearObjectives.Objective GearOptimizer_FindObjectiveByName(string name)
            => GearChain.FindObjective(name);
    }
}
```

- [ ] **Step 2: Link the pure sources into the test project**

In `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj`, add to the existing `<ItemGroup>` of `<Compile Include=...>` entries:

```xml
    <Compile Include="..\..\NGUAdvisor\Managers\GearObjectives.cs" Link="Linked\GearObjectives.cs" />
    <Compile Include="..\..\NGUAdvisor\Managers\GearChain.cs" Link="Linked\GearChain.cs" />
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~GearChainTests"`
Expected: FAIL — compile error, `GearChain` and `GearPriority` do not exist.

- [ ] **Step 4: Write `GearChain.cs`**

Create `NGUAdvisor/Managers/GearChain.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // One step of a gear priority chain: an objective plus how many of the still-free accessory
    // slots it is allowed to claim. Ported from the reference optimizer's (factor, maxslots) pair
    // -- external/gear-optimizer/src/Optimizer.js:262.
    public class GearPriority
    {
        public GearObjectives.Objective Objective;
        public int MaxAccessorySlots = GearChain.Unlimited;
    }

    // The chain layer: ordered objectives, each with an accessory budget.
    //
    // Why this exists: GearOptimizer scores ONE objective, so it fills every accessory slot with the
    // same stat (all-Power accessories under "Adventure"). The reference optimizer instead runs its
    // priorities in sequence -- sagas/optimize.worker.js:31 -- each claiming at most maxslots of the
    // remaining free accessory slots, which is what produces mixed sets.
    //
    // Presets live HERE and not in GearObjectives.Objectives on purpose: GearOptimizerDiagnostic
    // iterates that list and optimizes every entry, and it is the regression harness for the
    // optimizer refactor. Adding chains there would change its output.
    //
    // Unity-free (linked into tests) -- keep it that way.
    public static class GearChain
    {
        public const int Unlimited = int.MaxValue;

        // The reference caps its priority list at 5 (state.factors); native adopts the same cap so a
        // runaway chain cannot multiply the per-priority optimize cost without bound.
        public const int MaxPriorities = 5;

        public class Preset
        {
            public readonly string Name;
            public readonly IReadOnlyList<GearPriority> Priorities;
            public Preset(string name, IReadOnlyList<GearPriority> priorities) { Name = name; Priorities = priorities; }
        }

        public static GearObjectives.Objective FindObjective(string name)
            => GearObjectives.Objectives.FirstOrDefault(o =>
                string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

        private static GearPriority Step(string objective, int slots)
            => new GearPriority { Objective = FindObjective(objective), MaxAccessorySlots = slots };

        // Named chains, selectable exactly like an objective. Both repeat their lead objective as the
        // final unlimited step: reserve a couple of slots for the secondary stat, then fill whatever
        // is left with the lead again. Expressing a reserve this way needs no new grammar -- the same
        // objective may appear more than once in a chain.
        public static readonly IReadOnlyList<Preset> Presets = new List<Preset>
        {
            // Adventure that always keeps a respawn accessory. The TopRespawn pin only fires when the
            // loadout has NO respawn at all, so on merit-respawn gear it never engages; this reserves
            // a slot unconditionally.
            new Preset("Adventure + Respawn", new List<GearPriority>
            {
                Step("Adventure", 3),
                Step("Respawn", 1),
                Step("Adventure", Unlimited),
            }),
            // Adventure that keeps energy-support accessories instead of stacking pure Power.
            new Preset("Adventure + Energy", new List<GearPriority>
            {
                Step("Adventure", 3),
                Step("Energy NGU", 2),
                Step("Adventure", Unlimited),
            }),
        };

        public static Preset FindPreset(string name)
            => Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        // How many accessory slots each priority actually gets.
        //
        // Exact port of Optimizer.js:135 count_accslots:
        //     accslots = this.accslots - base_layout.counts['accessory'];
        //     accslots = this.maxslots < accslots ? this.maxslots : accslots;
        // -- the budget applies to the slots still FREE, and each priority consumes what it takes.
        public static int[] SlotBudget(int totalAccSlots, int pinnedAccCount, IReadOnlyList<GearPriority> chain)
        {
            var budget = new int[chain?.Count ?? 0];
            if (chain == null || chain.Count == 0) return budget;

            var free = Math.Max(0, totalAccSlots - Math.Max(0, pinnedAccCount));
            for (var i = 0; i < chain.Count; i++)
            {
                var want = Math.Max(0, chain[i].MaxAccessorySlots);
                var take = Math.Min(want, free);
                budget[i] = take;
                free -= take;
            }
            return budget;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~GearChainTests"`
Expected: PASS, 8 tests.

- [ ] **Step 6: Run the full suite and build the game DLL**

Run: `dotnet test tests/NGUAdvisor.Tests`
Expected: all pass, no regressions.

Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release`
Expected: succeeds.

- [ ] **Step 7: Stop for review**

Do not commit.

---

### Task 3: `Priorities` round-trips through `ProfileModel`

**Files:**
- Modify: `NGUAdvisor/Managers/ProfileModel.cs` (`ListBreakpoint` ~line 50, `LoadList` ~line 214, `ListToJson` ~line 448)
- Test: `tests/NGUAdvisor.Tests/ProfileModelRoundTripTests.cs` (add cases)

**Interfaces:**
- Consumes: nothing from Task 2 — `ProfileModel` stays a self-contained DTO with no dependency on `GearChain`, so it keeps its "zero UI / game dependencies" property. The mapping from DTO to `GearPriority` happens in `GearBreakpoints` (Task 8).
- Produces:
  - `class ProfileModel.GearPriorityEntry { string Objective; int Slots; }` — `Slots == 0` means unlimited
  - `List<GearPriorityEntry> ProfileModel.ListBreakpoint.Priorities`

**Background:** `ProfileModel`'s contract (its header comment at line 13) is that every unmodeled key passes through **verbatim** via `Extras`, so a round-trip can never lose data. `Priorities` would already survive untouched as an `Extras` entry — modeling it is what lets the editor show and edit it.

Profile grammar being added:

```json
{
  "Time": 0,
  "Priorities": [
    { "Objective": "Adventure",  "Slots": 3 },
    { "Objective": "Energy NGU", "Slots": 2 },
    { "Objective": "Respawn",    "Slots": 1 }
  ]
}
```

- [ ] **Step 1: Write the failing tests**

Append to `tests/NGUAdvisor.Tests/ProfileModelRoundTripTests.cs` (inside the existing test class; match its existing helper usage for load/save — read the file first and reuse whatever it already uses to parse and re-emit a profile):

```csharp
        [Fact]
        public void GearPrioritiesLoadIntoTypedEntries()
        {
            const string json = @"{""Breakpoints"":{""Gear"":[{""Time"":0,""ID"":[],""Priorities"":[
                {""Objective"":""Adventure"",""Slots"":3},
                {""Objective"":""Energy NGU"",""Slots"":2},
                {""Objective"":""Respawn""}]}]}}";

            var model = ProfileModel.Load(json);

            var bp = Assert.Single(model.Gear);
            Assert.Equal(3, bp.Priorities.Count);
            Assert.Equal("Adventure", bp.Priorities[0].Objective);
            Assert.Equal(3, bp.Priorities[0].Slots);
            Assert.Equal("Energy NGU", bp.Priorities[1].Objective);
            Assert.Equal(2, bp.Priorities[1].Slots);
            Assert.Equal("Respawn", bp.Priorities[2].Objective);
            Assert.Equal(0, bp.Priorities[2].Slots);   // omitted Slots == unlimited
        }

        [Fact]
        public void GearPrioritiesSurviveARoundTrip()
        {
            const string json = @"{""Breakpoints"":{""Gear"":[{""Time"":0,""ID"":[],""Priorities"":[
                {""Objective"":""Adventure"",""Slots"":3},
                {""Objective"":""Respawn"",""Slots"":1}]}]}}";

            var reloaded = ProfileModel.Load(ProfileModel.Load(json).Save());

            var bp = Assert.Single(reloaded.Gear);
            Assert.Equal(2, bp.Priorities.Count);
            Assert.Equal("Adventure", bp.Priorities[0].Objective);
            Assert.Equal(3, bp.Priorities[0].Slots);
            Assert.Equal("Respawn", bp.Priorities[1].Objective);
            Assert.Equal(1, bp.Priorities[1].Slots);
        }

        [Fact]
        public void GearBreakpointWithoutPrioritiesEmitsNoPrioritiesKey()
        {
            const string json = @"{""Breakpoints"":{""Gear"":[{""Time"":0,""ID"":[1,2],""Objective"":""Adventure""}]}}";

            var saved = ProfileModel.Load(json).Save();

            Assert.DoesNotContain("Priorities", saved);
        }
```

If `ProfileModel.Load`/`Save` are not the actual entry point names, read the top of `ProfileModelRoundTripTests.cs` and use whatever the existing tests call; do not invent an API.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~ProfileModelRoundTripTests"`
Expected: FAIL — `ListBreakpoint` has no `Priorities`.

- [ ] **Step 3: Add the DTO**

In `NGUAdvisor/Managers/ProfileModel.cs`, alongside the existing nested breakpoint types, add:

```csharp
        // Gear only: one step of a priority chain. Slots == 0 means "all remaining accessory slots".
        // Kept as a plain DTO -- ProfileModel has no dependency on GearChain so it stays Unity-free
        // and round-trip-testable; GearBreakpoints maps these onto GearPriority.
        public class GearPriorityEntry
        {
            public string Objective = "";
            public int Slots;
        }
```

and in `ListBreakpoint` (next to the existing `Objective` / `ForceRespawn` fields at ~line 54):

```csharp
            // Gear only: an ordered objective chain. When non-empty it supersedes Objective.
            public readonly List<GearPriorityEntry> Priorities = new List<GearPriorityEntry>();
```

- [ ] **Step 4: Load it**

In `LoadList`'s per-key loop (~line 217, where `Objective`, `TopRespawn` and `Challenge` are handled), add before the `IsCommentKey` check:

```csharp
                        if (kv.Key == "Priorities")
                        {
                            if (kv.Value != null && kv.Value.IsArray)
                                foreach (var step in kv.Value.AsArray.Children)
                                    b.Priorities.Add(new GearPriorityEntry
                                    {
                                        Objective = step["Objective"]?.Value ?? "",
                                        Slots = step["Slots"]?.AsInt ?? 0,
                                    });
                            continue;
                        }
```

The `continue` is what stops the key also landing in `Extras` — without it the key would be emitted twice on save.

- [ ] **Step 5: Save it**

In `ListToJson` (~line 451, next to the existing `Objective` / `TopRespawn` emission), add:

```csharp
                if (b.Priorities.Count > 0)
                {
                    var steps = new JSONArray();
                    foreach (var p in b.Priorities)
                    {
                        var step = new JSONObject();
                        step["Objective"] = p.Objective;
                        if (p.Slots > 0) step["Slots"] = p.Slots;
                        steps.Add(step);
                    }
                    o["Priorities"] = steps;
                }
```

Emitting `Slots` only when positive keeps "unlimited" written the way it was read, so a round-trip is textually stable.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~ProfileModelRoundTripTests"`
Expected: PASS, including every pre-existing round-trip test.

- [ ] **Step 7: Full suite + build**

Run: `dotnet test tests/NGUAdvisor.Tests` then `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release`
Expected: both clean.

- [ ] **Step 8: Stop for review**

Do not commit.

---

### Task 4: Validator warnings for malformed chains

**Files:**
- Modify: `NGUAdvisor/Managers/ProfileValidator.cs` (`Warnings`, line 64)
- Test: `tests/NGUAdvisor.Tests/ProfileValidatorWarningTests.cs` (add cases)

**Interfaces:**
- Consumes: `GearChain.MaxPriorities`, `GearChain.FindObjective` (Task 2). Both are Unity-free, so `ProfileValidator` keeps its "zero game/Unity dependencies" property. `GearObjectives.cs` and `GearChain.cs` are already linked into the test project by Task 2.
- Produces: additional strings in the existing `List<string> ProfileValidator.Warnings(string json)`.

**Why warnings and not failures:** the file header states warnings are "Advice, never a failure" — a profile naming an objective that a future build renames must still load. A refused chain step is skipped by `GearBreakpoints`, matching how `SpendPlanner` refuses an ambiguous name rather than mis-buying.

- [ ] **Step 1: Write the failing tests**

Append to `tests/NGUAdvisor.Tests/ProfileValidatorWarningTests.cs` (read the file first and match how its existing tests call `Warnings`):

```csharp
        [Fact]
        public void UnknownChainObjectiveIsWarned()
        {
            const string json = @"{""Breakpoints"":{""Gear"":[{""Time"":0,""ID"":[],
                ""Priorities"":[{""Objective"":""Definitely Not An Objective"",""Slots"":2}]}]}}";

            Assert.Contains(ProfileValidator.Warnings(json),
                w => w.Contains("Definitely Not An Objective"));
        }

        [Fact]
        public void NegativeSlotCountIsWarned()
        {
            const string json = @"{""Breakpoints"":{""Gear"":[{""Time"":0,""ID"":[],
                ""Priorities"":[{""Objective"":""Adventure"",""Slots"":-1}]}]}}";

            Assert.Contains(ProfileValidator.Warnings(json), w => w.Contains("Slots"));
        }

        [Fact]
        public void ChainLongerThanTheCapIsWarned()
        {
            const string json = @"{""Breakpoints"":{""Gear"":[{""Time"":0,""ID"":[],""Priorities"":[
                {""Objective"":""Adventure"",""Slots"":1},{""Objective"":""Adventure"",""Slots"":1},
                {""Objective"":""Adventure"",""Slots"":1},{""Objective"":""Adventure"",""Slots"":1},
                {""Objective"":""Adventure"",""Slots"":1},{""Objective"":""Adventure"",""Slots"":1}]}]}}";

            Assert.Contains(ProfileValidator.Warnings(json), w => w.Contains("5"));
        }

        [Fact]
        public void AValidChainProducesNoChainWarnings()
        {
            const string json = @"{""Breakpoints"":{""Gear"":[{""Time"":0,""ID"":[],""Priorities"":[
                {""Objective"":""Adventure"",""Slots"":3},{""Objective"":""Respawn"",""Slots"":1}]}]}}";

            Assert.DoesNotContain(ProfileValidator.Warnings(json), w => w.Contains("gear priority"));
        }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~ProfileValidatorWarningTests"`
Expected: FAIL — no chain warnings are produced.

- [ ] **Step 3: Implement**

Inside `ProfileValidator.Warnings`, after the existing augment checks, add a gear-chain pass. Read how `Warnings` currently walks the parsed JSON (it already reaches into `Breakpoints`) and reuse that traversal rather than re-parsing. The checks:

```csharp
            // Gear priority chains: advice only. A step whose objective never resolves is SKIPPED by
            // GearBreakpoints rather than mis-applied (same refuse-don't-guess rule SpendPlanner uses
            // for perk names), so a silent skip would look like "the chain ran" while a slot budget
            // quietly vanished. Say so here instead.
            foreach (var bp in gearBreakpoints)
            {
                var steps = bp["Priorities"];
                if (steps == null || !steps.IsArray) continue;

                if (steps.AsArray.Count > GearChain.MaxPriorities)
                    warnings.Add($"A gear priority chain has {steps.AsArray.Count} steps; only the first "
                               + $"{GearChain.MaxPriorities} are used.");

                foreach (var step in steps.AsArray.Children)
                {
                    var name = step["Objective"]?.Value ?? "";
                    if (string.IsNullOrEmpty(name))
                        warnings.Add("A gear priority step has no Objective and will be skipped.");
                    else if (GearChain.FindObjective(name) == null)
                        warnings.Add($"Gear priority objective \"{name}\" is not recognized; that step will be skipped.");

                    var slotsNode = step["Slots"];
                    if (slotsNode != null && slotsNode.AsInt < 0)
                        warnings.Add($"Gear priority \"{name}\" has negative Slots; it will claim no accessory slots.");
                }
            }
```

`gearBreakpoints` and `warnings` are placeholders for whatever the surrounding method already calls its accumulator and its `Breakpoints.Gear` array — use the existing names.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~ProfileValidatorWarningTests"`
Expected: PASS, including the pre-existing warning tests.

- [ ] **Step 5: Full suite + build**

Run: `dotnet test tests/NGUAdvisor.Tests` then `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release`

- [ ] **Step 6: Stop for review**

Do not commit.

---

### Task 5: Extract the per-objective scoring context (no behavior change)

**Files:**
- Modify: `NGUAdvisor/Managers/GearOptimizer.cs:165-449`
- Test: none — Unity dependencies. Verified by diagnostic diff.

**Interfaces:**
- Consumes: nothing new.
- Produces: a private nested `ScoreContext` inside `GearOptimizer` holding `statNames`, `exponents`, `constVals`, `idToVec`, `scratch` and `ScoreOf(Result)`. Task 6 builds one per priority.

**This task must not change any output.** It exists solely so Task 6 can score more than one objective in a single pass. `Optimize(objective, forceTopRespawn)` keeps its exact signature and semantics.

- [ ] **Step 1: Capture the regression baseline**

With the game running and the current DLL deployed, trigger `GearOptimizerDiagnostic.Run()` (Settings → diagnostic). Copy `%UserProfile%\AppData\LocalLow\NGUAdvisor\logs\gearopt-diagnostic.log` to a scratch path as `gearopt-before.log`.

**If the game is not available, stop and say so** — this task's only verification is this diff. Do not proceed on the assumption that a refactor of a 285-line method is safe because it compiles.

- [ ] **Step 2: Extract the scoring state**

In `Optimize`, the block currently spanning lines 210–275 (`statNames` through `ScoreOf`) captures one objective. Move it into a nested type built from `(objective, pools/idToItem, cube, baseItem, offhandFactor)`:

```csharp
        // Per-objective scoring state. Previously these were closures over Optimize's single objective;
        // a priority chain needs one of these per priority over the SAME candidate pools.
        //
        // The arithmetic below is unchanged and must stay an exact rewrite of GearScorer.GetRawVals +
        // ScoreVals -- see the equivalence argument in the comment carried down onto ScoreOf.
        private class ScoreContext
        {
            private readonly string[] _statNames;
            private readonly double[] _exponents;
            private readonly double[] _constVals;
            private readonly Dictionary<int, double[]> _idToVec;
            private readonly double[] _scratch;
            private readonly double _offhandFactor;

            public ScoreContext(GearObjectives.Objective obj,
                                Dictionary<int, GearScorer.Item> idToItem,
                                GearScorer.Item cube, GearScorer.Item baseItem, double offhandFactor)
            {
                _statNames = obj.Stats;
                _exponents = obj.Exponents;
                _offhandFactor = offhandFactor;
                var statCount = _statNames.Length;

                _constVals = new double[statCount];
                for (var i = 0; i < statCount; i++)
                    _constVals[i] = GearScorer.BaseValue(_statNames[i]);
                var cubeVec = VecOf(cube);
                var baseVec = VecOf(baseItem);
                for (var i = 0; i < statCount; i++)
                    _constVals[i] += cubeVec[i] + baseVec[i];

                _idToVec = new Dictionary<int, double[]>(idToItem.Count);
                foreach (var kv in idToItem)
                    _idToVec[kv.Key] = VecOf(kv.Value);

                _scratch = new double[statCount];
            }

            private double[] VecOf(GearScorer.Item it)
            {
                var v = new double[_statNames.Length];
                if (it?.Stats == null) return v;
                for (var i = 0; i < _statNames.Length; i++)
                    if (it.Stats.TryGetValue(_statNames[i], out double d) && !double.IsNaN(d))
                        v[i] = d;
                return v;
            }

            public double ScoreOf(Result r)
            {
                var statCount = _statNames.Length;
                Array.Copy(_constVals, _scratch, statCount);

                void AddId(int id)
                {
                    if (id == 0 || !_idToVec.TryGetValue(id, out var v)) return;
                    for (var i = 0; i < statCount; i++) _scratch[i] += v[i];
                }

                // Weapons first, in list order, so the mainhand/offhand split matches GetRawVals.
                if (r.MainWeapon != 0)
                {
                    AddId(r.MainWeapon);
                    if (r.OffWeapon != 0 && _idToVec.TryGetValue(r.OffWeapon, out var off))
                        for (var i = 0; i < statCount; i++) _scratch[i] += off[i] * _offhandFactor;
                }
                else
                {
                    // No mainhand: the offhand IS the first weapon and takes its full value.
                    AddId(r.OffWeapon);
                }

                AddId(r.Head); AddId(r.Chest); AddId(r.Legs); AddId(r.Boots);
                for (var a = 0; a < r.Accessories.Count; a++) AddId(r.Accessories[a]);

                double res = 1.0;
                for (var i = 0; i < statCount; i++)
                {
                    var v = _scratch[i] / 100.0;
                    if (_exponents != null && _exponents.Length > i)
                        v = Math.Pow(v, _exponents[i]);
                    res *= v;
                }
                return res;
            }
        }
```

**Carry the whole comment block from `GearOptimizer.cs:194-209` onto `ScoreContext`** — it is the proof that this is an exact rewrite of `GearScorer.GetRawVals + ScoreVals`, and `gear-optimizer-comparison.md:16` requires any scoring change to be mirrored in both places.

- [ ] **Step 3: Rewire the search helpers**

`PickSlot`, `MainAscent`, `AccessoryOptimize`, `RunOptimize` currently call the closure `ScoreOf()`. Give each of them a `ScoreContext ctx` parameter (or hoist them into a small private search type holding `ctx`, `pools`, `r`, `accSlots`, `twoWeapons`, `accSet`). Every `ScoreOf()` becomes `ctx.ScoreOf(r)`. **Do not change any loop bound, epsilon, ordering, or the `accSet` uniqueness handling** — including the `accSet.Remove(cur)` / `accSet.Add(best)` dance at lines 350–358 and its explanatory comment.

- [ ] **Step 4: Keep `Optimize` behaviorally identical**

`Optimize(objective, forceTopRespawn)` now builds one `ScoreContext` and runs exactly the passes it runs today, including the `forceTopRespawn` pre-filter (`maxResp` scan, then a full `RunOptimize` only for candidates tied at the maximum) and the `s > bestScore * (1 + 1e-12)` tie-break.

- [ ] **Step 5: Build**

Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release`
Expected: succeeds.

- [ ] **Step 6: Diff the diagnostic — the acceptance gate**

Deploy, run the diagnostic again, save as `gearopt-after.log`, then:

Run: `diff gearopt-before.log gearopt-after.log`
Expected: **only the `Time:` line differs.** Every objective's `current=`, `optimized=`, multiplier and every picked item id must be identical. Any other difference means the refactor changed behavior — fix it before continuing; do not rationalize a score that moved in the last digit.

- [ ] **Step 7: Stop for review**

Do not commit. Report the diff output.

---

### Task 6: Pins and chain execution in `GearOptimizer`

**Files:**
- Modify: `NGUAdvisor/Managers/GearOptimizer.cs`
- Test: none — Unity dependencies. Verified by build + diagnostic + live comparison against the site.

**Interfaces:**
- Consumes: `GearPriority`, `GearChain.SlotBudget`, `GearChain.MaxPriorities` (Task 2); `ScoreContext` (Task 5).
- Produces:
  - `Result Optimize(IReadOnlyList<GearPriority> chain, IReadOnlyList<int> pinnedIds, bool forceTopRespawn)`
  - `int[] OptimizeIds(IReadOnlyList<GearPriority> chain, IReadOnlyList<int> pinnedIds, bool forceTopRespawn)`
  - `double CurrentScore(IReadOnlyList<GearPriority> chain)` — scores the equipped loadout on `chain[0].Objective`
  - Existing `Optimize(GearObjectives.Objective, bool)` / `OptimizeIds` / `CurrentScore(Objective)` keep working, delegating to the chain form.

**Semantics** (from the spec, itself read off the reference):

1. Build pools once — shared by every priority.
2. Place pins. A pinned id is looked up in the pools; main-slot pins occupy their slot, accessory pins append in configured order. Pinned main slots and pinned accessory indices are frozen for the whole run.
3. Priority 0 runs the full alternation (`MainAscent` ↔ `AccessoryOptimize`, ≤5 rounds), with `MainAscent` skipping frozen main slots and the accessory fill capped at `frozenAccCount + budget[0]`.
4. Freeze all main slots and every accessory filled so far.
5. Priority k ≥ 1 runs accessory-only fill + local swap over indices ≥ the frozen count, capped at `frozenCount + budget[k]`, using its own `ScoreContext`. Frozen items score through the normal `AddId` path, which is what makes "best marginal accessory given what is already worn" correct.
6. `forceTopRespawn` applies after the whole chain and only when neither pins nor chain produced any respawn; its candidate loop re-runs the whole chain per candidate.
7. `Result.Score` = priority 0's objective score.

- [ ] **Step 1: Add the chain entry point**

```csharp
        // Chain-aware optimize. The single-objective overload delegates here with a one-element chain
        // and no pins, so existing callers are unaffected.
        //
        // Ported from the reference driver -- external/gear-optimizer/src/sagas/optimize.worker.js:29:
        //     base = construct_base(locked, equip);           // pins
        //     for (idx...) base = compute_optimal(base, idx); // one priority at a time
        // Each priority claims at most its budget of the slots still free (Optimizer.js:135), and the
        // slots it fills are locked for every later priority. That sequencing -- not the search inside
        // a priority -- is what produces mixed accessory sets.
        public static Result Optimize(IReadOnlyList<GearPriority> chain, IReadOnlyList<int> pinnedIds,
                                      bool forceTopRespawn = false)
```

The single-objective overload becomes:

```csharp
        public static Result Optimize(GearObjectives.Objective obj, bool forceTopRespawn = false)
            => Optimize(new[] { new GearPriority { Objective = obj, MaxAccessorySlots = GearChain.Unlimited } },
                        null, forceTopRespawn);
```

- [ ] **Step 2: Implement pin placement**

Before any priority runs:

```csharp
            // Pins ("must have in every inventory"). Reference: construct_base(state.locked, state.equip).
            // A pinned id the player no longer owns simply is not in the pools -- skip it and log once
            // rather than throw; the pin list outlives the item.
            var pinnedMain = new HashSet<part>();
            var pinnedAccCount = 0;
            if (pinnedIds != null)
            {
                foreach (var id in pinnedIds)
                {
                    if (id == 0 || !idToItem.TryGetValue(id, out var item)) { skippedPins.Add(id); continue; }
                    var slot = PartOf(id, pools);
                    if (slot == part.Accessory)
                    {
                        // Truncating silently would read as "the optimizer ignored my pin".
                        if (pinnedAccCount >= accSlots) { droppedPins.Add(id); continue; }
                        r.Accessories.Add(id);
                        pinnedAccCount++;
                    }
                    else
                    {
                        if (pinnedMain.Contains(slot)) { droppedPins.Add(id); continue; }
                        AssignMainSlot(r, slot, id);
                        pinnedMain.Add(slot);
                    }
                }
            }
```

`PartOf` and `AssignMainSlot` are small private helpers; `AssignMainSlot` mirrors the existing `switch (p)` at lines 430–438 (weapon → `MainWeapon`, and if `MainWeapon` is already pinned, `OffWeapon` when `twoWeapons`). Log skipped and dropped pins once each via `Main.LogDebug` with the item name from `Main.ItemName(id)`.

- [ ] **Step 3: Implement the chain loop**

```csharp
            var steps = chain.Take(GearChain.MaxPriorities).Where(p => p?.Objective != null).ToList();
            if (steps.Count == 0) return r;

            var budget = GearChain.SlotBudget(accSlots, pinnedAccCount, steps);
            var frozenAccCount = pinnedAccCount;

            for (var k = 0; k < steps.Count; k++)
            {
                var ctx = new ScoreContext(steps[k].Objective, idToItem, cube, baseItem, Offhand / 100.0);
                var cap = Math.Min(accSlots, frozenAccCount + budget[k]);

                if (k == 0)
                    RunOptimize(ctx, cap, pinnedMain, frozenAccCount);   // main ascent + accessories
                else
                    AccessoryOptimize(ctx, cap, frozenAccCount);         // accessories only

                frozenAccCount = r.Accessories.Count;
                if (k == 0) mainFrozen = true;
            }

            r.Score = new ScoreContext(steps[0].Objective, idToItem, cube, baseItem, Offhand / 100.0).ScoreOf(r);
```

`AccessoryOptimize` gains two parameters: `cap` (replacing its `accSlots` bound in both the greedy `while` and the swap loop) and `firstFree` (replacing today's `fixedCount`, so frozen slots are never re-picked). `MainAscent` skips any slot in `pinnedMain`, and is not called at all once `mainFrozen` is set.

Hoist the `ScoreContext` for priority 0 into a local instead of constructing it twice.

- [ ] **Step 4: Keep `forceTopRespawn` correct**

The existing `HasRespawn()` check and candidate loop stay, but each candidate now re-runs the **whole chain** with the respawn item pinned (add it to `pinnedIds` for that trial), not a single `RunOptimize`. The `maxResp` pre-filter, the "highest respawn wins outright, score breaks ties" rule and the `1 + 1e-12` epsilon are unchanged — keep the comment at lines 392–412 intact.

- [ ] **Step 5: Add the chain-aware `CurrentScore`**

```csharp
        // AdvisorApply compares CurrentScore against Optimize().Score behind a 5% re-equip bar. A chain
        // has no single score, so BOTH sides measure priority 0's objective -- otherwise the bar is
        // comparing two different quantities. See the spec's "Result.Score under a chain".
        public static double CurrentScore(IReadOnlyList<GearPriority> chain)
            => chain == null || chain.Count == 0 || chain[0]?.Objective == null
                ? 0
                : CurrentScore(chain[0].Objective);
```

- [ ] **Step 6: Build**

Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release`

- [ ] **Step 7: Regression — the no-pins, no-chain path is untouched**

Deploy, run `GearOptimizerDiagnostic.Run()` (it still calls the single-objective overload for every objective), diff against `gearopt-before.log` from Task 5.
Expected: only the `Time:` line differs. This is the backward-compatibility criterion from the spec.

- [ ] **Step 8: New-behavior check against the site**

In game: F3 quicksave writes `NGUSave.json`. Load it into the web Gear Optimizer, configure priorities `Adventure` (maxslots 3) → `Respawn` (1) → `Adventure` (unlimited). Run the native chain over the same save and compare the accessory picks. Small divergences are expected and acceptable — native uses coordinate ascent where the site uses Pareto expansion — but the **shape** must match: 3 Power accessories, 1 respawn accessory, remainder Power. If native produces zero respawn accessories, the budget is not being applied; debug before continuing.

- [ ] **Step 9: Stop for review**

Do not commit. Report both diffs.

---

### Task 7: Global pinned-items setting

**Files:**
- Modify: `NGUAdvisor/SavedSettings.cs`
- Modify: `NGUAdvisor/Managers/GearOptimizer.cs` (entry points read the setting)
- Test: none.

**Interfaces:**
- Consumes: `Optimize(chain, pinnedIds, forceTopRespawn)` (Task 6).
- Produces: `int[] Main.Settings.PinnedGearIds` and `IReadOnlyList<int> GearOptimizer.ActivePins()`.

- [ ] **Step 1: Add the setting**

Follow the existing array-setting pattern in `SavedSettings.cs` exactly — read how `_titanLoadout` is declared, restored in the `AssignValues(ref _titanLoadout, other?.TitanLoadout, (id) => IsEquipment(id))` line (~:435) and exposed as a property with `SaveSettings()` in its setter, and mirror it:

```csharp
        [SerializeField] private int[] _pinnedGearIds;
```

restore with the same `IsEquipment` predicate:

```csharp
            AssignValues(ref _pinnedGearIds, other?.PinnedGearIds, (id) => IsEquipment(id));
```

and add the property with the same shape as its neighbors (setter assigns then calls `SaveSettings()`).

- [ ] **Step 2: Read the pins in every optimize entry point**

```csharp
        // "Must have in every inventory" is ONE list, not one per breakpoint -- so it is a global
        // setting and every entry point honors it, including titan and gold gear resolution.
        public static IReadOnlyList<int> ActivePins()
        {
            try { return Main.Settings?.PinnedGearIds ?? new int[0]; }
            catch { return new int[0]; }
        }
```

Have `Optimize(GearObjectives.Objective, bool)` and the chain overload default `pinnedIds` to `ActivePins()` when the caller passes `null`. `ResolveModeGear`, `ResolveTitanGear` and `ResolveGoldGear` then pick pins up automatically.

**One deliberate exception:** `ResolveTitanGear`'s real-fight override (`GearOptimizer.cs:113`) exists because wearing loot gear in a live titan fight was a reported death loop. Pins must NOT override a real fight — when `realFight` is true, pass an empty pin list. Add a comment saying so.

- [ ] **Step 3: Build**

Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release`

- [ ] **Step 4: Live check**

Pin Ring of Greed. Trigger a gear pass on an `Adventure` breakpoint. Confirm the log line names the pin and that Ring of Greed is equipped. Then unequip/sell nothing but pin a non-existent id by hand-editing `settings.json`, reload, and confirm `debug.log` reports the skipped pin without an exception.

- [ ] **Step 5: Stop for review**

Do not commit.

---

### Task 8: Wire chains through `GearBreakpoints` and `AdvisorApply`

**Files:**
- Modify: `NGUAdvisor/AllocationProfiles/Breakpoints/GearBreakpoints.cs`
- Modify: `NGUAdvisor/Managers/AdvisorApply.cs:894-956`
- Test: none.

**Interfaces:**
- Consumes: `ProfileModel.GearPriorityEntry` shape (Task 3), `GearChain.FindPreset`/`FindObjective` (Task 2), `GearOptimizer.Optimize(chain, pins, force)` and `CurrentScore(chain)` (Task 6).
- Produces: `IReadOnlyList<GearPriority> GearBreakpoints.ActiveChain` (replacing the role of `ActiveObjective` for the re-optimize path; keep `ActiveObjective` as the priority-0 name so existing log lines and the `objectiveChanged` bookkeeping still read naturally).

- [ ] **Step 1: Parse `Priorities` in `ParseSpec`**

`GearSpec` (`GearBreakpoints.cs:12`) gains `public List<GearPriority> Priorities;`. In `ParseSpec` (line 28), after the existing `Objective` handling:

```csharp
            var chain = bp["Priorities"];
            if (chain != null && chain.IsArray)
            {
                spec.Priorities = new List<GearPriority>();
                foreach (var step in chain.AsArray.Children)
                {
                    var objective = GearChain.FindObjective(step["Objective"]?.Value ?? "");
                    // Refuse, don't guess: an unresolved name is SKIPPED and logged, never mapped onto a
                    // near-match. Same rule SpendPlanner applies to perk names.
                    if (objective == null)
                    {
                        Main.LogDebug($"Gear priority objective '{step["Objective"]?.Value}' not recognized; step skipped.");
                        continue;
                    }
                    var slots = step["Slots"]?.AsInt ?? 0;
                    spec.Priorities.Add(new GearPriority
                    {
                        Objective = objective,
                        MaxAccessorySlots = slots > 0 ? slots : GearChain.Unlimited,
                    });
                }
                if (spec.Priorities.Count > GearChain.MaxPriorities)
                    spec.Priorities = spec.Priorities.Take(GearChain.MaxPriorities).ToList();
            }
```

- [ ] **Step 2: Resolve the chain in `PerformSwap`**

In `PerformSwap` (line 46), the resolution order becomes: explicit `Priorities` → a named chain preset matching `Objective` → a single objective → the challenge default → the manual ID list. Concretely, before the existing `FindObjective` call:

```csharp
            List<GearPriority> chain = null;
            if (bp.priorities.Priorities != null && bp.priorities.Priorities.Count > 0)
                chain = bp.priorities.Priorities;
            else if (!string.IsNullOrEmpty(objectiveName))
            {
                var preset = GearChain.FindPreset(objectiveName);
                if (preset != null) chain = preset.Priorities.ToList();
            }
```

When `chain != null`, call `GearOptimizer.OptimizeIds(chain, null, forceRespawn)`, set `ActiveChain = chain` and `ActiveObjective = chain[0].Objective.Name`, and log the chain as `"Adventure(3) > Respawn(1) > Adventure(all)"` so `debug.log` says which chain ran. Otherwise the existing single-objective path is unchanged, with `ActiveChain` set to a one-element chain so downstream code has one shape to handle.

The `ChallengeDetector.DefaultGear` smart-default at line 56 keeps working — it yields an objective name, which flows into the preset lookup above.

- [ ] **Step 3: Use the chain in the re-optimize pass**

In `AdvisorApply.cs` around lines 929–956, replace `FindObjective(objName)` + `Optimize(obj, ...)` + `CurrentScore(obj)` with the `ActiveChain` equivalents. Both sides of the 5 % bar must use `chain[0]`'s objective — that is the whole point of `CurrentScore(chain)`.

Keep the `_lastGearObjective` bookkeeping semantics exactly as documented in the comment at lines 931–938: an objective switch bypasses the 5 % bar, and `_lastGearObjective` commits only when a pass actually resolves the switch. A **chain** switch counts as an objective switch — compare the rendered chain string, not just `chain[0].Objective.Name`, or swapping only the tail of a chain would never take effect.

- [ ] **Step 4: Build**

Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release`

- [ ] **Step 5: Live check**

Put a `Priorities` chain into a profile's gear breakpoint, activate it, and confirm `debug.log` logs the rendered chain and that the equipped accessories match its shape. Then confirm a profile with a plain `Objective` still behaves as before.

- [ ] **Step 6: Stop for review**

Do not commit.

---

### Task 9: Editor UI for chains and pins

**Files:**
- Modify: `NGUAdvisor/GearEditorPanel.cs` (source dropdown ~:235, `ObjectiveMode` ~:193, `ApplyMode` ~:366, `RecalcHeight` ~:357, `SourceChanged` ~:381)
- Modify: `NGUAdvisor/BasicSettingsPanel.cs` (pinned-items list)
- Test: none — verified by the `UI AUDIT` oracle.

**Interfaces:**
- Consumes: `ProfileModel.ListBreakpoint.Priorities` (Task 3), `GearChain.Presets` (Task 2), `Settings.PinnedGearIds` (Task 7).
- Produces: nothing consumed by later tasks.

**Read `docs/modules/ui-infra.md` §DPI calibration before placing any control.** The gear card already computes its own height (`RecalcHeight`, :357) and the profile editor has documented DPI debt (`ui-panels.md:68`) — every container holding a numeric derives from `UiTheme.NumH`, rows place children centred, button widths come from `UiLayout.BtnWidth`.

- [ ] **Step 1: Offer chain presets in the existing dropdown**

In the `_source` combo population (:235), after the objective entries:

```csharp
                foreach (var o in GearObjectives.Objectives) _source.Items.Add("Optimize: " + o.Name);
                foreach (var c in GearChain.Presets) _source.Items.Add("Optimize: " + c.Name);
```

Because preset names and objective names share one namespace (Task 2 asserts they never collide), `SourceChanged` (:381) needs no change — it already strips the `"Optimize: "` prefix and stores the bare name, and `GearBreakpoints` resolves preset-or-objective.

- [ ] **Step 2: Add the chain editor rows**

Extend `_objPanel` (:239) with a small list of chain steps: per row an objective `ComboBox` (populated from `GearObjectives.Objectives`), a `NumericUpDown` for `Slots` (minimum 0, 0 displayed as "all"), and ✕/↑/↓ buttons matching the existing row buttons' width via `UiLayout.BtnWidth`. Rows write into `_bp.Priorities` and raise the existing `OnChanged()`.

`ObjectiveMode` (:193) becomes `!string.IsNullOrEmpty(_bp.Objective) || _bp.Priorities.Count > 0`, and `RecalcHeight`'s `ObjInfoH` term (:360) grows by `rows * RowH` so the card is tall enough — a non-scrolling panel has no scrollbar to reach clipped content with (`ui-panels.md:39`).

When `_bp.Priorities` is non-empty, `_objInfo` should say the chain supersedes the single objective, so the two controls cannot silently disagree.

- [ ] **Step 3: Add the pinned-items list to settings**

In `BasicSettingsPanel.cs`, add a "Always equip these items" section backed by `Settings.PinnedGearIds`, following whatever paste/parse flow the panel already uses for gear ID lists — per `ui-panels.md:65`, paste flows parse+validate first, show the result for confirmation, change nothing on invalid or empty input, and offer a single-level undo. Reuse that; do not write a new one.

Remember `BasicSettingsPanel` does not scroll itself and grows to its content — re-derive its height after adding the section.

- [ ] **Step 4: Build and audit**

Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release`

Deploy, open the profile editor and the settings page, then check `debug.log`:
Expected: **zero `UI AUDIT` lines.** The first `UI metrics:` line names the calibration branch. Non-zero audit lines are the failure signal — fix them, do not explain them.

- [ ] **Step 5: Round-trip a profile through the editor**

Load a profile with a `Priorities` chain, open it in the editor, save without editing, and diff the file.
Expected: unchanged (the round-trip tests from Task 3 cover the model; this covers the editor path).

- [ ] **Step 6: Stop for review**

Do not commit.

---

### Task 10: Documentation

**Files:**
- Modify: `docs/modules/GearOptimizer.md`, `docs/modules/GearObjectives.md`, `docs/modules/gear-optimizer-comparison.md`, `docs/modules/AllocationProfiles.md`, `README.md`
- Create: `docs/modules/GearChain.md`

**Interfaces:** none.

The repo's own instruction: *"the docs carry the invariants, game-truth formulas, decomp provenance, and the user-reported bugs whose fixes must not be regressed"*, and *"If a module has no doc yet, write one when you finish working on it."*

- [ ] **Step 1: Write `docs/modules/GearChain.md`**

Cover: what a chain is; the budget rule with its `Optimizer.js:135` provenance; why presets live here and not in `GearObjectives.Objectives` (the diagnostic regression harness); the `MaxPriorities = 5` cap and where it comes from; the "a preset name and an objective name share one namespace, and neither is ever renamed" rule.

- [ ] **Step 2: Update `GearOptimizer.md`**

New entry points and their signatures; the pins → chain → TopRespawn ordering; the `Result.Score` = priority 0 rule and *why* (the `AdvisorApply` 5 % bar); that pins are skipped when not owned and truncated-with-a-log when they exceed the accessory slots; that `ResolveTitanGear`'s real-fight override drops pins.

- [ ] **Step 3: Update `GearObjectives.md`**

Note that chain presets live in `GearChain`, and that the never-rename rule now covers chain names too.

- [ ] **Step 4: Update `gear-optimizer-comparison.md`**

Close gap 2 (multi-priority chains) and gap 3 (locked items) in the §Gaps list — state what native now does and how it differs (native locks slots between priorities but searches with coordinate ascent, not Pareto expansion). Leave gaps 1, 4 and 5 open. Add the chain to the "Different by design" table.

- [ ] **Step 5: Document the profile grammar**

Add `Priorities` to `docs/modules/AllocationProfiles.md` and to the profile-grammar section of `README.md`, with the worked example from Task 3 and the "`Slots` omitted = all remaining" rule.

- [ ] **Step 6: Stop for review**

Do not commit.

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| A. ITOPOD combat mode | 1 |
| B. Reference semantics (chain + budget) | 2, 6 |
| B. Structural prerequisite (`ScoreContext`) | 5 |
| B. `Result.Score` under a chain | 6 (implementation), 8 (consumer), 10 (documented) |
| Config 1 — named chain presets | 2, 8, 9 |
| Config 2 — `Priorities` in the profile | 3, 4, 8, 9 |
| Config 3 — global pins | 6, 7, 9 |
| Backward compatibility | 5 step 6, 6 step 7 (diagnostic diff) |
| Risk: pins starve a chain | 6 step 2 (truncate + log) |
| Risk: pinned item not owned | 6 step 2 (skip + log), 7 step 4 |
| Risk: cost | 2 (`MaxPriorities = 5`), 8 step 1 (truncation) |
| Verification 1–4 | 6 steps 6–8, 9 step 4 |
| Docs to update | 10 |

No spec requirement is unassigned.

**Deviations from the spec, made deliberately while planning:**

1. The spec said `GearOptimizer` "cannot be unit-tested" and implied no tests were possible. Inspecting `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj` showed `ProfileModel.cs` and `ProfileValidator.cs` are already linked, and `GearObjectives.cs`/`GearScorer.cs` are Unity-free. So the chain arithmetic, the profile round-trip and the validation are TDD'd (Tasks 2–4); only the live-inventory wiring falls back to build + diagnostic diff.
2. Chain presets live in a new `GearChain.Presets`, not in `GearObjectives.Objectives`, because `GearOptimizerDiagnostic` iterates the latter and is the regression harness for Task 5.
3. Commit steps are replaced by "stop for review" throughout, per the repo owner's git rules.

**Type consistency:** `GearPriority.Objective` / `.MaxAccessorySlots`, `GearChain.Unlimited` / `.MaxPriorities` / `.Presets` / `.FindPreset` / `.FindObjective` / `.SlotBudget`, `ProfileModel.GearPriorityEntry.Objective` / `.Slots`, `GearBreakpoints.ActiveChain`, `GearOptimizer.ActivePins` / `.Optimize(chain, pins, force)` / `.CurrentScore(chain)` — each is defined once and referenced under the same name everywhere it appears. `ProfileModel` uses `Slots` (0 = unlimited); `GearPriority` uses `MaxAccessorySlots` (`GearChain.Unlimited` = unlimited); the mapping between the two happens in exactly one place, `GearBreakpoints.ParseSpec` (Task 8, Step 1).
