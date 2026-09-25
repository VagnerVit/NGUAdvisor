# ITOPODManager + ItopodConstants (`Managers/ITOPODManager.cs`, `ItopodConstants.cs`)

ITOPOD (zone 1000) is handled entirely here — `CombatManager.DoZone` explicitly defers.
Two modes: **Farm** (sit on the optimal one-shot floor) and **Push** (climb
`itopodStart..itopodEnd`, full CombatAI mode 2). `UpdateMaxFloor()` (periodic) decides the mode;
`Update()` (per tick) runs zone check + quick actions.

## Game-truth floor math (`ItopodConstants`)

Every ITOPOD spawn is the SAME mob — `Enemy(name, AR 1.2, atk 10, def 10, regen 1, hp 600)` from
`createEnemyTable()` — scaled by `powerUp(e, L)`: each stat `× 1.05^L`, then `× Random.Range(0.98, 1.02)`.

`PlayerController` subtracts the enemy's defense **before** the multiplier:

```
damage = (totalAdvAttack − defense/divisor) × multiplier × Random.Range(0.8, 1.2)
divisor = 3 for pierceAttack, 2 for everything else
```

so a guaranteed one-shot on floor `L` needs

```
attack ≥ 1.05^L × ( 600×1.02 / (0.8 × M) + 10×1.02 / divisor )
                   \______ AttackPerFloorUnit(M, piercing) ______/
```

**The defense term is a constant — it does not shrink as the rotation gets stronger.** The retired
`FloorHpNormalizer = 771.375` / `PiercingHpNormalizer = 769.25` folded it inside the `0.8` divisor,
i.e. divided it by `M` too. They are exactly `(600×1.02 + 10×1.02/divisor)/0.8`, correct only at
`M = 1` and increasingly OPTIMISTIC above it:

| M | floors overshot |
|---|---|
| 1 | 0 (0.17 % conservative) |
| 10 | +1 |
| 29 (ult × offBuff × ultBuff × charge 2.2 × mega) | +3 |
| 100 | +10 |

Those are floors the advisor would park on without being able to guarantee the one-shot it assumed —
worst exactly in Offensive mode, where the full buff stack lives. Pinned in
`tests/NGUAdvisor.Tests/ItopodMathTests.cs`, including the derivation of the two old constants.

API: `AttackPerFloorUnit`, `NormalizedAttack`, `FloorOfNormalized`, `BestFloor`,
`MultiplierForFloor` (the inverse — and it returns `+inf` when the scaled defense alone eats the
whole swing, a state the old form could not express). `MaxFloor = 1600` (`maxItopodLevel()`).

`ITOPODManager` never pre-multiplies a normalized attack by a buff factor any more: `FloorFor(choice,
buffMulti)` passes the multiplier into the solve. `ChooseAttack`/`ChooseMaxAttack` return an
`AttackChoice { Multiplier, Piercing }` — piercing carries its own flag for the `defense/3` divisor,
and its multiplier is `strongAttackMulti`, NOT `pierceAttackMulti`: `PlayerController.pierceAttack()`
reads `adventureController.strongAttackMulti`, which leaves `Character.pierceAttackPower()` dead code
for damage. Any "extra multiplier needed to reach floor L" must be `MultiplierForFloor(...) /
choice.Multiplier` — the required multiplier is not linear in the floor gap, so
`1.05^L / normalizedAttack` is wrong for the same reason.

## `ProfileForMode(combatMode)` — what advisors price ITOPOD with

Replaces `OptimalFloorForMode`, which answered with a single floor derived from the regular attack
alone. That is not what the pod runs: `OptimizeFloor` re-picks the floor between every pair of kills
from whichever move is off cooldown, so the yield is an AVERAGE over the rotation, and the spread
between a regular swing and a buffed ultimate is 20–30× ≈ 66 floors.

Returns `Profile { CycleSeconds, KillsPerSecond, DefaultFloor, PeakFloor, Slices[] }`, where each
`RotationSlice` is `{ Fraction, Floor }`. Shares: each big move fires at most once per its own
cooldown, so it takes `cycle / cooldown` of the kills, strongest first, remainder to the regular
attack. Buff uptime (`min(1, duration/cooldown)` for the offensive and ultimate buffs) splits each
attack share again, treated as independent of the move schedule — the conservative side, since
`CombatAI` does try to line them up.

Two things it deliberately does NOT read live:

- **Beast mode.** `beastModeBonus()` is folded into `totalAdvAttack()`, so sampling it live would
  make a "what if" answer depend on whether beast happened to be up. Starts from
  `ZoneStatHelper.EffectiveAdvAttack()` (beast-free) and adds the mode's own beast policy back
  (`×1.5` with Purple Liquid, else `×1.4`).
- **Floors we cannot reach.** Capped at `highestItopodLevel − 1`; farming above that needs a push.

## Floor solve (always the former "PP" mode)

