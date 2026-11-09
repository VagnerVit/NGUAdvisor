# ExpBalancer (`Managers/ExpBalancer.cs` + `Managers/ExpRatioTables.cs`)

EXP purchase planner: walks purchased E/M power/cap/bars toward the guide's ratios.
`ExpRatioTables` is the Unity-free, unit-tested half (which ratio does this phase want);
`ExpBalancer` reads the live character, picks the phase, and does the walking.

## Ratio semantics — the most misread numbers in the guide (user-verified vs Blaze's Ratioz)

- "3:1 E:M" is a ratio of PURCHASED STAT VALUES, **not** an EXP split. Magic units cost exactly
  3× energy (game unit costs: pow 450 vs 150, cap 3-per-750 vs 1-per-250, bars 240 vs 80 EXP),
  so a target value ratio of `r:1` needs an EXP split of `PoolE = r/(r+3)` — 3:1 lands on an EVEN
  50/50 split. Treating "3:1" as the EXP split drives the values to 9:1 and wastes EXP.
- "5:160k:4 pow:cap:bars" is also a UNIT ratio; in EXP-space a pool's shares are
  power:cap:bars = 750:640:320 (identical for magic — all its costs scale ×3).

## Phase table (`ExpRatioTables.For`, transcribed from `external/ngu-guide` ch.1-7)

| Phase (ProgressionAnalyzer.Chapter) | guide P:C:B | EXP-space shares | E:M values | EXP pool E/M |
|---|---|---|---|---|
| ch.1-2 | 1:37.5k:1 | 150:150:80 | energy only | 1.0 / 0 |
| ch.3, T5 not beaten | 5:160k:4 | 750:640:320 | energy only | 1.0 / 0 |
| ch.3, T5 beaten (CBlock1) | 5:160k:4 | 750:640:320 | 5:1 | 0.625 / 0.375 |
| ch.4-6 | 5:160k:4 | 750:640:320 | 3:1 | 0.5 / 0.5 |
| ch.4, T6 v2–v3 beaten | 5:160k:4 | 750:640:320 | 2:1 | 0.4 / 0.6 |
| ch.7+ | 4:150k:1 | 600:600:80 | 3:1 | 0.5 / 0.5 |

- Chapter 0 (unknown) falls back to the ch.3-6 row — the guide's longest stretch.
- **The 2:1 row's trigger is the T6 VERSION, and nothing else (user-caught 2026-09-12).** Guide ch.4
  "EXP Spending", verbatim: 3:1 base, "After T6v2, focus Magic to get up to a 2:1 E:M ratio", "After
  T6v4 accs and BB NGU Ygg/EXP, return to 3:1". There used to be a second trigger, a "CBlock2 done"
  proxy reading `hour24Challenge.curCompletions >= 3`, and it was wrong twice over: the guide's EXP
  section never names CBlock2, and CBlock2 is precisely the block that leaves the 24H challenges
  UNfinished ("Finish all challenges aside from the last 5 NoTM and 24 H challenges"), so the count
  was not a proxy for it either. Its only effect was handing 2:1 to players still on T6 v1.
- **The T6 rows read BEATEN versions, not the selected one (user-reported 2026-09-12).** `t6Version`
  used to be `ZoneHelpers.TitanVersion(5)` — the V1–V4 difficulty dropdown in the Beast's zone,
  which never moves on its own. It is now `TitanVersionsBeaten(5)` (0 = none). The same misread
  pinned `ProgressionAnalyzer.Chapter` at 3, which held this table on the post-T5 5:1 row for the
  whole game; see ProgressionAnalyzer.md for the full blast radius.
- Magic locked (all-time `highestBoss < 37`) forces energy-only regardless of phase.
- The guide's ch.1-3 "only buy magic cap for Ygg unlocks/auto-activations" is a manual one-off
  call, so those phases are modelled as energy-only rather than reserving a magic share.
- Not auto-detected: the pre-T7 "energy only once you can BB Normal NGU Ygg/EXP" window.
- A phase change makes `WriteCustomPlan` zero the boxes a phase doesn't want (e.g. the magic
  custom-purchase boxes in ch.1-2). It used to switch `Main`'s `AutoBuyEM` path off for them too —
  that path is **gone** (2026-08-14): it spent EXP with the amounts this class writes, so the two
  were one deciding for the other, and its "Buy E/M (EXP)" checkbox did not gate this class at all
  (user-reported: EXP kept draining while they banked for a digger). `Settings.AdvisorExpBuys` — the
  ADVISOR toggle on the EXP row — is the only control over EXP spent on energy/magic/R3 now. Off
  means EXP banks.

## Resource 3 and the Hack Day phases (guide ch.6 "EXP Spending")

