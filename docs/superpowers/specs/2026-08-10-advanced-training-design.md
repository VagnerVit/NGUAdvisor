# Advanced Training calculator

Date: 2026-08-10
Status: draft for owner review

Ports the community AT Calculator (iboj88) into the advisor: how long a target AT level takes, where the
blitz-boost ceiling sits, and how much cap is needed to blitz-boost for a given time — plus the sheet's
Time Machine tab.

Advise-only. It never feeds energy and never changes a level target.

---

## The sheet's missing function, derived from the decomp instead of guessed

The sheet calls three Apps Script functions — `atcalc`, `bb`, `bbtrue` — whose bodies are not in any
export (both the current and the "Old Version" sheet call them; the old one shows `#NAME?`). They are
fully derivable from `AdvancedTrainingController`:

```csharp
getDivisor()      = baseTime * (level + 1)
progressPerTick() = (energy / 50) * sqrt(totalEnergyPower()) * totalAdvancedTrainingSpeedBonus() / getDivisor()
```

A level completes when `barProgress >= 1`, so it takes `1 / progressPerTick` ticks, and the game ticks
at 50/s (`AtHourPlanner` already encodes the ×50; the sheet encodes the same thing as 0.02 s).

Two regimes follow, and they are exactly the sheet's two branches:

| regime | condition | cost per level |
|---|---|---|
| **Blitz boost** | `progressPerTick >= 1` | one tick = **0.02 s** |
| **Normal** | above that | `0.02 * baseTime * (L+1) / M`, with `M = (energy/50) * sqrt(epow) * atSpeedBonus` |

Summed, the normal regime gives `t = 0.02 * baseTime * ((L1+1)² − (L0+1)²) / (2M)` — which is `atcalc`.

**BB ceiling:** `progressPerTick >= 1` ⇔ `L + 1 <= M / baseTime`.

### Why this beats the sheet

The sheet hardcodes `baseTime`: its P/T formulas use `500000` and its Wandoos formulas `1000000`, i.e.
`baseTime = 1e7` and `2e7`. But `baseTime` is a **serialized field on each `AdvancedTrainingController`
instance**, so the advisor reads the real per-slot value and cannot go stale when a slot is retuned.
Substituting the sheet's constants reproduces its numbers exactly (`0.02 * 1e7 / (2 * 20) = 5000`
against a modifier of `(ecap/1000)*sqrt(epow)*(1+gear)`), which is how this derivation was checked.

### One special case the sheet does not model

`progressPerTick()` returns **`1f` unconditionally when `wishes[190].level >= 1`** — a level every tick
regardless of energy. Any ETA must short-circuit to `0.02 s/level` in that state, or it will quote a
number the game will beat by orders of magnitude.

### Gates that make an ETA meaningless if ignored

`updateAdvancedTraining()` returns early unless `training.attackTraining[4] >= 25000` **and**
`training.defenseTraining[4] >= 25000`. Below that, AT does not progress at all, whatever the energy.
The module must say "AT is locked" rather than quote a time.

## Architecture

### `Managers/AtMath.cs` — Unity-free, linked into the test project

The four formulas, and the only place they live for this module:

```csharp
public static double LevelAt(double l0, double r, double t)          // sqrt((l0+1)^2 + 2rt) - 1
public static double? SecondsToLevel(double l0, double l1, double r) // null when r <= 0 or l1 <= l0
public static double StatMultiplier(double level)                    // 1 + 0.1 * level^0.4
public static double BbCeiling(double m, double baseTime)            // m / baseTime - 1
```

`SecondsToLevel` returns `null` rather than a number when it cannot answer — same rule as `PpEta`: a
rendered zero or infinity reads as a prediction.

### A deliberate, recorded duplication

`AtHourPlanner` already carries private copies of `LevelAt` and the `1 + 0.1·L^0.4` multiplier
(`AtHourPlanner.cs:23-26, 285-301`). The right end state is for it to call `AtMath`, and this repo
explicitly warns against re-deriving shared math locally.

**It is NOT being switched over in this work.** `AtHourPlanner` decides segment length, and its own doc
records that getting that wrong wasted whole rebirths; a behaviour-preserving refactor with no runtime
verification available is the wrong trade. `AtMath` gets a comment naming it canonical, `AtHourPlanner`
gets a pointer comment, and the switch is a follow-up for when game verification is back on. The
duplication is three formulas, written down, not forgotten.

### `AtPanel.cs` — the view

Per fed AT slot (`advancedTraining.level[]` / `energy[]` / `levelTarget[]`, ids as `AtHourPlanner`
already uses them): current level, the BB ceiling at the energy currently assigned, and — where a
`levelTarget` is set — the ETA to it.

Plus two calculators the sheet is actually used for:
- **Cap to BB for a duration:** levels reached in `T` seconds at 0.02 s/level is `T/0.02`; the energy
  needed to hold BB at that level is `energy = 50 * baseTime * (L+1) / (sqrt(epow) * atSpeedBonus)`.
- **Time Machine tab:** the sheet's TM math is `levels = T/0.02` and `cap = levels * unitCost * 1000 / (0.02 * power)`
  for energy and magic separately. **The sheet's own Evil column is marked broken by its author**
  ("The evil portion of this is broken :( ") — so the TM section ports the Normal column only, and says
  so, rather than shipping arithmetic its source disowns.

Read-only. No control feeds energy or sets a target.

## Testing

`AtMath` is Unity-free and unit-tested:
- `LevelAt(l0, r, 0) == l0`; monotone in `t`; `r == 0` → stays at `l0`.
- `SecondsToLevel` inverts `LevelAt` (round-trip within tolerance).
- `SecondsToLevel` → null for `r <= 0`, for `l1 <= l0`, for NaN/∞.
- `StatMultiplier(0) == 1`; a hand-checked value at a known level.
- `BbCeiling` reproduces the sheet's own "Highest BB level (full ecap)" cell, which is the check that
  validates the whole derivation. The sheet's modifier is `1.85202591774521e14`
  (`(ecap/1000)·sqrt(epow)·(1+gear)`), so `M = 20 × modifier = 3.70405183549e15`, and with
  `baseTime = 1e7` that gives `M / baseTime = 370405183.5` — the sheet displays `370405183`. Assert
  `BbCeiling(20 * 1.85202591774521e14, 1e7)` lands on `370405183.5` within a relative tolerance, and
  document the ×20 as `1000/50` (the sheet divides energy by 1000, the game by 50).
- One end-to-end case transcribed from the sheet: current 4 758 488, target 4 825 398, both below the
  ceiling → 1338.2 s, matching the sheet's displayed value.

`AtPanel` is Unity-dependent: build only. `UI AUDIT` is deferred; note that the previous 70-line
baseline is invalid now that `ApPanel` and `PpPanel` are audited, and must be re-measured.

## Out of scope

Feeding energy, changing level targets, the TM Evil column (broken at the source), and switching
`AtHourPlanner` onto `AtMath`.
