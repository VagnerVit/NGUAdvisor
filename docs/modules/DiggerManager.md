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

## RecapDiggers — buy the cheapest next level (2026-09-20)

`RecapDiggers(priorityOrder)` resets the set to level 1 and hands it to `LevelWithinBudget`, which
repeatedly buys **the cheapest next level anywhere in the set** until the next one does not fit in
`gps × DiggerCap`.

Game truth (decompiled `AllGoldDiggerController.drain`): a digger's drain is
`baseGPSDrain[id] * gpsGrowthRate[id]^(curLevel−1)`, with growth around 1.5–1.75 per digger. So the
top level of an expensive digger costs more than dozens of levels further down, and the marginal
cost of the next level is `drain(d, 1) − drain(d, 0)`.

### Why priority-order leveling was replaced

The previous allocation walked the set in priority order and gave each digger everything it could
carry before moving on. On a live set that meant the lead digger ate **98.9 %** of the budget and the
tail ran on crumbs. Two consecutive `[DiggerDbg]` lines caught it exactly (user-reported
2026-09-20): Adventure going L85 → L86 added 3.1e26 drain and cost Drop Chance **twelve levels**
(L78 → L63) — one level bought at the price of twelve. Same budget, measured: priority order bought
230 levels, marginal cost bought 260.

**What this deliberately gives up.** Priority no longer decides who gets leveled. It decides who
gets a SLOT (`CurrentDiggerSet` / `ReconcileAdvisorDiggers` still rank membership) and it breaks
ties in the buy loop. An expensive digger the advisor ranks first now gets fewer levels than before
— including the Evil-climb Stats digger, whose "level it first" behaviour was itself a fix. That
regression is intentional and user-directed; if it needs to come back, it has to come back as a
membership or budget rule, **not** by restoring a first-come allocation.

It is only defensible because levels are the one currency every digger shares: adventure stats vs.
drop chance vs. NGU speed have no exchange rate, and inventing one would be a made-up constant
driving real decisions.

### Two things that look like bugs and are not

- **The budget is not spent to the last coin.** The loop stops at the first level that does not fit.
  A leftover means the cheapest remaining level costs more than what is left — `spent=` in
  `[DiggerDbg]` is there to show it.
- **The old even `gps/count` split is not what this is.** That split collapsed every digger to level
  1 on Evil (per-level drains dwarf `gross/count` — user-caught: 6–9 diggers stuck at L1 with 9e21
  gross). Buying by marginal cost has no fixed per-digger share: a digger that cannot afford level 2
  simply stops bidding and the rest of the budget goes to the others.

The advisor MUST pass its ranked set (`RecapDiggers(set)`) — the parameterless overload levels
against `_curDiggers`, which the reconcile path never updates (stale lock-swap order).

Give-back after the loop walks the LIVE `totalGPSDrain()` and drops the most expensive level in the
set, mirroring how they were handed out. It exists for double-summation drift, not for policy.

## Upgrades

`UpgradeCheapestDigger` (gated on `Settings.UpgradeDiggers`): buys max-level upgrades for the
globally cheapest digger while `cost + MoneyPitThreshold <= realGold`, recursing to the next
cheapest. `[DiggerDbg]` recap diagnostics go to debug.log, throttled 60 s and written only when the
decision changes (src, order, per-digger level/max); gross/budget/drain magnitudes are printed but
are not part of the change key.
