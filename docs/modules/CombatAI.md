# CombatAI (`Managers/CombatAI.cs`)

Per-fight decision engine, instantiated fresh EVERY move tick by `CombatManager.DoCombat`
(constructor takes the full combat snapshot; instances are throwaway). Pipeline per tick:
`DoPreCombat()` → `DoCombatBuffs()` (includes `DefensiveRoutine`) → `DoCombat()` (attack pick).
Each stage returning true consumes the tick.

## CombatSnapshot — the enemy attack-loop model (decomp game-truth)

Enemies attack on fixed loops; the snapshot predicts the special/big attack:

| Enemy | LoopSize | Special# | Warning# | Notes |
|---|---|---|---|---|
| Walderp (zone 16) | 6 | 3 | 2 | "Walderp says" — see below |
| Beast (19) | 10 | — | — | loop only |
| Nerd (23) | 8 | 4 | none | damaging warning on 3 |
| Godmother (26) | 9 | 4 | none | explosion mode = attack rate ÷5 (i.e. 5× faster) |
| Exile V1/V2 (30) | 10/9 | 4 | 3 | |
| Exile V3/V4 | 8/7 | 3 | none | |
| Jake (bigBoss3) / Tippi (finalBoss) | rapid: rate ×0.15; locustCount ≥ 10 → every attack special | | | skipturn < 0 → rate ×1.15 |
| AI.charger | 5 | 4 | 2 | counter increments on loop ENTRY — loop/special numbers off by one (`chargeCooldown − 1`) |
| AI.rapid | 14 | 7 | 4 | same off-by-one (`rapidEffect − 1`) |
| AI.exploder | 1 | 1 | — | every attack is the big one |

Key derived values: `AttacksToBlock = ceil(2.9 / attackRate)` (block covers 2.9 s);
`OptimalTimeToBlock = 2.95 − rate × (AttacksToBlock − 1)` — cast block at this remaining-time to
eat the maximum number of hits. `NextAttackNoDamage` / `BlockableAttackNoDamage` model warning
moves; `TimeTillNextDamagingAttack` skips them. First strike in non-titan zones is delayed by
+50 % of the rate (`firstStrike` field). Paralyze time-left is added to `timeTillAttack`.

## Defensive routine — the Should / Delay / WaitFor pattern

Every defensive move runs three checks (documented at `DefensiveRoutine`):
**Should** (worth using?), **Delay** (worth it but wrong moment — e.g. never parry/paralyze an
attack block will cover), **WaitFor** (returning true with NO cast to stall the tick — burning
fractions of a second so the move lands at its optimal moment). WaitFor stalls are deliberate:
do not "fix" a `return true` without a cast.

Order (mode ≤ 2): `CheckFatalBlow` → Paralyze (if prioritized) → Block → Paralyze → disable
BeastMode → Parry. Offensive mode (3): paralyze-AI enemies paralyzed, Parry only, no stalls.

- **Prioritized paralyze** enemies: poison AI (block is weak vs poison), grower (slows growth),
  Jake incl. his Amalgamate appearances, Walderp, IT HUNGERS (saves glop).
- **`CheckFatalBlow`**: if the next damaging attack would kill — postpone (paralyze), drop beast
  mode (3× damage taken!), parry (halves), then block even if it was held for a special;
  charge multiplies parry/block mitigation (`chargeMulti`).
- `IncomingDamage(block, parry, charge)` mirrors the game: titan growCount scaling, beast ×3,
  block bonus, parry ÷2, poison DoT window, charger special ×4, exploder ×1000, grower scaling.
- **OhShit replaces Paralyze** when ready and HP < 60 %.

## Beast mode contract

`CheckEnableBeastMode` consults `CombatManager.DesiredBeastMode()` FIRST — the user/advisor
setting outranks the AI (user-reported death loop: offensive mode force-cast beast over
`TitanBeastMode=false`). Enable needs: cooldown bonus ≤ 1.01/1.5, negligible block bonus, block+
paralyze available soon, HP thresholds per mode; disable path (`CheckDisableBeastMode`) drops it
before an unshielded damaging attack or when HP after the hit would be too low. `CastBeastMode`
paths return true even when the button glitch keeps it non-interactable (comment: game bug).

## Walderp ("Walderp says")

On `inWaldoSaysLoop`: `waldoSays` = the named move is FORCED (must be used by attack 3);
otherwise the named move is BANNED (any other move must land). Forced Ultimate waits for Charge
when affordable. Strong/Pierce are held back near his special so they're available to answer.

## Attack selection (`CombatAttacks`) — DPS economics

- One-shot checks first: `oneShotDamage = curHP + defense/2` (pierce: `defense/3`) vs
  `attackMultiplier × movePower` where attackMultiplier includes 0.8 safety factor.
- Otherwise a **gain/loss wait model**: `gain = power × (globalCooldown − remainingCooldown)`
  per available-but-not-ready stronger move vs `loss` = best ready move; waits for the stronger
  attack only when there's enough quiet time (`TimeTillNextDamagingAttack − OptimalTimeToBlock >
  2 × globalCooldown`).
- Charge-waste guards: don't burn an active Charge on a weak attack when Ultimate is imminent;
  with Beast set 1 complete (`beast1complete`, parry ×3 damage), letting Parry consume Charge is
  acceptable.
- Ultimate waits for Charge when `ChargeCooldown ≤ (ultCooldown − gcd)/chargePower` unless
  active buffs would expire.

## Fragile dependencies

Reflection field reads (decomp names): `enemyAttackTimer`, `firstStrike`, `rapidMode`,
`skipturn`, `locustCount`, `chargeCooldown`, `rapidEffect`, `paralyzeTime`, plus
`eai.growCount/auraID/kneeCapped/explosionMode/inWaldoSaysLoop/waldoSays/waldoAttackID`.
A game update renaming any of these silently degrades combat — check here first after a patch.
Aura 6 = regen negation (never HyperRegen); Beast V4 / Exile V2+ carry it periodically —
`DoHeal` times HyperRegen into the safe window of their loop.
