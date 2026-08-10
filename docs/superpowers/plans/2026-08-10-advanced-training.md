# Advanced Training Calculator — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the community AT Calculator into the advisor — time to an AT level, where the blitz-boost ceiling sits, the cap needed to blitz-boost for a given time, and the sheet's Time Machine tab — with the sheet's missing Apps Script functions derived from the decomp rather than guessed.

**Architecture:** `AtMath` holds the four formulas, Unity-free and unit-tested against the sheet's own worked numbers. `AtPanel` is the read-only view over the live slots. `AtHourPlanner` is deliberately NOT refactored onto `AtMath` — see the constraint below.

**Tech Stack:** C# net48 (game DLL) / net9.0 xUnit (tests), WinForms.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-10-advanced-training-design.md`. Read it first — it carries the decomp derivation and why each formula is what it is.
- **net48** for `NGUAdvisor.csproj`; never change `TargetFramework`.
- Build: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release` · Tests: `dotnet test tests/NGUAdvisor.Tests` — currently **199 passing**, keep them green.
- **DO NOT COMMIT.** No `git commit`, `git add`, `git push`, no PR.
- **Do NOT touch `AtHourPlanner.cs` beyond adding one pointer comment.** It carries private copies of two of these formulas and the right end state is for it to call `AtMath` — but it decides segment length, its own doc records that getting that wrong wasted whole rebirths, and no runtime verification is available. The duplication is deliberate and recorded; switching it over is a follow-up.
- **Advise-only.** Nothing may feed energy or change a level target.
- `AtMath.cs` must be **Unity-free** — no `Main.`, `Character`, `UnityEngine`. It is linked into the net9.0 test assembly.
- **Main-thread rule:** panel handlers must not call allocation or game-mutating code.
- **DPI:** every hand-placed pixel via `UiTheme.S(n)`; anything holding text sized from `SText`/`SHead`/`SCtl`/`SLines`; prose through `UiLayout.FitOrGrow`; widths from `UiLayout.BtnWidth`. Read `docs/modules/ui-infra.md` §DPI calibration first.
- **The old 70-line `UI AUDIT` baseline is INVALID** — `ApPanel` and `PpPanel` were added to the audit list after it was measured. It must be re-measured; never "expect zero".
- Match existing style (`Managers/` uses `var` and expression-bodied members).

## File Structure

| File | Responsibility |
|---|---|
| `NGUAdvisor/Managers/AtMath.cs` | **Create** — the four formulas, Unity-free |
| `tests/NGUAdvisor.Tests/AtMathTests.cs` | **Create** |
| `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj` | **Modify** — link `AtMath.cs` |
| `NGUAdvisor/Managers/AtHourPlanner.cs` | **Modify** — ONE pointer comment, nothing else |
| `NGUAdvisor/AtPanel.cs` | **Create** — the read-only view |
| Panel registration | **Modify** — as `PpPanel` was wired |
| `docs/modules/AtCalculator.md` | **Create** |

---

### Task 1: `AtMath` — the formulas (TDD)

**Files:**
- Create: `NGUAdvisor/Managers/AtMath.cs`
- Create: `tests/NGUAdvisor.Tests/AtMathTests.cs`
- Modify: `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `double AtMath.LevelAt(double l0, double r, double t)`
  - `double? AtMath.SecondsToLevel(double l0, double l1, double r)`
  - `double AtMath.StatMultiplier(double level)`
  - `double AtMath.BbCeiling(double m, double baseTime)`

**The game truth these encode** (`AdvancedTrainingController`, decompiled):

```
getDivisor()      = baseTime * (level + 1)
progressPerTick() = (energy / 50) * sqrt(totalEnergyPower()) * totalAdvancedTrainingSpeedBonus() / getDivisor()
```
A level lands when `barProgress >= 1`, and the game ticks 50/s. Define `M = (energy/50) * sqrt(epow) * atSpeedBonus`, so `progressPerTick = M / (baseTime * (L+1))` and `r = 50 * progressPerTick * (L+1) = 50 * M / baseTime` — a constant, which is why `dL/dt = r/(L+1)` integrates in closed form.

**A DISCREPANCY WITH THE SHEET THAT MUST NOT BE "FIXED" TOWARD THE SHEET.**
Blitz boost happens while `progressPerTick >= 1`, i.e. `M / (baseTime·(L+1)) >= 1`, i.e. `L <= M/baseTime − 1`. So the highest BB **level** is `M/baseTime − 1`. The sheet's "Highest BB level (full ecap)" cell computes `M/baseTime` — it solved for `L+1` and printed it as `L`, so it is **off by one**. Immaterial at the sheet's own scale (370 million), but the code is the authority: implement `− 1`. A test asserts the sheet's number and the doc records the off-by-one, so nobody later "corrects" the code to match the spreadsheet.

