# PP Advisor Panel — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the perk-point plan that `SpendPlanner` already computes, plus the one thing it does not: how long until the next perk is affordable at the pace actually being earned.

**Architecture:** No new manager — there is no decision here, only a readout, and panels may read managers directly. One pure Unity-free helper (`PpEta`) holds the arithmetic so it is unit-testable and cannot drift; `PpPanel` is the view over `SpendPlanner`, `GrowthTracker` and `ItopodFarmAdvisor`.

**Tech Stack:** C# net48 (game DLL) / net9.0 xUnit (tests), WinForms.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-10-pp-advisor-design.md`. Read it first.
- **net48** for `NGUAdvisor.csproj`; never change `TargetFramework`.
- Build: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release` · Tests: `dotnet test tests/NGUAdvisor.Tests` — currently **194 passing**, keep them green.
- **DO NOT COMMIT.** No `git commit`, `git add`, `git push`, no PR. The owner forbids it.
- **`SpendPlanner` is NOT modified.** This module reads it. Same for `GrowthTracker` and `ItopodFarmAdvisor`.
- `PpEta.cs` must be **Unity-free** — no `Main.`, `Character`, `UnityEngine`. It is linked into the net9.0 test assembly.
- **Read-only.** No control may buy a perk. The auto-buy toggle already exists in `AdvisorApply` and is not duplicated here.
- **Main-thread rule:** panel handlers must not call allocation or game-mutating code.
- **DPI:** every hand-placed pixel via `UiTheme.S(n)`; anything holding text sized from `SText`/`SHead`/`SCtl`/`SLines`; prose through `UiLayout.FitOrGrow`; button widths from `UiLayout.BtnWidth`. Read `docs/modules/ui-infra.md` §DPI calibration before placing a control.
- **`UI AUDIT` baseline is 70 lines, not zero** (measured 2026-08-10). Any future check is before/after.
- Match existing style (`Managers/` uses `var` and expression-bodied members).

## File Structure

| File | Responsibility |
|---|---|
| `NGUAdvisor/Managers/PpEta.cs` | **Create** — the ETA arithmetic, Unity-free |
| `tests/NGUAdvisor.Tests/PpEtaTests.cs` | **Create** |
| `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj` | **Modify** — link `PpEta.cs` |
| `NGUAdvisor/PpPanel.cs` | **Create** — the view |
| Panel registration | **Modify** — wire it the way `ApPanel` was wired (see Task 2) |
| `docs/modules/PpAdvisor.md` | **Create** |

---

### Task 1: `PpEta` — the arithmetic (TDD)

**Files:**
- Create: `NGUAdvisor/Managers/PpEta.cs`
- Create: `tests/NGUAdvisor.Tests/PpEtaTests.cs`
- Modify: `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: `static double? PpEta.HoursTo(long cost, long banked, double perHour)`.

**The contract, and why each branch exists:** a missing answer must be *absent*, never a fake number.
An unknown rate rendered as `0h` or `∞` is a wrong answer wearing the right label — the spec forbids it.

- `banked >= cost` → `null` (already affordable; the caller says "AFFORDABLE NOW", not "0h")
- `perHour <= 0` → `null` (no rate, so no estimate)
- `double.NaN` / `double.IsInfinity(perHour)` → `null`
- otherwise → `(cost - banked) / perHour`

- [ ] **Step 1: Write the failing tests**

Create `tests/NGUAdvisor.Tests/PpEtaTests.cs`:

