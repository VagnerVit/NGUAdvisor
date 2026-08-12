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
  profile's `GearBreakpoints.ActiveChainSource ?? ActiveObjective`. The hunt must be checked FIRST
  outside challenges — the override is set whenever AutoProfile runs, so `override ?? hunt` never
  fell through (user-reported). The profile's `ActiveChain` is used **iff no override is in play**,
  which is a flag and deliberately NOT a name comparison: `ActiveObjective` is a chain's lead
  objective, so an override asking for "Adventure" used to inherit the profile's
  "Adventure + Respawn" chain. Three anti-churn rules, each from a real bug:
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
  butter majors only, 50-item rule follows perk 94 ≥ 610).
- **EXP buys**: one `ExpBalancer.BuyTick(0.10)` walk step per minute.
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
  `best=` and their `ratio=` against `bar=x1.05`, and `why=`. Only equips were ever announced, so the
  common outcome — the 5 % bar holding the worn set — left no trace, and neither did the two scores it
  was measured on. The LOOT HUNTER path logs its membership test instead of a score (it has no single
  objective score). No extra optimizer work: the renderer reads the scores the decision already
  computed. The 120 s throttle covers the score path; the cap covers the exits in front of it.

  ```
  [GearDbg] HELD obj='NGU MARATHON' chain='Energy NGU>Respawn' switch=False asserted=True cur=4.512e6 best=4.663e6 ratio=1.033 bar=x1.05 why=same objective and inside the 5% re-equip bar
  ```

`[TitanGoldDbg]` (`LogTitanGoldState`) is documented in GoldDropAdvisor.md §Diagnostics;
`[DiggerDbg]` lives in DiggerManager. `[CapDbg]` is in LevelPlanner.md.