- [ ] **Step 1: Write the failing tests**

Create `tests/NGUAdvisor.Tests/AtMathTests.cs`:

```csharp
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class AtMathTests
    {
        [Fact]
        public void LevelAtZeroTimeIsTheStartingLevel()
        {
            Assert.Equal(1000, AtMath.LevelAt(1000, 500, 0), 6);
        }

        [Fact]
        public void LevelIsMonotoneInTime()
        {
            double a = AtMath.LevelAt(100, 250, 10);
            double b = AtMath.LevelAt(100, 250, 20);
            Assert.True(b > a);
        }

        [Fact]
        public void AZeroRateNeverAdvancesTheLevel()
        {
            Assert.Equal(750, AtMath.LevelAt(750, 0, 3600), 6);
        }

        [Fact]
        public void SecondsToLevelInvertsLevelAt()
        {
            const double l0 = 4_758_488, r = 12_345.678;
            double t = 9_000;
            double reached = AtMath.LevelAt(l0, r, t);
            double? back = AtMath.SecondsToLevel(l0, reached, r);
            Assert.NotNull(back);
            Assert.Equal(t, back.Value, 3);
        }

        [Fact]
        public void SecondsToLevelRefusesToAnswerRatherThanInventANumber()
        {
            Assert.Null(AtMath.SecondsToLevel(100, 200, 0));       // no rate
            Assert.Null(AtMath.SecondsToLevel(100, 200, -5));      // negative rate
            Assert.Null(AtMath.SecondsToLevel(100, 100, 10));      // already there
            Assert.Null(AtMath.SecondsToLevel(100, 50, 10));       // target behind us
            Assert.Null(AtMath.SecondsToLevel(100, 200, double.NaN));
            Assert.Null(AtMath.SecondsToLevel(100, 200, double.PositiveInfinity));
        }

        [Fact]
        public void StatMultiplierIsOneAtLevelZeroAndGrowsAsTheFourTenthsPower()
        {
            Assert.Equal(1.0, AtMath.StatMultiplier(0), 9);
            Assert.Equal(1.0, AtMath.StatMultiplier(-5), 9);          // guard, never below 1
            Assert.Equal(1 + 0.1 * System.Math.Pow(10000, 0.4), AtMath.StatMultiplier(10000), 9);
        }

        // The check that validates the whole derivation of the sheet's missing `atcalc`/`bb`.
        // The sheet's modifier is (ecap/1000)*sqrt(epow)*(1+gear); the game divides energy by 50, not
        // 1000, so M = 20 * modifier. With the P/T slots' baseTime of 1e7 this must land on the sheet's
        // own "Highest BB level (full ecap)" cell.
        [Fact]
        public void BbCeilingReproducesTheSheetsHighestBbLevel()
        {
            const double sheetModifier = 1.85202591774521e14;
            double m = 20 * sheetModifier;
            double ceiling = AtMath.BbCeiling(m, 1e7);
            // Sheet displays 370405183 (it solves for L+1 and prints it as L, so it reads one HIGHER
            // than the true highest BB level — see the plan. The code is the authority.)
            Assert.Equal(370405183.5 - 1.0, ceiling, 0);
        }

        // End-to-end against the sheet's displayed answer: both current and target sit BELOW the BB
        // ceiling, so every level costs one 0.02 s tick and the total is 0.02 * (target - current).
        [Fact]
        public void TheSheetsWorkedPowerCaseIsOneTickPerLevelBelowTheCeiling()
        {
            const double current = 4_758_488, target = 4_825_398;
            Assert.True(target < AtMath.BbCeiling(20 * 1.85202591774521e14, 1e7));
            Assert.Equal(1338.2, 0.02 * (target - current), 1);
        }
    }
}
```

- [ ] **Step 2: Link it into the test project**

In `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj`, add to the existing `<Compile Include=...>` group:

```xml
    <Compile Include="..\..\NGUAdvisor\Managers\AtMath.cs" Link="Linked\AtMath.cs" />
```

- [ ] **Step 3: Run and watch it fail for the right reason**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~AtMathTests"`
Expected: FAIL — compile error, `AtMath` does not exist.

- [ ] **Step 4: Implement**

