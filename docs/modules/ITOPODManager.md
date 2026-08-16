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
for damage. Mode 3's `threshold` is `MultiplierForFloor(...) / choice.Multiplier` — the required
multiplier is not linear in the floor gap, so the old `1.05^maxFloor / normalizedAttack` was wrong
for the same reason.

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

## Optimize modes (`Settings.ITOPODOptimizeMode`)

| Mode | Behavior |
|---|---|
| 0 | No floor optimization |
| 1 | "Lazy shifter": best floor for regular/idle attack; defers to the game's own Lazy ITOPOD when bought+on |
| 2 | Best floor for the strongest attack available within the respawn window, buff-aware; maxFloor rounded down to 10s |
| 3 | AP-cycle optimizer (see below); maxFloor rounded down to 50s |

Mode 2/3 plan a buff queue (`PlanBuffs` → `nextBuffs`) and re-optimize the floor after every kill
(`OptimizeFloor` runs between fights only). The floor picked accounts for buffs about to be cast
(`multi` from queue head + active buff durations vs remaining respawn).

### Mode 3 — the AP-kill cycle

ITOPOD awards AP every N kills (`lootDrop.killsUntilAP`); tiers are 50-floor bands
(`lootDrop.itopodTier`). Mode 3 farms at the regular-attack default floor, and when the AP kill
is 3 kills away, schedules a buff burst (`Buff.None, None, <buff>` = "two plain kills, then the
buffed one") to one-shot a HIGHER tier floor exactly on the AP kill — then returns to the default
floor. `threshold = 1.05^maxFloor / maxAttack` picks the cheapest sufficient combo in escalating
order: Charge → OffBuff(×1.2) → UltBuff(×1.3) → combinations → MegaBuff(×1.2·1.2·1.3) → Charge ×
combos (`chargePower()` is the game read). Floors ≥ 1550 or tier ≥ 20 with fast respawn skip the
dance.

## Floor modes (`Settings.ITOPODFloorMode`)

An axis of its own, orthogonal to the optimize mode above: WHICH floor, not how it is solved.

| Mode | Behavior |
|---|---|
| 0 Optimal | the solve above owns the floor; `ITOPODAutoPush` is false, so it never climbs past `highestItopodLevel − 1` |
| 1 Fixed | `ITOPODTargetFloor` IS the floor. `UpdateMaxFloor` skips the attack solve, `OptimizeFloor` writes the target and returns — no per-kill re-optimization, no buff-aware shifting. Works even with `ITOPODOptimizeMode == 0`: it is an instruction, and the game's Lazy ITOPOD would otherwise drift off it |
| 2 Max | the solve above, pushing as high as the rotation one-shots (`ITOPODAutoPush` true) |

`ITOPODAutoPush` survives as the underlying **permission** flag rather than a UI control, because the
push-death rule needs to revoke permission without discarding the mode the user chose. On a death
during a push it clears, and mode 2 falls back to Optimal (Max is nothing but the push, so leaving it
selected would show a mode that no longer does anything). A **fixed** target survives that death: it
stops climbing and farms the highest floor reached.

A fixed target above `highestItopodLevel − 1` pushes to the TARGET, not to the solved maximum — the
"need to push" branch in `UpdateMaxFloor` reads `maxFloor`, which Fixed has already set to the target.

## Push mode

Entered when `maxFloor > highestItopodLevel − 1` and `ITOPODAutoPush`: sets range
`(highest−1, maxFloor+1)` and fights with CombatAI mode 2 (full defense). **A death during push
(itopodLevel dropped below highest−1) permanently flips `Settings.ITOPODAutoPush` off** — the
advisor won't retry a push it died in.

## Gotchas

- `UpdateMaxFloor` force-disables the game's `lazyITOPODOn` in modes ≥ 1 (they'd fight over the
  floor) — mode 1 is the exception that respects it.
- Beast-mode enable in idle combat briefly toggles `autoattacking` off/on around the cast
  (`CheckBeastMode`) — the game blocks the cast while auto-attacking.
- `haveCast` gates Fight() so exactly one buff cast happens per respawn window before attacking.
- Farm-mode fighting uses CombatAI mode 4 (one-shot: regular attack spam); Move 69 is weaved
  between fights when not pushing.
