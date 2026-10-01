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
- **Titan targets**: `targets[i] = !ak || ZoneHelpers.TitanHasWantedDrops(i)`. Below AK, attend the
  spawn to FIGHT it. At AK, the titan dies in whatever is worn — so attend only while its drop table
  still owes gear, and that spawn is then worth a swap into loot accessories
  (`GearOptimizer.ResolveTitanGear` → `GearChain.LootChain`). A titan with nothing left to give is
  worth no swap at all. The earlier `if (!ak)` alone meant an AK titan never entered
  `TitanSwapTargets`, so `RefreshTitanSnapshots` never listed it, `AnyTitansSpawningSoon()` stayed
  false and no lock was taken — the kill landed in whatever gear the advisor happened to be wearing
  and its drop chance was wasted (user-reported 2026-09-16). During a challenge the whole target
  list is still cleared, AK titans included.
- **Gear refresh** (`ApplyGearRefresh`, throttle 120 s): objective resolution order is
  challenge rotation > GEAR HUNT ("LOOT HUNTER") > `ChallengeOverlay.GearObjectiveOverride` >
  profile's `GearBreakpoints.ActiveChainSource ?? ActiveObjective`. **The challenge rotation only
  ever reaches this list when the profile named no gear for the challenge** — `ChallengeOverlay.Tick`
  suppresses its own override on `GearBreakpoints.ProfileOwnsGear` (see ChallengeOverlay.md), so the
  precedence here never has to arbitrate profile-vs-rotation. The hunt must be checked FIRST
  outside challenges — the override is set whenever AutoProfile runs, so `override ?? hunt` never
  fell through (user-reported). The profile's `ActiveChain` is used **iff no override is in play**,
  which is a flag and deliberately NOT a name comparison: `ActiveObjective` is a chain's lead
  objective, so an override asking for "Adventure" used to inherit the profile's
  "Adventure + Respawn" chain. Three anti-churn rules, each from a real bug:
  1. `_gearAsserted=false` on every payload load → first pass equips UNCONDITIONALLY (a reload
     can leave a lock's TEMP loadout worn with the restore set lost — statics wipe).
  2. Objective CHANGES bypass the 2 % improvement bar (`GearReequipBar`; "wrong gear within the bar
     on the new objective is still wrong gear" — TM HOUR wearing the push loadout). Lowered from 5 %
     on user request 2026-10-01; no flip-flop is possible at any bar, because the optimizer is
     independent of what is worn (pools = worn ∪ inventory, ascent from an empty set) and both sides
     of the bar use one scorer.
     **On the same objective the bar is judged per chain step** (`GearChain.DecidingStep`, 2026-10-01):
     lexicographic like the chain itself — the first step whose best/worn ratio leaves `[1/bar, bar]`
     decides (above → equip, below → hold); every step inside → hold. Per step the worn side is
     `GearOptimizer.CurrentScore(objective_k)` and the best side `ScoreOf(best, objective_k)`. The lead
     alone used to decide, so under `Respawn(3)+PowerWeapon > NGUs(all)` the capped Respawn lead read
     x1 forever and a +57 % NGU accessory set was never equipped (user-reported 2026-10-01).
  3. `_lastGearObjective` commits ONLY when the switch actually resolves (equip or verified
     optimal) — a fizzled pass must not consume the bypass (segment flipped during a titan lock;
     stale AT gear then sat inside the bar forever). "Verified" on a switch is SET MEMBERSHIP
     (every id of the best set already worn), never `best.Score <= cur`: the score is the chain's
     lead objective only, and a chain whose later steps take accessories from the lead (ITOPOD
     Push) always scores below a set built for the lead alone — the old test declared the profile's
     pure-NGU set "optimal" and the push set was never equipped (2026-09-22).
  4. The membership test also runs AFTER the bar clears on the same objective. "Worn" means worn AS
     the best copy: ids all worn **and** `!LoadoutManager.HasStrongerCopyToSwapIn(ids)`. A stronger
     duplicate of a worn id is therefore not worn, and `ChangeGear` now swaps it (LoadoutManager.md);
     without a free inventory slot it cannot, both sides report "nothing to swap", and the pass holds
     instead of re-announcing "+x % from new drops" every 120 s. The LOOT HUNTER membership test uses
     the same copy check.
  `GearRestored()` clears both the marker and the throttle. Called by LockManager after a restore
  and by `GearBreakpoints.PerformSwap` after the profile swaps gear — a breakpoint re-applied on
  rebirth/reload otherwise overwrote an override set (ITOPOD Push) while the marker still claimed it.
  **ITOPOD Push override**: floor mode Push + standing in the pod → `ITOPOD Push`, beside Loot
  Hunter, below challenge rotation. `ItopodPushGearPending()` holds ITOPODManager's climb until it is
  worn (see ITOPODManager.md). A CHANGE of the override name (Push toggled, leaving the pod, hunt,
  segment) skips the 120 s throttle, so the set returns on the next 30 s tick — with the throttle,
  turning Push off left the push set on for up to two minutes (user-reported 2026-09-22).
- **Wandoos OS switch**: switching wipes the target OS's levels, so it needs BOTH the ≥1.25×
  projected advantage (same threshold that turns the advisor row red — row and auto agree) AND
  projected-hour-from-zero ≥ 1.5× the CURRENT real bonus (pay for itself within the run);
  ≤ 1 switch / 10 min.
- **Titans** (`ApplyTitans`): targets every reachable below-AK titan (riddle titans 6/7/8 only
  when their quest flags unlock); challenge active → stand down (below-AK titans unviable).
  The chased version is `OptimizationAdvisor.PushObjective()`: the highest version whose staged
  requirement projected best gear clears (user rule: push the best version the gear allows),
  falling back to `NextObjective()`'s lowest un-AK'd version.
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
  **Every routing layer that parks the character also sets the combat mode** via
  `ApplyFarmCombatMode` — including the hunt, which until 2026-08-10 wrote only `SnipeZone` and so
  inherited the previous layer's mode (Idle, if the advisor had been parked in the pod). That halves
  the drops the hunt was routed for: idle pays a full `attackSpeed` of spawn latency on EVERY kill
  while a manual mode lands the opening swing on the spawn frame, so manual is never slower and up
  to 2× faster (ZoneCadence.md). The hunt's mode comes from the same measured source the farm layers
  use — `ZoneCadence.FastestMode(zone)`, the fastest survivable of Idle/Offensive for that one zone —
  and only when no cadence estimate exists at all does it fall back to Offensive on the
  never-slower rule (Idle if the regular attack is not unlocked yet). `modeSrc=` in the `[ZoneDbg]`
  line names which of the three it was.
  Then Farm Gear Zones (permanent item-max bonuses) > boost farm > ITOPOD; Farm Best Boost
  falls back to ITOPOD when `BoostDemandExists` says nothing consumes boosts.
  **The fallback picks its combat mode on PP, not on boosts** — routing to the pod *because nothing
  consumes boosts* and then choosing the mode by boost rate is self-contradictory. PP is the currency
  nothing else in the game produces, so it decides; the PP/EXP/AP rates and the floor band go in the
  log line (`ItopodFarmAdvisor`). When ITOPOD wins on boosts outright, the boost mode stands.
- **Titan gold** (`ApplyTitanGold` + `BestGoldTitan`, on the 30 s AK cache): auto-targets the
  **most profitable** AK-able titan — NOT the highest, titan gold is not monotone in index
  (GoldDropAdvisor.md, "Ranking, not height") — **on every AK cycle**; the kill is free, so there is
  no payoff gate and the `TitanMoneyDone` latch is re-armed here rather than blocking (see
  GoldDropAdvisor.md, "Why titan gold has no gate"). `[TitanGoldDbg]` in `debug.log` records the whole
  decision when it changes. `HighestAkTitan()` stays: other advice reads it, and it is the fallback
  when nothing is eligible (so the clearing pass still runs). Panels read `GoldTitanTarget()`, the
  cached pick — they run on the WinForms thread and must never re-rank, since `PredictedDrop` reaches
  the gear optimizer.
- **Gold** (`ApplyGold`): auto-CBlock during challenges; gold-starvation re-snipe trigger
  (clears `GoldSnipeComplete` when augs unaffordable despite TM holding gold) — **both the
  starvation trigger and the new "gold drop improved" trigger go through GoldDropAdvisor**, so a
  snipe is never re-armed for a drop the Time Machine would discard (GoldDropAdvisor.md).
- **Quests**: asserts the advisor strategy once (majors on, bank guard, abandon minors < 30 %,
  butter majors only, **50-item minors always on**, minors manual only while a quest item is being
  levelled). The 50-item rule used to follow `perk 94 >= 610`, which was inverted — that perk is
  exactly the case where the rule does nothing, because the game already hands out a flat 50
  (QuestManager.md has the decomp).
- **EXP buys**: one `ExpBalancer.BuyTick(0.60)` walk step per minute — 60 % of the bank each
  time, the rest left as a hand-spending reserve. The waterfill can't overshoot the ratio at any budget, and banked EXP earns nothing, so the
  old 10 % budget only meant EXP idled ~10 minutes on average before being spent.
- **Blood**: cast timing + single-sink routing from BloodPlanner (60 s throttle); pooling turns
  ALL auto-spells off so the pill can charge.

## Diagnostics (`debug.log`) — grep a tag before adding a fourth channel

All three channels below copy `[TitanGoldDbg]`'s discipline exactly (GoldDropAdvisor.md
§Diagnostics): a stable greppable tag, a **60 s cadence cap checked BEFORE the line is rendered**,
then **emit only when the rendered line CHANGED**, and every input of the decision on one line so
the line alone explains the outcome. Rendering is wrapped per channel — a logging fault can never
escape into the step it observes. Observation only: none of them changes a decision.

- **`[ZoneDbg]`** (`LogZoneDbg`, called from every exit of `ApplyZones`) — **which LAYER routed the
  zone and what lost.** The layer field is the point: `none` / `gold` / `gearhunt` / `gearfarm` /
  `boostfarm` / `itopod`, in the precedence the code actually applies.

  **`zone=` is where `Main.Update()` will send the character, NOT this layer's pick.** ApplyZones only
  writes `Settings.SnipeZone`; `Main.ResolveAdventureZone()` then overrides it with the gear hunt,
  Target ITOPOD or the locked-zone fallback. Both callers ask that ONE method — the line used to
  re-derive nothing at all and reported the pick, so with Target ITOPOD on it named a farm zone
  nobody was in while the character sat in the pod (user-caught). When the two differ the line adds
  `advised=<n> (<name>) overriddenBy=<gear hunt|Target ITOPOD|zone locked>`. The EVIL CLIMB and
  gold-starved detours resolve through `UpdateFurthestZone()` and stay out of it — a logger must not
  drive that. Carries the applied combat
  mode, the winner's rate, `beat=` (the runner-up and why it lost — the nearest non-viable gear zone
  with the drop chance it needs, or the ITOPOD's boost rate), `boostDemand=` (the
  `BoostDemandExists` gate) and `gearfarm=` (why the gear farm did not take the routing, carried
  into the boost line so one line explains the whole chain). On the `gearhunt` layer, `wantMode=`
  plus `modeSrc=` say which mode the hunt applied and whether it was measured or a fallback. The user-facing
  `Advisor: farm zone -> …` lines go to the advisor output log, name only the winner, and are absent
  entirely on the paths that decline to route. The cadence cap matters only for the exits ahead of
  `ApplyZones`' 10-minute throttle (combat off, gold modes, gear hunt, `AdvisorZones` off) — those
  run on every 30 s tick; the change check matters on all of them, because an unchanged line
  repeated every 10 minutes for hours buries the transitions.

  ```
  [ZoneDbg] layer=itopod zone=1000 (ITOPOD) combat=Offensive pick=ITOPOD rate=no boost demand — cube at softcap, no gear needs boosts · 0.0121 PP/s, 3.44 EXP/s (floors 700-1150) wantMode=Offensive beat=every farmable zone boostDemand=False gearfarm=nothing uncapped in budget
  ```