```csharp
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class PpEtaTests
    {
        [Fact]
        public void AlreadyAffordableYieldsNoEstimate()
        {
            Assert.Null(PpEta.HoursTo(cost: 100, banked: 100, perHour: 50));
            Assert.Null(PpEta.HoursTo(cost: 100, banked: 250, perHour: 50));
        }

        [Fact]
        public void NoRateYieldsNoEstimateRatherThanInfinity()
        {
            Assert.Null(PpEta.HoursTo(cost: 100, banked: 0, perHour: 0));
            Assert.Null(PpEta.HoursTo(cost: 100, banked: 0, perHour: -5));
        }

        [Fact]
        public void NonFiniteRateYieldsNoEstimate()
        {
            Assert.Null(PpEta.HoursTo(100, 0, double.NaN));
            Assert.Null(PpEta.HoursTo(100, 0, double.PositiveInfinity));
        }

        [Fact]
        public void NormalCaseDividesTheShortfallByTheRate()
        {
            var h = PpEta.HoursTo(cost: 2_500_000, banked: 1_230_000, perHour: 380_000);
            Assert.NotNull(h);
            Assert.Equal(3.342, h.Value, 3);   // 1_270_000 / 380_000
        }

        [Fact]
        public void AVeryLargeShortfallStaysFiniteAndPositive()
        {
            var h = PpEta.HoursTo(long.MaxValue / 2, 0, 1.0);
            Assert.NotNull(h);
            Assert.True(h.Value > 0 && !double.IsInfinity(h.Value));
        }
    }
}
```

- [ ] **Step 2: Link it into the test project**

In `tests/NGUAdvisor.Tests/NGUAdvisor.Tests.csproj`, add to the existing `<Compile Include=...>` group:

```xml
    <Compile Include="..\..\NGUAdvisor\Managers\PpEta.cs" Link="Linked\PpEta.cs" />
```

- [ ] **Step 3: Run and watch it fail for the right reason**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~PpEtaTests"`
Expected: FAIL — compile error, `PpEta` does not exist.

- [ ] **Step 4: Implement**

```csharp
using System;

namespace NGUAdvisor.Managers
{
    // The ONE place a "when can I afford the next perk" estimate is computed.
    //
    // It exists as its own Unity-free file for two reasons: it is the only arithmetic in the PP
    // module, so isolating it makes it unit-testable without an NGU install; and a second copy in the
    // panel would be free to drift from this one.
    //
    // Every "no answer" case returns null rather than a number. A rendered 0h or an infinity reads as
    // a real prediction, and the module's whole value is that its numbers can be trusted.
    public static class PpEta
    {
        public static double? HoursTo(long cost, long banked, double perHour)
        {
            if (banked >= cost) return null;                                   // already affordable
            if (double.IsNaN(perHour) || double.IsInfinity(perHour)) return null;
            if (perHour <= 0) return null;                                     // no rate -> no estimate
            return (cost - banked) / perHour;
        }
    }
}
```

`cost - banked` is computed in `long` and only then divided as `double`, so a large shortfall keeps
full precision instead of losing it to a float multiply.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/NGUAdvisor.Tests --filter "FullyQualifiedName~PpEtaTests"` → PASS, 5 tests.

- [ ] **Step 6: Full suite + game build**

Run: `dotnet test tests/NGUAdvisor.Tests` → 199 passing (194 + 5).
Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release` → 0 warnings, 0 errors.

- [ ] **Step 7: Stop for review.** Do not commit.

---

### Task 2: `PpPanel` — the view

**Files:**
- Create: `NGUAdvisor/PpPanel.cs`
- Modify: the same registration sites `ApPanel` uses — read how `NGUAdvisor/ApPanel.cs` is wired into `SettingsForm.cs`, `Managers/Destinations.cs` and `Managers/SettingsIndex.cs`, and follow it exactly. `ApPanel` was registered as a `SettingsIndex` **Reference**, not as a System, because it owns no setting and no automation; this panel is the same shape, so make the same choice.

**Interfaces:**
- Consumes: `PpEta.HoursTo` (Task 1); `SpendPlanner.NextPerk()`, `SpendPlanner.NextPerkPlanned()`; `GrowthTracker.Rate`; `ItopodFarmAdvisor.ForMode`.
- Produces: nothing consumed downstream.

**The reads, with their exact shapes — do not go hunting for alternatives:**

- Banked PP: `Main.Character.adventure.itopod.perkPoints`.
- Next buy: `SpendPlanner.NextPerk()` → `Buy { bool Known; int Id; string Name; long CurLevel; long TargetLevel; long Cost; bool Affordable; }`.
- Queued-but-gated buy: `SpendPlanner.NextPerkPlanned()` → `PlannedBuy { bool Known; string Name; long Cost; int MinChapter; bool DifficultyGated; }`.
- **Measured** PP/hr: `GrowthTracker.Rate(s => s.GPp, win, false, out double r)` returning `bool`.
  `GrowthPanel.cs:167` already does exactly this — **read that line and reuse its window convention**
  rather than inventing a window. `GPp` is cumulative *gains*, so spending PP cannot depress the rate
  (`GrowthTracker.cs:8`, a standing user rule) — do not switch it to the raw `Pp` balance.
