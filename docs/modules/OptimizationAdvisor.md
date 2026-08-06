# OptimizationAdvisor (`Managers/OptimizationAdvisor.cs`)

The ADVISORY layer — "gaps + actions". Every `Rec` answers "where do we need to GO" (target +
action), never a bare status readout (the status strip covers "where we are"). `Optimal=true`
rows collapse into one "✓ optimal" dashboard line; `AutoKey` names the AdvisorApply toggle that
can act on the row. Read-only itself; `Analyze()` cached 2 s; every rec individually guarded.

## The titan kill ladder (core public API)

- `NextObjective()` — the first titan+version at THIS difficulty not yet auto-killed, with a
  STAGED requirement (user's kill ladder): never killed → guide's MANUAL first-kill stats;
  killed → guide's IDLE stats until met; then the game's exact AK stats (HP-regen gate from T4
  up). Tables in `TitanTables` (Unity-free, shape-tested — finding #33).
- `DifficultyMaxTitanIndex()` — Normal caps at T6 (idx 5), Evil T9 (8), Sadistic T14 (13).
  **History**: the old NextTitanIndex (highest AK'd + 1) ignored difficulty AND versions — on
  Normal with T6 partially AK'd it chased T7 (unreachable content); whole rebirths were wasted.
- `VersionKilled`: **the `titan6V1Kills..V4Kills` save fields are DEAD** — the game never
  increments them (only ImportExport zeroes them). T6 per-version kills = achievements 148–151
  (Beast v1–v4); T7+ keep NO per-version record (spawn-version proxy). User report: chip stuck
  on "FIRST KILL (v2)" after a confirmed v2 kill.
- `ProjectedBestGear()` — best/current Power and Toughness objective-score ratios as attack/def
  projection multipliers (attack ~linear in the Power stat); cached 120 s (two optimizer runs).
- `BossUnlockCeiling()` — highest boss that still unlocks anything at this difficulty; past it,
  boss pushing is pure EXP (drives the "NUMBER ritual" row and the diggers' `ceiling0`).
- `GoldStarvedForAugs/Diggers(c, factor)` — gold-sink starvation checks; `factor` gives callers
  hysteresis (adventure router farms gold until TWO upgrades are affordable). Digger upgrade cost
  = `baseGoldCost × growthRate^maxLevel` (decomp AllGoldDiggerController).

## Digger/beard set computation (consumed by AdvisorApply)

`CurrentDiggerSet()` — the most rule-dense method in the codebase. Structure: pick a base order,
then apply the **DIGGER LAWS** (user-corrected semantics, 3rd revision) as overrides in priority
order:

- **Hybrid membership**: a MANUAL profile naming diggers for this phase makes that list the
  candidate POOL — the advisor only reorders/levels within it (poolFilter strips law-introduced
  filler). This keeps NGU diggers off before the profile's ALLNGU phase. AutoProfile has no
  digger breakpoints → advisor's own fill-every-slot set (guarded on `!AutoProfile` so a stale
  manual profile can't leak in).
- **Laws** (applied bottom-up, later = stronger): Stats(2) has priority only while stats gate
  progress (boss push / floor-restricted `!ceiling0` / challenge); Blood(10) needs a live ritual
  caster (`BloodPlanner.BloodMatters()` — ask the owning planner, never string-match another
  module's output; the old AutoTokens string-match was wrong three ways, see inline comment);
  DC(0)/PP(8) picked by VENUE (titan window/gear hunt → DC in + PP benched; ITOPOD → PP in + DC
  benched — ITOPOD rolls are FLAT, no DC scaling); Adv(3) always leads; **an active gear hunt
  outranks even Adv — DC(0) first, applied last** (user-caught: at one digger slot the Adventure
  lead pushed DC out of `Take(slots)`, so the hunt farmed with zero drop chance). Titan window
  does NOT outrank Adv — only the hunt does.
- **Titan window = 60 s** — sized to the machinery, not the event: digger applier ticks every
  30 s, titan gear lock engages < 20 s before spawn; 60 s guarantees exactly one swap pass lands
  ahead of the lock (user-reported: diggers swapped ~8 min early and lingered).
- Challenges: with AutoProfile the advisor drives diggers/beards even in challenges (profile's
  one-shot at rebirth hit 0 gold and never retried — user-caught in a BASIC challenge on the
  Evil climb); with a manual profile, challenge mode returns null (profile owns the set).
- `NGU MARATHON` segment gets a growth-multiplier-first order {4,5,11,6,7,8,1,0}.
- Beards cost nothing → always fill every slot; Golden (6) needs Troll ≥ 7.

## Recs produced (one try/catch each)

Power (AK gap, push mode only) · Gear (re-optimize headroom from ProgressionAnalyzer) · Wandoos
(exact comparator) · Adv Training (wish-190 auto / Wandoos-as-power target ≈ current×1.25 rounded
to 500 / push) · Diggers (missing + affordability `log(gps/base)/log(growth)` + recap headroom) ·
Beards · Yggdrasil + Perks + Quirks (SpendPlanner; **"Bank X — next guide buy at ch.N" instead of
"plan complete"** when steps are chapter/difficulty-gated — user-reported both rows) · Beard perm
(shavings `floor(sqrt(level)×timeFactor)`, timeFactor capped 8; sub-1h rebirth banks NOTHING) ·
EXP (ExpBalancer ratios) · Gold (titan gold banking) · Blood (BloodPlanner) · ITOPOD beacon ·
NGU x/hr row · Boss-ceiling row · LSC opportunity.

**Wandoos-as-power contexts**: challenge block, NORB (Number mult dead), NOAUG, or gold-starved
for augs — AT-3/AT-4 (Wandoos E/M) become the power source and the AT rec switches accordingly.