```csharp
using System;

namespace NGUAdvisor.Managers
{
    // THE canonical Advanced Training level math for this codebase.
    //
    // Game truth, decompiled from AdvancedTrainingController:
    //   getDivisor()      = baseTime * (level + 1)
    //   progressPerTick() = (energy/50) * sqrt(totalEnergyPower()) * totalAdvancedTrainingSpeedBonus()
    //                       / getDivisor()
    // A level lands when barProgress >= 1 and the game ticks 50/s. With
    // M = (energy/50) * sqrt(epow) * atSpeedBonus the level speed is dL/dt = r/(L+1) for a CONSTANT
    // r = 50*M/baseTime, which is what makes the closed forms below exact rather than a simulation.
    //
    // This replaces three Apps Script functions in the community AT Calculator (atcalc, bb, bbtrue)
    // whose bodies are in no export of that sheet. They were derived here, then checked against the
    // sheet's own displayed numbers (see AtMathTests).
    //
    // NOTE: AtHourPlanner carries private copies of LevelAt and StatMultiplier. This file is the
    // canonical source; switching AtHourPlanner over is a deliberate follow-up, deferred because that
    // module decides segment length and no runtime verification is currently available.
    //
    // Unity-free on purpose: linked into tests/NGUAdvisor.Tests, which builds without an NGU install.
    public static class AtMath
    {
        // L(t) = sqrt((L0+1)^2 + 2rt) - 1
        public static double LevelAt(double l0, double r, double t)
        {
            if (r <= 0 || t <= 0) return l0;
            return Math.Sqrt((l0 + 1.0) * (l0 + 1.0) + 2.0 * r * t) - 1.0;
        }

        // The inverse — this is the sheet's `atcalc`. Null when there is no answer, never a fabricated
        // zero or infinity: a rendered number reads as a prediction.
        public static double? SecondsToLevel(double l0, double l1, double r)
        {
            if (double.IsNaN(r) || double.IsInfinity(r) || r <= 0) return null;
            if (double.IsNaN(l0) || double.IsNaN(l1)) return null;
            if (l1 <= l0) return null;
            return ((l1 + 1.0) * (l1 + 1.0) - (l0 + 1.0) * (l0 + 1.0)) / (2.0 * r);
        }

        // The AT slot's contribution to attack/defense: 1 + 0.1 * L^0.4 (slot 1 = attack, 0 = defense).
        public static double StatMultiplier(double level)
            => level <= 0 ? 1.0 : 1.0 + 0.1 * Math.Pow(level, 0.4);

        // Highest level still blitz-boosting: progressPerTick >= 1 <=> L <= M/baseTime - 1.
        // The community sheet's equivalent cell omits the -1 (it solves for L+1), so it reads one
        // higher. The decomp is the authority here; do not "correct" this toward the spreadsheet.
        public static double BbCeiling(double m, double baseTime)
        {
            if (baseTime <= 0 || m <= 0) return 0;
            return m / baseTime - 1.0;
        }
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~AtMathTests"` → PASS, 8 tests.

- [ ] **Step 6: Add the pointer comment to `AtHourPlanner`**

At `AtHourPlanner.cs`, next to the existing math comment block (~lines 23-26), add **one** comment
saying `AtMath` is now the canonical source of `LevelAt` and the `1 + 0.1·L^0.4` multiplier, that this
module keeps private copies deliberately, and that switching over is deferred until runtime
verification is available. **Change no code in that file.**

- [ ] **Step 7: Full suite + game build**