- **Modelled** PP/hr: `ItopodFarmAdvisor.ForMode(mode).PpPerSecond * 3600`, with `DefaultFloor`,
  `PeakFloor` and `CombatMode` from the same struct for the label.

**Three rules that are the point of this panel:**

1. **Measured is the headline; modelled is a separate, labelled line.** They answer different questions
   ("at my pace" vs "if I farmed the pod"). Never blend them into one number.
2. **When there is no measured rate** (`Rate` returns false — normal shortly after a load, since
   `GrowthTracker` samples only since load) fall back to the modelled figure **and say which one is in
   use**. A modelled ETA presented as measured is a wrong answer wearing the right label.
3. **`NextPerk().Known == false` does NOT mean "plan complete".** Only say that when
   `NextPerkPlanned().Known` is also false. Otherwise report what the banked PP is *for*
   ("Queued: <name> · needs chapter N"). Collapsing these two was a real user-reported bug — see
   `docs/modules/SpendPlanner.md`.

- [ ] **Step 1: Read the contract and a model panel**

Read `docs/modules/ui-infra.md` §DPI calibration, `docs/modules/ui-panels.md`, and `NGUAdvisor/ApPanel.cs` (the most recently built panel, same read-only shape). Follow `ApPanel`'s structure and registration.

- [ ] **Step 2: Build the panel**

Content, top to bottom:
- Head `PERK POINTS`
- `Banked: <Abbrev(perkPoints)> PP`
- Next buy: name, `CurLevel -> TargetLevel`, cost. If `Affordable`, say so plainly. Otherwise show the
  shortfall and the ETA from `PpEta.HoursTo`, with the rate and which rate it is:
  `short 1.27M · ~3h 20m at 380K PP/hr (measured)`.
- If `PpEta.HoursTo` returns null and the item is not affordable, show **no duration at all** — say
  `no rate yet` instead of a number.
- Queued-but-gated line from `NextPerkPlanned()` when it is known.
- Modelled line: `ITOPOD would pay <Abbrev> PP/hr (<mode name>, floors N-M)`.
- Provenance line, verbatim: `Order: community guide perk plan (docs/NGU-KNOWLEDGE.md).`

Numbers through `NumberFormatter.Abbrev`. **There is no shared duration formatter in this codebase**
(the only one is private to `ProfileValidator`) — add a small `private static string` helper inside
`PpPanel` rather than creating a public utility for a single caller. Combat-mode names come from
`BoostFarmAdvisor.ModeName(int)`, which `AdventurePanel` already uses; do not hardcode a name array.

**Do not duplicate `GrowthPanel`.** It already shows PP rate chips. This panel's reason to exist is the
ETA to the next perk; keep the rate lines to the minimum that makes the ETA legible.

- [ ] **Step 2b: The one action this panel carries — "Farm ITOPOD for PP"**

Owner request (2026-08-10): the panel says what the pod would pay, so it should also be able to go do
it, rather than sending the user to Combat → ADVENTURE → ITOPOD to flip the same switches.

This is the **only** control on the panel. It is a routing preference, not a purchase — it changes
nothing that cannot be changed back, and it never spends PP.

A toggle (reflecting current state, not a fire-and-forget button) that sets:
- `Settings.AdventureTargetITOPOD = true`
- `Settings.ITOPODOptimizeMode = 2` (the `PP` entry of `AdventurePanel`'s Optimize list
  `{ Disabled, Default, PP, EXP/AP }` — read that list rather than hardcoding the literal if a named
  constant exists)

and turning it off restores `AdventureTargetITOPOD = false`. Do **not** touch `ITOPODCombatMode`
here — the pod reads it as a boolean and `AdventurePanel` owns that choice.

**Three preconditions the label must disclose, verified from the routing code — the panel must not
promise an effect it cannot deliver:**

1. **`Settings.CombatEnabled` gates everything.** `Main.cs:1386` returns before any routing when it is
   off. If it is off, say so on the toggle rather than silently doing nothing.