- **`[GearDbg]`** (`LogGearDbg`, called from every exit of `ApplyGearRefresh`) — **why gear was or
  was not re-equipped.** Verdict is `EQUIP` / `HELD` / `OFF`, then the active objective, the rendered
  chain (`GearChain.Describe` — the same key `_lastGearObjective` commits), `switch=` (objective/chain
  change, which BYPASSES the bar), `asserted=` (the post-load unconditional assert), `cur=` vs
  `best=` and their `ratio=` (the lead step), `steps=[<objective> x<ratio> | …]` (every step's
  best/worn ratio, `new` = the worn set scores 0 there), `decided=<k>:<objective>` (or `none`), against
  `bar=x1.02`, and `why=`. Only equips were ever announced, so the
  common outcome — the bar holding the worn set — left no trace, and neither did the two scores it
  was measured on. The LOOT HUNTER path logs its membership test instead of a score (it has no single
  objective score). No extra optimizer work: the renderer reads the scores the decision already
  computed. The 120 s throttle covers the score path; the cap covers the exits in front of it.

  ```
  [GearDbg] HELD obj='NGU MARATHON' chain='Energy NGU(all) > Respawn(1)' switch=False asserted=True cur=4.512e6 best=4.581e6 ratio=1.015 steps=[Energy NGU x1.015 | Respawn x1] decided=none bar=x1.02 why=same objective and every step inside the re-equip bar
  ```

`AdvisorApply.LastGearChain` / `LastGearVerdict` expose the last applied chain key and `[GearDbg]` line
read-only (StateExport's GEAR section). `ApplyPit` logs the gold with each throw, passes the plan
verdict to `MoneyPitManager.AdvisorThrow(source)` and reports verdict changes via
`MoneyPitManager.LogPlanChange` (MoneyPitManager.md).

`[TitanGoldDbg]` (`LogTitanGoldState`) is documented in GoldDropAdvisor.md §Diagnostics;
`[DiggerDbg]` lives in DiggerManager. `[CapDbg]` is in LevelPlanner.md.
