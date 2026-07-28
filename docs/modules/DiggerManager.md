# DiggerManager (`Managers/DiggerManager.cs`)

Digger set executor + leveler. TWO distinct write paths — do not merge them:

1. **`EquipDiggers` (clear-and-rebuild)** — used by lock swaps (Titan {11,8,3,0}, Ygg {11,8}),
   restores, quick swap, profile. Maintains `_savedDiggers/_tempDiggers/_curDiggers`.
2. **`ReconcileAdvisorDiggers` (converge-in-place)** — the advisor's path (AdvisorApply).
   Deliberately does NOT touch the saved/temp/cur statics — those belong to the swap/restore
   machinery.

## EquipDiggers rules (each fixed a report)

- **Bail BEFORE clearing at 0 GPS** — the old clear-then-fail loop stripped the active set every
  10 s pass post-rebirth ("diggers never turn on").
- Filter the request to usable diggers (leveled `maxLevel > 0`, distinct, ≤ slots) — a set naming
  locked diggers must not fail forever over them.
- Activation gate: `goldPerSecond() − drain(d) >= gross × (100 − DiggerCap)/100`.

## ReconcileAdvisorDiggers rules

- **Reset every ACTIVE digger to level 1 FIRST** — membership must be judged at the level-1
  baseline: recap redistributes the whole budget, so a kept member's inflated level must not make
  the set read "full" and freeze out a cheap newcomer (user-caught: the 1e12 Stats digger
  permanently locked out of an open slot because net GPS sat at the reserve).
- Drop obsolete members off a SNAPSHOT (`ActiveDiggers.ToArray()`) — `activateDigger` mutates the
  live list. Read the toggle RESULT (the game can refuse); never clear/rebuild on refusal.
- A member that can't afford activation is left missing and retried next pass — no churn.
- Complete = live active set EXACTLY equals the target (count + membership).

## RecapDiggers — greedy priority leveling

`RecapDiggers(priorityOrder)`: reset all to level 1, then level in PRIORITY order — each digger
sized against `gps − everyoneElse'sDrain` via `SetLevelMaxAffordable`
(`floor(log(cap/base, growthRate) + 1)`, clamped to maxLevel, rolled back if total drain exceeds
gross). **The old even `gps/count` split collapsed every digger to level 1 on Evil** (per-level
drains dwarf gross/count — user-caught: 6–9 diggers stuck at L1 with 9e21 gross). The advisor
MUST pass its ranked set (`RecapDiggers(set)`) — the parameterless overload levels against
`_curDiggers`, which the reconcile path never updates (stale lock-swap order).

`SetLevelMaxAffordable` intentionally handles LEVELS ONLY — the activateDigger arms that once
lived there were unreachable AND would have invalidated RecapDiggers' enumeration.

## Upgrades

`UpgradeCheapestDigger` (gated on `Settings.UpgradeDiggers`): buys max-level upgrades for the
globally cheapest digger while `cost + MoneyPitThreshold <= realGold`, recursing to the next
cheapest. `[DiggerDbg]` recap diagnostics go to debug.log, throttled 60 s.
