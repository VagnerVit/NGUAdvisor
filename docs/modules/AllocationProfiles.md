# AllocationProfiles (`AllocationProfiles/`)

The profile execution engine: parses a profile's `Breakpoints` and applies them every allocation
pass. The user-facing grammar is documented in README.md (§Allocation); this doc covers the
runtime.

```
CustomAllocation            — owns the file, the reload, and DoAllocations()
  BreakpointWrapper         — all systems' breakpoint sets + the startup summary string
  Breakpoints/
    BaseBreakpoints<T>      — generic time/challenge selection + swap gating
    Energy|Magic|R3Breakpoints        → ResourceBreakpoints/* (the token engine)
    Gear|Digger|Beard|Wandoos|NGUDiff|ConsumablesBreakpoints
  RebirthStuff/
    BaseRebirth + Time|Number|BossNum|Muffin|MoneyPitRunRebirth
```

## Breakpoint selection (`BaseBreakpoints<T>`)

Breakpoints are sorted DESCENDING by time, so the first whose time has passed is the latest
applicable one. `Time` accepts a plain number or `{h,m,s}` (`ParseTime`).

**Challenge-aware selection**: while a challenge is active, a breakpoint tagged with that challenge
code wins; otherwise (or if none matches) untagged breakpoints — the normal timeline — are used.
`Swap()` re-fires when the SELECTED breakpoint changes **or the active challenge changes**
(`currentChallenge` is tracked for exactly that), and `PerformSwap` returning false leaves
`swapped` false so the swap is retried next pass (this is what lets a swap that failed on
0 gold/locked button succeed later).

**Rebirth watermark** (`CustomAllocation`): when `rebirthTime` jumps backwards a rebirth happened
(ours or manual), and every breakpoint set is `Reset()` — sets only re-fire on a CHANGE, so a
single time-0 breakpoint would never re-apply after rebirth (user-reported: diggers stayed off).

`ReloadAllocation` writes a template profile when the file is missing, and on a parse error keeps
an EMPTY wrapper + "Resave to reload" (never a half-parsed profile).

## Resource token engine (`ResourceBreakpoints/`)

One class per token family: `NGUBP`, `AdvancedTrainingBP`, `AugmentBP`, `BestAug`, `BasicTrainingBP`,
`TimeMachineBP`, `WandoosBP`, `RitualBP`, `BR`, `HackBP`, `CapCalc`.

Contract (`ResourceBreakpoint`):
- `IsValid()` = `CorrectResourceType() && Unlocked() && !TargetMet()` — an invalid priority DROPS
  OUT and its share redistributes (this is how LevelPlanner's target caps steer allocation, and how
  ChallengeOverlay's stripping works: e.g. `BestAug` refuses NOAUG, `TimeMachineBP` refuses NOTM,
  `NGUBP` dies with the disabled NGU button).
- `UpdateMaxAllocation(prioCount)` — non-cap tokens take `idle / prioCount`; **CAP tokens take
  `min(need, idle-at-their-turn)`**, so they stay OUT of the equal-share divisor. Allocation walks
  the list IN ORDER recomputing idle/prioCount per non-cap token, which is why ChallengeOverlay
  emits surplus NGU lanes as `CAPNGU-*` (see ChallengeOverlay.md).
- `:pct` suffix caps a priority at a percentage (of cap for CAP tokens, of idle otherwise).
- `CapCalc` is the shared "highest BB breakpoint reachable in the next 10 s" solver.

`ParseBreakpointArray` deliberately takes **no rebirthTime parameter** — the rebirth deadline is a
property of the RUN, not of a parsed breakpoint.

## Non-resource systems

- **GearBreakpoints** — either a static `ID` list or an `Objective` (+`ForceRespawn`); exposes
  `ActiveObjective`/`ActiveForceRespawn`, which `AdvisorApply.ApplyGearRefresh` and
  `AdvisorApply`'s optimize call read. `ActiveProfileDiggers()` on DiggerBreakpoints is the Hybrid
  pool `OptimizationAdvisor.CurrentDiggerSet` consults.
- **ConsumablesBreakpoints** — token list + `:count`, executed by `ConsumablesManager`.
- **WandoosBreakpoints / NGUDiffBreakpoints** — OS and NGU track (note: `LevelPlanner` may own the
  NGU track instead in the Ch.5 24 h shape — see LevelPlanner.md).

## Rebirth (`RebirthStuff/`)

`BaseRebirth` decides when to rebirth and into which challenge (`RCTarget` holds the challenge-code
vocabulary — BASIC/NOAUG/24HR/100LC/NOEC/TC/NORB/LSC/BLIND/NONGU/NOTM, the same codes
`ChallengeDetector` returns). Types: `TimeRebirth`, `NumberRebirth` (OldNumber × Target),
`BossNumRebirth` (N more bosses), `MuffinRebirth` (+ TimeBalancedMuffin — the 24 h/muffin cycle with
all its skip conditions from README), `MoneyPitRunRebirth`.

Two things other modules depend on: `RebirthAvailable()` requires `LockManager.CanSwap()` — **a
stranded mode lock means the run cannot end** (the invariant LockManager/MoneyPitManager/
YggdrasilManager all defend), and `CastBloodSpellsForRebirth()` force-casts pooled blood before the
reset (blood is wiped), which is why BloodPlanner can route to NUMBER without fear of losing it.
`NextRebirthTargetSeconds()` is the run-deadline read used by WandoosAdvisor, BloodPlanner and
AtHourPlanner.
