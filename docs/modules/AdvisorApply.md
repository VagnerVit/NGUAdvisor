# AdvisorApply (`Managers/AdvisorApply.cs`)

Phase B: opt-in auto-apply. When a system's `Advisor*` toggle is on, the advisor's recommendation
is APPLIED, not just displayed. Runs from Main's loop (main thread), tick throttled to 30 s;
every change is logged.

## Safety gates (order matters)

1. `GlobalEnabled` master must be on.
2. `ChallengeOverlay.Tick` + `LevelPlanner.Tick` run FIRST — the overlay computes the segment and
   the gear-objective override the gear refresh consults.
3. **Set/gear appliers run only under `LockManager.CanSwap()`** (diggers, beards, Wandoos OS,
   gear refresh) — mode locks own the sets. Purchase/routing appliers (perks, EXP, gold, pit,
   quests, blood, zones, titans, transforms) keep running during locks (audit fix: a titan wait
   used to stall perk/EXP/blood automation for no reason). `CanSwap()` is evaluated ONCE per
   tick — never re-checked between appliers.

## Fault containment (stage R2) — the design is deliberate, read before "simplifying"

One applier's exception used to kill every later one (a throw in ApplyPerks silently skipped the
remaining fourteen steps; unthrottled steps starved the tail PERMANENTLY, throttled ones
intermittently — worse to diagnose). The fix is per-step containment (`RunStep(name, action)`),
not a framework:

- Fault episodes are keyed by STEP NAME, never by exception message (messages carry
  ever-changing ids → keying on them would flood the logs the throttle exists to protect).
- First failure reports at once with the full stack; repeats report at most every
  `ReportEvery` (10 min) — the quiet-window text derives from the constant, never hardcoded.
- **"Recovered" is never claimed** — only "no exception for the interval". A throttled skip is a
  nonthrowing return indistinguishable from work; clear-on-return would read skips as recoveries
  and flood "fail/recovered" pairs. Episodes clear only after a full interval without a throw.
- Seven appliers catch their own complete bodies (Gold, Quests, Pit, Titans + gated inner calls)
  and call `OnStepFailed`/`ObserveStepReturn` themselves — their `ObserveStepReturn` is placed so
  a disabled/stand-down invocation never counts as a successful exercise. Do not double-wrap.
- The outer Tick catch fires only for orchestration faults (session-visible, same rate limit).

## Appliers — non-obvious rules

- **Diggers**: `ReconcileAdvisorDiggers` converges membership, then ALWAYS re-level via
  `RecapDiggers(set)` — leveling must not be gated on the full set activating (the Evil Blood
  digger can't afford level 1: base drain ~1e24 vs gross ~5e21 — the old "recap only on complete
  set" froze ALL diggers at level 1 the whole run, user-caught). Recommendation order passed
  explicitly so the greedy budget levels high-priority diggers first.
- **Gear refresh** (`ApplyGearRefresh`, throttle 120 s): objective resolution order is
  challenge rotation > GEAR HUNT ("LOOT HUNTER") > `ChallengeOverlay.GearObjectiveOverride` >
  profile's `GearBreakpoints.ActiveObjective`. The hunt must be checked FIRST outside challenges —
  the override is set whenever AutoProfile runs, so `override ?? hunt` never fell through
  (user-reported). Three anti-churn rules, each from a real bug:
  1. `_gearAsserted=false` on every payload load → first pass equips UNCONDITIONALLY (a reload
     can leave a lock's TEMP loadout worn with the restore set lost — statics wipe).
  2. Objective CHANGES bypass the 5 % improvement bar ("wrong gear within 5 % on the new
     objective is still wrong gear" — TM HOUR wearing the push loadout).
  3. `_lastGearObjective` commits ONLY when the switch actually resolves (equip or verified
     optimal) — a fizzled pass must not consume the bypass (segment flipped during a titan lock;
     stale AT gear then sat inside the 5 % bar forever).
  `GearRestored()` (called by LockManager) clears both the marker and the throttle.
- **Wandoos OS switch**: switching wipes the target OS's levels, so it needs BOTH the ≥1.25×
  projected advantage (same threshold that turns the advisor row red — row and auto agree) AND
  projected-hour-from-zero ≥ 1.5× the CURRENT real bonus (pay for itself within the run);
  ≤ 1 switch / 10 min.
- **Titans** (`ApplyTitans`): targets every reachable below-AK titan (riddle titans 6/7/8 only
  when their quest flags unlock); challenge active → stand down (below-AK titans unviable).
  First-kill objectives are ATTEMPTED only when projected best gear covers the manual stage
  (user-reported: doomed fights + spawn parked off the paying version). Spawn-version forcing:
  park on the highest AK-able version while a gold bank is pending (kill is free in gold gear —
  forcing the chase version turned it into a real fight in DROP gear, death loop) or while the
  next version is out of reach; otherwise force the chased version (spawn version never
  auto-advances in the game). Combat posture is FIELD-CALIBRATED: Defensive is the default for
  real fights (user cleared v2 only on Defensive); Offensive only when both stats fully cover
  the stage; Idle only at AK; beast only ≥1.25× the def bar on a proven kill. Also force-enables
  `SwapTitanLoadouts` (advisor owns titans → snapshot machinery must equip the kill set).
- **Zones** (`ApplyZones`): CBlock/pit-run gold logic owns zones — stand down. GEAR HUNT
  outranks everything, is cheap, and sits OUTSIDE the 10-min throttle (toggle acts next tick).
  Then Farm Gear Zones (permanent item-max bonuses) > boost farm > ITOPOD; Farm Best Boost
  falls back to ITOPOD when `BoostDemandExists` says nothing consumes boosts.
- **Titan gold** (`ApplyTitanGold` + `HighestAkTitan` 30 s cache): auto-targets the HIGHEST
  AK-able titan (its drop dwarfs all lower ones); re-banks when the AK version rises
  (`TitanGoldVersionBanked`).
- **Gold** (`ApplyGold`): auto-CBlock during challenges; gold-starvation re-snipe trigger
  (clears `GoldSnipeComplete` when augs unaffordable despite TM holding gold).
- **Quests**: asserts the advisor strategy once (majors on, bank guard, abandon minors < 30 %,
  butter majors only, 50-item rule follows perk 94 ≥ 610).
- **EXP buys**: one `ExpBalancer.BuyTick(0.10)` walk step per minute.
- **Blood**: cast timing + single-sink routing from BloodPlanner (60 s throttle); pooling turns
  ALL auto-spells off so the pill can charge.