2. **Gear Hunt outranks ITOPOD targeting** — `Main.cs:1391`: `GearHunter.Active && GearHunter.ZoneReachable()`
   wins. That precedence is the fix for a user-reported bug (Target ITOPOD silently overrode the hunted
   stage), so it must not be worked around. While a hunt is active, say the setting will take effect
   once the hunt ends.
3. **It overrides the advisor's zone choice.** With `AdvisorZones` on, `ApplyZones` keeps setting
   `Settings.SnipeZone`, but `Main.cs:1393` prefers the ITOPOD flag — so while this toggle is on, the
   advisor's farm routing is bypassed. Say that on the panel; a user who forgets it will wonder why
   gear/boost farming stopped happening.

**Main-thread rule:** writing `Settings` from a WinForms handler is fine and is what the sibling panels
do. Do NOT call allocation or routing code directly from the handler — the next `Main.Update()` pass
picks the change up.

Reflect live state through the panel's existing sync path, so flipping the same setting in
`AdventurePanel` updates this toggle and vice versa. They are the same `Settings` property, so there is
one source of truth; do not cache a local copy.

Keep `SettingsIndex` parity: this panel now surfaces `AdventureTargetITOPOD` and `ITOPODOptimizeMode`,
which `AdventurePanel` also registers. Follow whatever the catalogue's convention is for a setting
reachable from two places — read `SettingsIndex.cs:248` and the audit in `BasicSettingsPanel` before
choosing, and if the audit would report a duplicate, prefer leaving registration with `AdventurePanel`
(the owner) and not re-registering here.

- [ ] **Step 3: Build**

Run: `dotnet build "NGUAdvisor/NGUAdvisor.csproj" -c Release` → 0 warnings, 0 errors.
Run: `dotnet test tests/NGUAdvisor.Tests` → still 199.

The `UI AUDIT` check needs the running game and is deferred by owner decision — state in your report that it was NOT run, and that the comparison baseline is **70 lines, not zero**.

- [ ] **Step 4: Stop for review.** Do not commit.

---

### Task 3: Documentation

**Files:**
- Create: `docs/modules/PpAdvisor.md`
- Modify: the grouped-doc table in `CLAUDE.md` (one row, as `ApPurchaseAdvisor.md` did)

- [ ] **Step 1: Write the doc**

In the voice of the existing module docs (dense, file:line citations, why over what). It must record:
- That this module **computes no plan** — `SpendPlanner` owns the perk order — and why no `PpAdvisor`
  manager exists: there is no decision, only a readout, so a forwarding layer would have no job.
- **The two rates and the rule that they never blend**, plus the fallback and its labelling requirement.
- Why the rate reads `GrowthTracker.GPp` (gains) and not the `Pp` balance: spending must not depress
  the rate (`GrowthTracker.cs:8`).
- That `NextPerk().Known == false` is not "plan complete", and the user-reported bug behind that rule.
- That every "no answer" case in `PpEta` returns null on purpose, and what a rendered 0h/∞ would imply.
- That the panel is a `SettingsIndex` Reference rather than a System, and why.
- That nothing in this module has been run against the game — every runtime check is deferred.

- [ ] **Step 2: Stop for review.** Do not commit.

---

## Self-Review

**Spec coverage:** scope correction → plan goal · inputs table → Task 2 Step 2 · two-rates rule → Task 2 rules 1–2 and Task 3 · `NextPerkPlanned` trap → Task 2 rule 3 · panel layout → Task 2 Step 2 · `PpEta` null contract → Task 1 · architecture (no new manager) → plan header and Task 3 · testing → Tasks 1 and 2. Nothing unassigned.

**Placeholders:** none — every code step carries real code or names the exact file to copy the pattern from.

**Type consistency:** `PpEta.HoursTo(long, long, double) → double?` defined in Task 1, consumed under that name in Task 2. `Buy` and `PlannedBuy` field names are quoted from `SpendPlanner` as it exists; `GrowthTracker.Rate`'s signature is quoted from the call already at `GrowthPanel.cs:167`.

**Known deferral:** every runtime check (panel audit, live balance, real rates) is deferred by owner decision. This plan produces code verified by build and unit tests only.
