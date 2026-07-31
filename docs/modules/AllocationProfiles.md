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

`BestAug`'s ranking model — boost formula, the `e_i : 2` energy split, the gold ceiling and why the
horizon must track the augment phase rather than a fixed hour — is derived in `docs/AUGMENTS.md`.
`EnergyBreakpoints.AugmentPhaseSecondsLeft()` supplies that horizon: the earliest later breakpoint on
the active (challenge-aware) timeline whose priorities contain no `AugmentBP`.

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

**Basic Training ignores the game's `syncTraining` toggle.** `ALLBT`/`CAPALLBT` always expands to all
12 slots and `BasicTrainingBP` calls the `addEnergy(long)` overload, which does not mirror. The
parameterless `addEnergy()` would honour sync: it clamps the input to `idleEnergy / 2` and copies the
amount into `allDefenseController.trains[id]`. Since `training.attackCaps[i]` and `defenseCaps[i]`
drift apart as levels reduce them independently, the mirrored amount is simply wrong for the
receiving slot — user-reported: Charge (cap 46) received the 21 that Piercing Attack needed, and the
defense tree stayed permanently under-fed. Expanding to 6 slots under sync was the old behaviour.

**`WAN`/`CAPWAN` is a leftovers BLACK HOLE, not a leftovers sink — never park it in a long-run
profile.** Three properties compound. (1) `WandoosBP.TargetMet()` is hardcoded `false`, so the lane
never drops out and never redistributes its share. (2) `num = ceil(baseEnergyTime /
totalWandoosEnergySpeed)` is the allocation that reaches the game's 1-level-per-tick cap; whenever
that exceeds the token's ceiling, `ceil(num / MaxAllocation)` makes the lane request its ENTIRE
ceiling every pass. That is the normal case, not the edge case: any Evil+ run (`baseTime` >= 1e21)
and every Normal character whose cap is below `baseTime / speed`. An uncapped `CAPWAN` then means
"take all idle energy", and a trailing `WAN` means "take everything the CAP tokens left". (3) The
payoff is the narrowest in the game — Wandoos multiplies **Fight Boss** A/D only (never adventure
stats, so it cannot help a titan kill) and its dump levels are **wiped at rebirth**.

Measured on a ch.3 Normal save (cap 5 571 250, `totalWandoosEnergySpeed` ~3.0, so BB would need
3.3e8 = 59x cap): `Normal-LRB`'s trailing `WAN` held **62.5 % of the cap for the whole 3 h 55 m run**
and returned 6 449 levels = 28.4x A/D. Boss requirements in that range grow ~10x per boss
(`bossAttack` 1.98e72 at boss 74 vs 1.98e77 at boss 79), so the whole run's Wandoos investment was
worth **~1.45 bosses** — against ~6.2 bosses from that single rebirth's Number multiplier — and it
died at the rebirth, while every NGU sat at the level it had held for 507 h of playtime.

Two rules follow. Because the bonus is `L^0.8` of the levels standing at the END of the run and
leveling is linear in allocation, only total end-of-run levels matter: Wandoos belongs in a LATE
breakpoint, never in the bootup hour (0 -> 100 % speed over the first hour, so those levels cost the
most). And the direct dump earns a lane only where the guide names it — NoAug/NoTM/NoRB challenges,
CBlock4, the final Sadistic LRB — not as the default resting place for spare E/M. `Normal-LRB` was
fixed accordingly (AT first, NGU tail, no Wandoos); `CBlock2-Normal` still leads with `CAPWAN:50` by
design, because a NoTM/NoAug-flavoured block is exactly the case where Wandoos IS the power source.

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