Run: `dotnet test tests/NGUAdvisor.Tests` → 207 passing (199 + 8).
Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release` → 0 warnings, 0 errors.

- [ ] **Step 8: Stop for review.** Do not commit.

---

### Task 2: `AtPanel` — the view

**Files:**
- Create: `NGUAdvisor/AtPanel.cs`
- Modify: the same registration sites `PpPanel` uses — read how `NGUAdvisor/PpPanel.cs` is wired into `SettingsForm.cs`, `Managers/Destinations.cs` and `Managers/SettingsIndex.cs` and follow it exactly, **including adding the panel to `SettingsForm`'s `UiLayout.Audit` list** (both earlier panels were missed there; do not repeat that).

**Interfaces:**
- Consumes: `AtMath` (Task 1).
- Produces: nothing downstream.

**Live reads.** Follow how `AtHourPlanner` already reads the slots (`AtHourPlanner.cs:264-290`) rather than inventing access: `character.advancedTraining.level[id]`, `.energy[id]`, `.levelTarget[id]`, `.barProgress[id]`, and per-slot `baseTime` from the slot's `AdvancedTrainingController`. Energy power is `character.totalEnergyPower()`; the AT speed bonus is `character.totalAdvancedTrainingSpeedBonus()`.

**Two gates that make any number meaningless — check them BEFORE showing an ETA:**

1. **AT does not progress at all** unless `character.training.attackTraining[4] >= 25000` **and**
   `character.training.defenseTraining[4] >= 25000` (`AdvancedTrainingController.updateAdvancedTraining`
   returns early otherwise). Say "AT locked" and show no times.
2. **`wishes[190].level >= 1` makes `progressPerTick()` return `1f` unconditionally** — a level every
   tick regardless of energy. In that state every level costs 0.02 s and the energy-based formulas do
   not apply. Say so and use the flat rate.

- [ ] **Step 1: Read the contract and the model panel**

Read `docs/modules/ui-infra.md` §DPI calibration and `NGUAdvisor/PpPanel.cs` (the most recent panel, same read-only shape, and the one whose registration you are copying).

- [ ] **Step 2: Build the panel**

Sections:

- Head `ADVANCED TRAINING`
- If either gate above is active, say so first and suppress the per-slot times.
- Per fed slot (energy > 0 or a target set): name, current level, `BbCeiling` at the energy currently
  assigned, and — when `levelTarget[id] > 0` — the ETA via `AtMath.SecondsToLevel`. A null ETA prints
  no duration; say `no rate` instead.
- **Cap to blitz-boost for a duration:** at 0.02 s/level, `T` seconds reaches `T/0.02` levels, and
  holding BB there needs `energy = 50 * baseTime * (L+1) / (sqrt(epow) * atSpeedBonus)`. Show it for a
  couple of fixed horizons (e.g. 1 h and 24 h) rather than adding an input box — this panel is a
  readout, and an input control would be the only editable thing on it.
- **TIME MACHINE** section, Normal only: `levels = T/0.02`, and cap needed
  `= levels * unitCost * 1000 / (0.02 * power)` for energy and magic separately, with `power` being
  `totalEnergyPower()` / `totalMagicPower()`. **The sheet's Evil column is marked broken by its own
  author** — do not port it, and say on the panel that only Normal is covered.
- Provenance line, verbatim:
  `Formulas: decompiled AdvancedTrainingController; layout after iboj88's AT Calculator.`

Read-only. Nothing feeds energy or writes a level target.

- [ ] **Step 3: Build**

Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release` → 0 warnings, 0 errors.
Run: `dotnet test tests/NGUAdvisor.Tests` → still 207.

`UI AUDIT` needs the game and is deferred; state in your report that it was NOT run and that the old 70-line baseline is already invalid.

- [ ] **Step 4: Stop for review.** Do not commit.

---

### Task 3: Documentation

**Files:**
- Create: `docs/modules/AtCalculator.md`
- Modify: the grouped-doc table in `CLAUDE.md`

- [ ] **Step 1: Write the doc**

In the voice of the existing module docs. It must record:
- The decomp derivation (`getDivisor`, `progressPerTick`, 50 ticks/s) and that it replaces three Apps
  Script functions absent from every export of the source sheet.
- **Why `baseTime` is read per slot and not hardcoded**, unlike the sheet (which bakes 1e7 for P/T and
  2e7 for Wandoos as its 500000/1000000 constants).
- **The off-by-one against the sheet's "Highest BB level" cell**, and that the decomp wins — so nobody
  "corrects" the code toward the spreadsheet later.
- The two gates: the 25 000 basic-training floor, and wish 190 forcing one level per tick.
- That the TM section is Normal-only because the sheet's author marked the Evil column broken.
- **The deliberate duplication with `AtHourPlanner`**, why it was not refactored (segment length; wasted
  rebirths; no runtime verification), and that it is a queued follow-up rather than an oversight.
- That nothing in this module has been run against the game.

- [ ] **Step 2: Stop for review.** Do not commit.

---

## Self-Review

**Spec coverage:** derivation → Task 1 · off-by-one → Task 1 + Task 3 · the two gates → Task 2 · per-slot `baseTime` → Tasks 2, 3 · cap-to-BB → Task 2 · TM Normal-only → Task 2, 3 · deliberate duplication → Task 1 Step 6 and Task 3 · testing → Task 1. Nothing unassigned.

**Placeholders:** none — every code step carries real code or names the exact file to copy from.

**Type consistency:** `LevelAt`, `SecondsToLevel`, `StatMultiplier`, `BbCeiling` are defined once in Task 1 and used under those names in Task 2. `M` is defined identically in the plan header, the code comment and the tests as `(energy/50)·sqrt(epow)·atSpeedBonus`.

**Known deferral:** every runtime check is deferred by owner decision; this plan produces code verified by build and unit tests only, and the `UI AUDIT` baseline is already invalid and needs re-measuring.