`Settings.ITOPODOptimizeMode` is no longer read (user decision 2026-09-22: the Optimize picker was
dropped as useless). The one solve left: `PlanBuffs` queues the next buff that fits the respawn
window, and `OptimizeFloor` re-picks, between every pair of kills, the best floor for the strongest
attack available within that window, buff-aware (`multi` from queue head + active buff durations vs
remaining respawn). `SolvedMaxFloor` is rounded down to 10s. The former Default (regular attack only,
defers to the game's Lazy ITOPOD) and EXP/AP (buff burst on the AP kill) paths are gone; the setting
survives only for the retired grid and old settings files.

## Floor modes (`Settings.ITOPODFloorMode`)

An axis of its own, orthogonal to the optimize mode above: WHICH floor, not how it is solved.

| Mode | Behavior |
|---|---|
| 0 Optimal | the solve above owns the floor; `ITOPODAutoPush` is false, so it never climbs past `highestItopodLevel − 1` |
| 1 Fixed | `ITOPODTargetFloor` IS the floor. `UpdateMaxFloor` skips the attack solve, `OptimizeFloor` writes the target and returns — no per-kill re-optimization, no buff-aware shifting |
| 2 Push | climbs to `PushTargetFloor()` — the highest floor whose FIGHT is won (below) — in the `GearChain.ItopodPush` set, then returns to Optimal by itself |

Stored values are `FloorModeOptimal/Fixed/Push`; the picker shows them as Push, Optimal, Fixed
(`AdventurePanel.FloorModeByItem`), so never write a picker index into the setting.

**Push wears its own gear.** While the floor mode is Push and the character stands in the pod,
`AdvisorApply`'s gear pass uses `ITOPOD Push` as an override (beside Loot Hunter, below challenge
rotation). The floor solve reads the gear worn at that moment, so `UpdateMaxFloor` does not START a
climb while `AdvisorApply.ItopodPushGearPending()` — the gear pass will swap but has not yet. That
check repeats every exit of the gear pass (advisor off, gear refresh or ManageGear off, quest lock,
challenge, hunt), so a pass that will never swap cannot stall the push. `PushTargetFloor()` is the
same solve, read-only, for the page's "Push: floors A-B reachable" line.

`ITOPODAutoPush` survives as the underlying **permission** flag rather than a UI control, because the
push-death rule needs to revoke permission without discarding the mode the user chose. On a death
during a push it clears, and Push falls back to Optimal (Push is nothing but the climb, so leaving it
selected would show a mode that no longer does anything). A **fixed** target survives that death: it
stops climbing and farms the highest floor reached.

A fixed target above `highestItopodLevel − 1` pushes to the TARGET, not to the solved maximum — the
"need to push" branch in `UpdateMaxFloor` reads `maxFloor`, which Fixed has already set to the target.

## Push target: the fight, not the one-shot

`ItopodConstants.BestWinnableFloor` replays one fight per ITOPOD AI, action by action, and bisects
the floor (every enemy stat grows with it, ours do not). Game truth (decomp `EnemyAI`): enemy hit
`max(0.1·atk, atk − totalAdvDefense/2)·roll`, ×3 in beast mode (`PlayerController.takeDamage`),
AR 1.2 s; every AI counter resets at spawn, and the pod spawns one of each of normal, charger (4×
hit every 5th action, actions 3–4 idle), poison (+`floor(0.2·atk·roll)` straight off curHP on 5 of
every 9 actions — defense does not touch it), rapid (actions 9–14 at 0.3·AR), grower (×(1 +
⌊n/2⌋/5)) and paralyze (2 s of no attacking every 12 actions, action 1 of each cycle idle). Regen
ticks on both sides. The enemy side takes its WORST jitter and roll (1.02, 1.2) because a death
ends the push; our side is the mean sustained rotation (`BoostValueMath.SustainedDamagePerSlot`),
buffs, heals, block and parry left out — all of them only help. A fight past
`MaxFightActions` counts as lost. The target is `max(fight floor, one-shot floor)`, rounded DOWN
to a 10 (user rule 2026-09-23: 418 is not worth the climb over 410; 420 or stay on 410).

**Push finishes by itself.** Once the push set is worn (`ItopodPushGearPending()` false) and the
target is no longer above `highestItopodLevel − 1`, `UpdateMaxFloor` sets the floor mode back to
Optimal and clears `ITOPODAutoPush`; the gear override ends with it (user request 2026-09-23).

## Push mode

Entered when `maxFloor > highestItopodLevel − 1` and `ITOPODAutoPush`: sets range
`(highest−1, maxFloor+1)` and fights with CombatAI mode 2 (full defense). **A death during push
(itopodLevel dropped below highest−1) permanently flips `Settings.ITOPODAutoPush` off** — the
advisor won't retry a push it died in.

## Gotchas

- `UpdateMaxFloor` always force-disables the game's `lazyITOPODOn` (they'd fight over the floor).
- Beast-mode enable in idle combat briefly toggles `autoattacking` off/on around the cast
  (`CheckBeastMode`) — the game blocks the cast while auto-attacking.
- `haveCast` gates Fight() so exactly one buff cast happens per respawn window before attacking.
- Farm-mode fighting uses CombatAI mode 4 (one-shot: regular attack spam); Move 69 is weaved
  between fights when not pushing.