- R3 is a third pool (`Targets.PoolR`), ratio 4/150k/1 = 600:600:80 in EXP-space. Costs come from the
  game's public `energyPurchases` / `magicPurchases` / `res3Purchases` `custom*Cost` methods (R3 is
  the E/M cost x100 000: power 15M, bars 8M, 250 cap 100k); the stat mutation stays ours because
  `buyCustom*` is private. R3 is only in the walk when `res3.res3On`; R3 speed (max 50) is not in
  the ratio — the guide gives an order for it, not a ratio — and stays a manual buy.
- `ExpRatioTables.For(..., HackDayPhase, emFloorReached)`: BeforeHd1 = R3 only; AfterHd1 = E/M walk
  until power 3M/1M and cap/bars at 75 % of the ratio-implied value, then R3 only; AfterHd2 = same
  with 6M/2M. A cap at the hard cap or a gated stat counts as satisfied.
- **Hack Day completion is NOT detectable** — the game has no flag or counter, so `DetectHackDay`
  returns Unknown and the balancer keeps the pre-R3 E/M-only behaviour. Wiring a detector (or a
  user setting) is the only missing piece; the tables and tests are in place.

## Rich Jerks (StatBoostPurchases attack/defense boost)

Permanent multiplier on FIGHT BOSS attack/defense (`Character.cs` attack/defense recompute), not on
adventure stats. 3 EXP per 1 % (`customAttackCost`). Milestones (`RichJerksTargetPct`): 1k % once
T5 is beaten (ch.3 "after getting CCoD from T5"), 10k % from ch.4 (CBlock 2 Prep), 100k % once T6v4
is beaten (ch.4 Evil Prep: "100k Rich Jerks" within the 1m % combined with perks). `BuyTick` fills a
milestone before the E/M/R3 walk, from the same tick budget. Above the milestone nothing is bought:
no clean marginal Fight-Boss-gain-per-EXP comparison against power/cap/bars exists. `Verdict` carries
the current value and the Phase text shows "Rich Jerks now / target".

## Walk-toward-ratio model (replaces "catch the runaway leader")

Each stat has a level `k = ExpSpent / TargetShare`; equal levels = perfect ratio. The old code
anchored targets to the single HIGHEST level — a stat left ahead by an earlier ratio phase (the
early 1:37.5k:1 pours EXP into CAP) demanded an astronomical catch-up lump you can't un-spend.
`BuyTick(fraction)` instead **waterfills** the budget across the lagging stats — raise the
lowest levels to a common water line, never referencing the leader. Converges smoothly; once all
levels are within band (`BalanceTolerance = 0.75` of the max) it degrades to proportional
maintenance. `Analyze()` reports balance % (min/max level), which stats the next chunk feeds, and
the phase the targets came from.

## No dead zone at small banks (user-reported: "nothing is ever bought")

Two guards used to make a small EXP bank permanently unspendable, and they compounded:

1. A flat `budget < 100 → skip`. At 959 banked EXP the then-10 % tick budget was 95, so it bought
   nothing — every minute, forever, while EXP trickled in at +144/hr. Nothing was spent, so the
   bank never grew past the floor either. The floor is now the **cheapest unit actually on offer**
   (`UnitCost`, mirroring `BuyStat`'s rounding: power/bars per-unit, cap 1 EXP / 3 for magic),
   clamped to the bank. Waiting has no upside — a purchase is an instant permanent stat.
2. Even with a budget, the waterfill can slice it into per-stat crumbs that each round down to
   zero units, so every `BuyStat` returned 0. If the walk bought nothing, the whole budget now
   goes to the most-behind stat that one unit is affordable for.

## Game-gate handling

- Custom purchases unlock permanently at **all-time `highestBoss ≥ 17`** (never re-lock on Evil —
  one of the few CORRECT uses of raw highestBoss); magic stats at ≥ 37.
- **Energy** CAP buys are game-gated until `capEnergy` crosses 100k — a gated stat is
  `Buyable=false` and **excluded from the balance** so it can't pin the percentage at 0 forever.
  Magic CAP has no such gate (`MagicPurchases.tryCustomCap`/`buyCustomCap`).
- `BuyStat` replicates the game's `buyCustom*` math exactly per stat: unit costs, cap rounded to
  250s, `hardCap()` clamp. `WriteCustomPlan` mirrors the reachable deficits into the game's
  custom-purchase boxes so the game's own "Buy ALL Custom" button reflects the walk.

Consumers: OptimizationAdvisor EXP row; AdvisorApply `exp` auto-toggle calls `BuyTick(0.60)` once a
minute — a big slice, since the waterfill never overshoots the ratio at any budget and banked EXP
earns nothing; the remaining 40 % is a reserve for manual buys.
