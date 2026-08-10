# AtHourPlanner (`Managers/AtHourPlanner.cs`)

AT HOUR extension: near the segment's stock end (2 h into the run), forecast where AT
Power/Toughness land if feeding continues, and extend the segment when the projection crosses the
next titan kill-ladder stage or makes a new zone idle-farmable. Hard cap `MaxEnd = 18000` s (4 h
of AT = the 5 h mark). Decided ONCE per run in the window [1 h 55 m, 2 h 05 m] — the segment
engine is time-anchored by law: bounded windows, no re-litigation. Both outcomes are logged via
`ChallengeOverlay.Record` so the feed always says what was weighed.

## Persistence is load-bearing

Decision persisted in `Settings.AtHourPlannedEnd/AtHourDecidedRunSec`. Statics don't survive an
advisor reload, and without persistence the past-the-boundary branch forced `NormalEnd` — which
silently CANCELLED an in-flight extension, snapping AT HOUR to MARATHON mid-segment. That
mattered because AT HOUR is AT's ONLY feeding window (the marathon's CAPALLAT sits behind surplus
absorbers — fixed 2026-07-16 in ChallengeOverlay), so a cancelled extension meant AT never fed
again that run. A rebirth/quickload (runSec < decidedRunSec) re-arms the decision. Fresh advisor
past the boundary with no persisted decision → keep stock shape (never surprise-flip
RECOVERY/MARATHON back into AT HOUR).

## Forecast math (decomp `AdvancedTrainingController`)

- Level speed `dL/dt = R/(L+1)` → closed form `L(t) = sqrt((L0+1)² + 2Rt) − 1`; `levelTarget`
  caps it; target −1 = paused (R := 0).
- **The closed form alone over-projects — the projection goes through `AtMath.LevelAtCapped`.**
- AT stat multiplier: `1 + 0.1 × L^0.4` (slot 1 = attack, slot 0 = defense) → projected stat =
  reference × ratio of multipliers.
- `Solve` scans in 60 s steps (ratios monotone, first hit wins).

### The uncapped projection was a bug (fixed 2026-08-10)

`LevelAt` used the bare closed form. Game truth, `updateAdvancedTraining`:

```csharp
barProgress[id] += progressPerTick();
if (barProgress[id] >= 1f) { barProgress[id] = 0f; if (canLevel()) level++; }
```

The bar is set to **zero**, not decremented — the overflow is **discarded**. A slot with
`progressPerTick >= 1` therefore gains exactly one level per tick however much energy it holds, and
the closed form promises growth the game cannot deliver. Magnitude over this planner's own 2–4 h
window: `ppt = 2` → +6 %/+10 %; `ppt = 10` → +47 %/+74 %; `ppt = 78` → +233 %/+331 %. The last is
the realistic one — a live level of 4.76M against a 370M blitz ceiling implies `ppt ≈ 78`.

This module EXTENDS the run's AT segment. Over-projection made it extend chasing titan stages and
zone thresholds the run would never reach — the same class of loss this doc records above.

`LevelAt` now calls `AtMath.LevelAtCapped(L0, Ppt, t, TickSeconds)` (`TickSeconds = 0.02`, the
game's 50 Hz tick), the forward twin of `AtMath.SecondsToTarget` and piecewise off the same
`ceiling = ppt·(L0+1) − 1`. `ReadSlot` stores `Ppt` alongside `R`; the `Cap` clamp and the
`Cap == -1` pause are untouched.

**The safety property, and it is the whole review argument: when `ppt <= 1` the ceiling sits at or
below `L0`, the first branch is taken, and the result is the SAME arithmetic as before.** Only
blitz-boosting slots move, which is exactly the bug.
`AtMathTests.LevelAtCappedEqualsTheUncappedClosedFormWhenNotBlitzBoosting` pins that equivalence
across a grid of `ppt <= 1` / level / time cases, and
`LevelAtCappedIsStrictlyLowerThanTheUncappedFormWhenBlitzBoosting` pins the other half. Do not
weaken either — together they are the reason this edit to a segment-length decider was reviewable
without runtime verification.

## Reference stats — two attack references (user decision + a real 1.5× bug)

Live P/T projected onto the optimizer's best Power/Toughness gear
(`OptimizationAdvisor.ProjectedBestGear`) — AT HOUR wears AT-speed gear; thresholds are met in
the KILL loadout. Then:

- **Titan ladder**: `refAtk = totalAdvAttack() × atkMult` — beast mode INCLUDED (the guide tables
  and the game's AK gate both compare raw totalAdvAttack).
- **Zone tables**: `zoneAtk = refAtk / beastModeBonus()` — ZoneStatHelper divides beast out.

One shared reference understated attack ~1.5× against the titan ladder and extended the segment
chasing stages the kill loadout already cleared.

Both live in **one private `References(c, out refAtk, out refDef, out zoneAtk)`** (extracted
2026-08-10, behaviour-identical — same reads in the same order; `Decide` still words its own
`Rec("no usable reference stats")`). It was extracted rather than copied for `GoalLevels` below
precisely because a second private copy of this derivation is the ~1.5× bug waiting to happen again.

## `GoalLevels` — the read-only accessor (decides nothing)

```csharp
public static bool GoalLevels(Character c, out double atkLevel, out double defLevel, out string label)
public const string GoalMetLabel = "already met";
```

"Up to which AT level does more AT still buy progress?" — the level at which the next objective's
staged requirement is met, past which AT only makes the number bigger. Same rule `LevelPlanner`
freezes P/T on, expressed as a level, for `AtPanel`'s GOAL row.

Needs come from the requirement this module already reads —
`OptimizationAdvisor.StagedRequirementFor(obj.Index, obj.Version, refAtk, refDef, …)` — as
`reqA/refAtk` and `reqD/refDef`; the level is
`AtMath.LevelForMultiplier(need · AtMath.StatMultiplier(currentLevel))` (slot 1 = attack,
0 = defense). `label` is `"{titan} {stage} stats"`, the module's own log naming.

Return contract, so the view never prints a number it cannot stand behind:

| situation | returns | `label` | levels |
|---|---|---|---|
| a need > 1 | `true` | `"T3 v1 idle stats"` | the threshold level; **NaN** for a slot whose own need is met |
| both needs ≤ 1 | `false` | `GoalMetLabel` | NaN |
| no objective / unreadable requirement / `NaN` need / unreadable levels / throw | `false` | `null` | NaN |

`GoalMetLabel` is a shared constant so the view distinguishes "already met" from "cannot determine"
without string-matching this module's prose. **It touches none of the decision machinery** — no
`EndSec`, `Decide`, persistence, window, `MaxEnd`, buffer or `Solve` — and it writes nothing. Note it
goes through `OptimizationAdvisor.ProjectedBestGear`, whose two optimizer runs are cached for 120 s;
the ≤1/s panel refresh therefore costs an optimizer pass twice a minute at worst, the same cost this
planner already pays.

## Decision details

- Regen-gated titan stages are declared `blocked` — AT can't raise regen.
- Zone candidates: only the NEXT unreachable zone (higher ones follow on later runs); thresholds
  ×1.0001 mirror FightType's strict `>` (need exactly 1.0 would "solve" at t=0).
- Winner gets a 10 % schedule buffer, clamped to [NormalEnd, MaxEnd] — never earlier than the
  stock boundary (the extension must not CUT the hour it lengthens) — and never past the run's
  scheduled rebirth.
- No-extension paths report the honest near-miss: "X needs +N %; th more AT projects +P %/+T %".
- Requires `AutoProfile` (it owns AT feeding; an extended segment would otherwise allocate
  nothing).
