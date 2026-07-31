# NGU Idle — Strategy Knowledge Base

Distilled from the community guide (https://sayolove.github.io/ngu-guide) plus the Boost Almanac and
PP/EXP-income sheets. This is the **source of truth** for authoring stage/goal profiles, choosing gear
optimizer objectives, and driving the status HUD's "what you're working toward" targets.

External references:
- Guide: https://sayolove.github.io/ngu-guide/en/intro/
- Gear Optimizer tool: https://gmiclotte.github.io/gear-optimizer/#/ (our native optimizer reimplements its scoring)
- Boost Almanac (Google Sheet): id `1UyOPvZ_Gen02xfJZuPGnOQNVETxoQXlYJ9ObHmmDWRI`
- PP/EXP income (Google Sheet): id `1v9yA1Cv8W7OS1Vo_3LU3rHVBsPXaFRzP4T7Di2ZT3YY` (gid 1550539240)
- Wiki: https://ngu-idle.fandom.com/wiki/NGU_Idle_Wiki

Deep dives that expand a single line of this file:
- `AUGMENTS.md` — augment boost/cost math, tier crossovers, energy split, what `BestAug` prices

> Note: these Google Sheets are not machine-fetchable here; mine them opportunistically for exact boost
> values and rebirth-length breakpoints during implementation.

---

## Progression: 8 chapters

The game is organized as 8 chapters gated primarily by **rebirth difficulty** (Normal → Evil → Sadistic)
and **Titan** kills (T1–T12). Each chapter has characteristic energy/magic allocation, fruit order,
challenge sequence, and loadout goals. Detection heuristic in `Managers/StageDetector.cs` uses
`Character.settings.rebirthDifficulty` + `Character.highestBoss` (boss thresholds are approximate — see the
table at the bottom; refine with titan-version reads if needed).

### Ch.1 Start-HSB (Normal, pre-T1)
- 30-minute rebirths as often as possible (farm boss EXP, cut skill costs). Extend to 1h after Boss 58.
- Basic Training: Attack (auto-syncs Block); progress all tiers to max speed (~25 min) to unlock Advanced Training.
- Energy spend order: Total ESpeed 25 → Base EBars 4 → Total ESpeed 50 → Base ECap 300k; then ratio **1 : 37.5k : 1** (Power:Cap:Bars).
- Augments unlock Boss 17: buy the most expensive augment finishable in 30 min (only `base_i` matters
  at these level counts; the crossovers move as gold grows — see `AUGMENTS.md`).
- Adventure sets: Tutorial → Sewers (B7) → Forest (B17) → Cave of Many Things (B37) → The Sky (B48) → HSB (B58).
- Time Machine unlocks Boss 30 (wear Gold Drops gear in furthest zone to set gold, don't level yet).
- Blood Magic Boss 37 (Poke Yourself / Blood Number Boost).
- ITOPOD after Pissed Off Key: climb to floor 100 (~16 PP). Perks: first 5 Newbie (0–4) → 2× Instant AT Levels (18) → alternate Gen Energy Power I (6) / Gen Energy Cap I (8).
- **Milestone:** manually kill T1 (Gordon Ramsay Bolton) ~1350/1350 P/T (2300/2100 for idle).

### Ch.2 T1-Mega (Normal, T1→T4)
- Heavy Time Machine + augment upgrades. Progress adventure zones. Fruit: FoG 2 → FoPa 1 → FoA 1 → Pom 1 → FoPa 2 → FoG 4 → Pom 3 → FoG 10.
- After Boss 100: start T4 puzzle; do a micro challenge-block (5 basic + 1 24h).
- **Milestone:** long rebirth to defeat T4, farm Mega gear.

### Ch.3 T4-BAE (Normal, beards)
- Switch to 24h rebirths for beard farming (BEARd > Neckbeard > Beard Cage).
- Resource split: **energy half ADV / half DC; magic → YGG**.
- Challenge order: T4 → Mini-CBlock → Beardverse → T5 → CBlock1 → BDW/BAE.
- Fruit: FoG 10 → Pom 5 → (FoPa1 + FoA1 + FoK1 + FoL1) → Pom 10 → FoL 5.

### Ch.4 T6 (Normal, mid scaling)
- Long rebirth until T6 weapon drop. Energy:Magic ratio 3:1, then 2:1 after CBlock2.
- Manual major quests; idle minor quests. Farm chocolate gear at ~1.3T power.

### Ch.5 Evil-IDP (Evil begins — IDP = **Interdimensional Party**, Boss-166 unlock)
Sourced verbatim from sayolove ngu-guide `en/chapters/chapter-5` (fetched 2026-07-17). Entering Evil
**re-locks** the progression systems (TM/AT/Wandoos/etc.); you re-climb to re-unlock them, permanent
NGU/aug bonuses carry the power. Two sub-modes:

**A. Early climb (30-min rebirths).** Start ONE Basic challenge ASAP (free entry; don't do more — you
don't need the adventure stats). 30-min rebirths using Fruit-Activation perks to climb; repeat until you
stop gaining ~100× number/rebirth (usually Boss 60–80), then push to Boss 125. Re-unlock ladder:
EV (Evilverse) B58 → PPPL (Pretty Pink Princess Land) B100 → T7 (Greasy Nerd) B125 + puzzle →
Meta Land B158 → T8 path + IDP B166. Snipe order: EV exploder → EV idle (accs) → PPPL exploder →
PPPL idle (accs) → T7.

**B. The 24h rebirth (once T7-capable) — four phases (REPLACES the Normal "1h TM / 1h AT / 22h NGU"):**
1. **0:00–0:30 Time Machine** — optimize 2 respawn, lock Voodoo Doll, rest in TM ("as many TM levels as
   you can in 30 min"; 0–2 levels early).
2. **0:30–3:00 Augmentation** — swap weapon to BDW gun; run best augment to the 3h mark (Gear Optimizer).
   Magic: cast Counterfeit Gold once, then Blood Number for the remainder. Use Blood Digger.
3. **3:00–23:00 Normal NGU + Advanced Training** — BB Normal NGUs *while* raising AT for objectives; wear
   BDW Head on AT runs; Wandoos-AT until cheap to run Wandoos 98. **AT with <40% of Energy cap.** First RB:
   100k AT Block, then AT Power until adv power can snipe EV exploder (5–7T pow); later RBs AT P/T until
   idling EV for accs. **At Boss 115/120, BB AT P/T the whole rebirth to prep T7.** (Once Normal NGUs hit
   BB they're time-gated — only more run-time helps.)
4. **23:00–24:00 Evil NGU** — switch to Evil NGUs (exploit temp Beard-Cage levels); focus NGU Augments +
   NGU Ygg/EXP toward T7; eat Fruit of Power A + stat digger to push bosses. **+1 extra hour of Evil NGU
   per T7 version defeated** (1h post-T7v1, 2h post-T7v2…). Evil NGUs multiply Normal NGUs. (At Boss 124 at
   RB end, dump FoK EXP on Rich Jerks.)

**Ratios / spend:** pre-T7 buy ONLY Energy once you can consistently BB Normal NGU Ygg/EXP. Post-T7 back to
**3:1 E:M** and **5 : 160k : 4 Pow:Cap:Bars**. **R3: don't buy** (except speed→5.1 if flicker bothers you).
Hacks (= R3-NGUs, unlocked via Incriminating Evidence, a T7 drop): post-T7 run A/D Hack until CBlock 3,
then Adventure Hack.

**Titan targets** (Choco gear can kill T7): T7v1 manual 140T/90T, idle 300T/200T, AK 500T/250T/5T;
T7v2 3.2q/1.6q P/T; T7v3 55q/35q P/T. Meta snipe 26q/12q, idle 45q/31q. IDP snipe 250q/110q, idle
480q/310q. T8 LRB shape: Idle Meta → Snipe IDP → Idle IDP → 1-shot Max Meta → Idle IDP until T8 kill.

**CBlock 3** (finish all Normal challenges first — RB to Normal, wait 3 min, start): All 100-Level, No-Aug,
Basic; Troll 1 (mandatory) + 2 (recommended); NoNGU 1, NoTM 1, NoRB 6, Blind 1.

**Beards (pre-T7):** Fu > Golden > Reverse > Cage > BEARd > LadyBeard > Neck.
**Quirks:** EM Pow/Cap 1 → Beard/AT Banks 1 → Beasted Boosts 2 → Adventure Quirk (LRB to T8).
**Perks:** Beard/AT Banks 3+4 → Fib 1 → EM Pow/Cap/NGU 2 (L10) → Fib 3 → Welcome-to-Evil (~B120) → EM…
until CBlock3 → Beard/AT Banks 5 → EM…until easy Normal-NGU BB → Fib 34 → finish EM → Energy Bars 2 →
Adventure Perk until T8 → Magic Bars 2 when cheap.
**Yggdrasil:** early FoR 24 → GuffA 12 → GuffB 1 → Quirk 4; mid-late Melon 24 → Quirk 12 → GuffA 24 →
Quirk 24 → PowerD 24 → GuffB 24 (Melon 8 out-seeds Pom 24; poop Melon 8+).
**AP tiers:** Yellow&Red Heart → Acc Slot 1&2 → Green Heart → Grey Heart (225k, save from Evil entry, buy
post-T7) → Beard Slot 5 → Blue Heart → MacGuffin Slots 1&2 (post-CBlock3); reserve 450k for T8; then
Orange Heart, Extended Quest Bank, Beard Slot 6.
**MacGuffin priority:** Adv > EM Pow > EM Cap > EM NGUs > Drop > EM Bars > Gold > Augs > Stats > EM Wandoos
> Number > SMART/SEXY.
**Evil multipliers (game formulas — already handled via the game's own difficulty methods):** Att/Def
÷1e30; Aug ÷2.5e12; TM ÷1e12; BM ÷1e9; Wandoos ×1e6 per OS; boss bonus 1.5^boss (vs 2^boss Normal);
drop chance cube-rooted.

### Ch.6 T8-JRPG (Evil, R3 sink)
- Start buying R3 upon T8. Daycare-focused loadout for looty/pendant progress.
- Sequence: snipe Typo set → CBlock4 → Hackday 1 → buy E/M to 3M/1M power → resume R3 → evil NGUs after blackbeard.
- Endgame: farm Typo → snipe Fad → max Typo → farm Fad → snipe JRPG → max Fad → long RB to T9.

### Ch.7 T9 (Evil/late)
- 24 manual kills for AK. Cards: tag Adv/Hack/Wish, cast only Meh+, yeet rest.
- Energy:Magic 6M/2M → 24M/8M; continue R3.
- Sequence: max set → nuts → BEUC → BEUC CBlock → BEUC Hackday (250+ R3 power) → snipe Rad at v3 → long RB to Rad set + 50+ soul points.
- **Milestone:** kill v4 24×, reach Boss 300.

### Ch.8 Sadistic (endgame)
- Entry challenge: all Basics, No Aug, 100 Lvl, No Equip, No RB, first two Trolls.
- Buy Fertilizer; 23h rebirths with Muffins (aim 2 rebirths/muffin). Hackday whenever ≥1.3× adventure multiplier; alternate with snugday.

### Rules of thumb
1. Scale rebirth length to highest fruit tier once T2 unlocks.
2. Post-T4 consolidate: 1h Time Machine, 1h Advanced Training, 22h NGUs.
3. Snipe upcoming adventure sets before maxing current ones when zones are close.
4. Energy:Magic ratio transitions: 1:37.5k:1 early → 3:1 T6 → 2:1 post-CBlock2 → 6M:2M T9 → 24M:8M late T9.
5. Delay R3 until T8; evil NGUs only after enough progress; prioritize manual quests early in rebirths.

---

## Gear Optimizer objectives (per goal)

The guide's GO advice: **early game optimize "Power"; mid/late run multiple loadouts, each focused on ONE
priority, plus a couple Respawn items** (Respawn matters more as systems scale). Our native optimizer
(`Managers/GearObjectives.cs`) exposes these objectives; each maps to game stat spec(s):

| Goal | Objective(s) | Notes |
|---|---|---|
| Adventure push | Power / Toughness / Adventure | Ch.1–2 primary |
| Respawn | Respawn | always run ≥1 respawn item late (TopRespawn toggle) |
| Time Machine | Time Machine (E/M cap+power) | 1h/rebirth post-T4 |
| Advanced Training | Advanced Training (AT Speed) | 1h/rebirth post-T4 |
| Augments | Augments (Aug Speed) | |
| Beards | Beards (Beard Speed) | Ch.3 farming |
| NGUs | Energy NGU / Magic NGU / NGUs | 22h/rebirth post-T4 |
| Drops / Gold | Drop Chance / Gold Drops | Adv/DC split |
| Yggdrasil | Yggdrasil (Seeds>EXP>Gold>AP) | harvest set |
| EXP | Experience | |
| Wishes / Hacks | Wishes / Hacks | later chapters |
| Daycare | Daycare | Ch.6 looty/pendant |
| Cooking | Cooking | |

NGU allocation rule (from GO NGU tab): run an NGU while gaining **>1.05×/hr**; run **Respawn** when
**<0.95×/hr**; otherwise split **Adventure/Drop Chance** and **Yggdrasil/EXP**.

**PP has no gear spec** (perk points come from rebirths, not gear) — it cannot be a gear objective.

### NGU lists (match injector NGU tokens)
- Energy NGUs: Augments, Wandoos, Respawn, Gold, Adventure-a, Power-a, Drop Chance, Magic-NGU, PP.
- Magic NGUs: Yggdrasil, Exp, Power-b, Number, Time Machine, Energy-NGU, Adventure-b.

---

## Stage detection map (heuristic — `Managers/StageDetector.cs`)

| Difficulty | Highest boss | Chapter |
|---|---|---|
| Normal | < 58 | Ch.1 Start-HSB |
| Normal | 58–99 | Ch.2 T1-Mega |
| Normal | 100–128 | Ch.3 T4-BAE |
| Normal | ≥ 129 | Ch.4 T6 |
| Evil | < 150 | Ch.5 Evil-IDP |
| Evil | 150–249 | Ch.6 T8-JRPG |
| Evil | ≥ 250 | Ch.7 T9 |
| Sadistic | any | Ch.8 Sadistic |

Boss thresholds are approximate placeholders — tune against real play / titan-version reads. Detection is a
**hint only**; profile switches are always user-confirmed.

---

## Existing sample profile library

`NGU/sampleprofiles/` is already grouped by difficulty + goal and is the base for the curated stage/goal
presets (Phase 1): `Normal/` (24hr, 24hr-AdvDC, 24hr-PAWG, LRB-*, CBlock*, BeastRB, Miniblock*),
`Evil/` (EvilStart, 24hr-*Evil, CBlock*, HackDay, RAD*, LRB-*), `Sadistic/`, plus top-level cblock*/24hr.
Phase 1 will re-express these with objective-based gear timelines so loadouts auto-optimize.

## Guide spend orders (sayolove ngu-guide, ch2-5) — implemented in Managers/SpendPlanner.cs

**ITOPOD perks (PP), Normal:** Newbie perks -> Generic Energy Power/Cap I -> Bonus Titan EXP x1 (online-AK EXP bug) -> What a Crappy Perk -> A Digger Slot -> Boosted Boosts I (10) -> Faster NGU Energy (until CBlock1) -> post-CBlock1 Ygg block: I want your seeds / First Harvest / FoK sucks 1+2 -> (ch4) Generic Magic Power/Cap I, Faster NGU Magic, BB1 max, E/M Bar I, AT/Beard/TM Level Banks I+II, Inventory I+II, Wandoos Lover, Bonus Boss EXP, BB2.
**Evil (ch5, PARTIAL - verify names in-game at Evil):** Beard/AT Banks 3+4 -> Fib 1 -> EM Pow/Cap/NGU 2 to L10 -> Fib 3 -> Welcome to Evil (~boss 120) -> EM until CBlock3 -> Banks 5 -> EM until easy normal NGU BB (2:2:1) -> Fib 34 -> finish EM -> Energy Bars 2 -> Adventure through T8 -> Magic Bars 2 when cheap.

**Beast quirks (QP):** ch4: Baby's First Quirk: Adventure (300 QP, +25% adv). Evil ch5: finish EM Pow/Cap 1 (Baby's First set) -> Beard/AT Banks 1 -> Beasted Boosts 2 -> Adventure Quirk during T8 LRB.

**Yggdrasil tiers (seeds):** ch3: FoG 10 -> Pom 5 -> FoK 1 + FoL 1 -> Pom 10 -> FoL 5. Post-TC3 (cap 24): FoG 24 -> Pom 24 -> FoK 24 -> FoL 24 -> FoPa/FoA 24 -> FoAP 24 -> FoPb/FoN 24 -> FoR 24. Eat/harvest: harvest FoG early (seeds), eat when diggers capped; harvest fruits before FoL until L12+ then eat; poop Pom always, others at max. Evil ch5: FoR 24 -> GuffA 12 -> GuffB 1 -> Quirk 4; later Melon 24 -> Quirk 12 -> GuffA 24 -> Quirk 24 -> PowerD 24 -> GuffB 24 (Melon 8 out-seeds Pom 24; poop Melon 8+).

## CORRECTION (EXP ratios — verified against guide verbatim + Blaze Ratioz)
"Split EXP evenly into energy/magic (3:1 E:M base)" means: EXP split is 1:1 (EVEN); the 3:1 E:M is the
resulting PURCHASED-VALUE ratio (magic units cost exactly 3x energy: pow 450 vs 150 EXP/unit, cap 3 vs 1
per 250, bars 240 vs 80). 5:160k:4 pow:cap:bars is likewise a UNIT/value ratio. Blaze's Ratioz tab
compares stat VALUES, same convention. Post-T6v2 the guide targets a 2:1 VALUE ratio (EXP 2:3 toward
magic) until T6v4 accs + BB, then back to 3:1 values into Evil. R3 joins the ratio at Evil (E:M:R3, TBD).

---

## Evil-era (difficulty) correctness — standing checklist

Audit 2026-07-17: the value FORMULAS are Evil-correct — they delegate to the game's own difficulty-aware
methods (`baseEnergyTime`/`baseMagicTime`, `baseSpeedDivider`, `*UpgradeSpeedDividers`, `sadisticDivider`,
`addR3`, `getTotalStatBoost`, `AugTimeLeft`, `hitTarget`) or branch on `rebirthDifficulty`. The recurring
Evil bugs came from FOUR cross-cutting patterns, not the math. Every new advisor calc/gate must clear all:

1. **Boss reads.** `ZoneHelpers.CurrentHighestBoss(c)` for progression/stage/climb/titan gating —
   `highestBoss` is NORMAL's all-time max and does NOT reset on Evil (evil=`highestHardBoss`,
   sadistic=`highestSadisticBoss`). `highestBoss` is ONLY for permanent-feature unlocks.
2. **System re-locks.** Entering Evil re-locks ONLY four systems each rebirth until their unlock boss is
   re-killed: **Augments, Advanced Training, Broken Time Machine, Blood Magic** — gate those on live
   `buttons.<x>.interactable`, never a boss number. Everything Wandoos-and-below (Basic Training, Wandoos,
   NGUs, magic resource, custom EM purchases) is ALWAYS unlocked.
3. **Gating fires on Evil.** Evil re-climbs from boss 1, so segment/condition gates must handle the
   low-boss re-climb (e.g. Wandoos-AT stops were NGU-MARATHON-only; the climb never hits marathon → stale).
4. **Formulas / thresholds.** Prefer the game's difficulty-aware methods; if reimplementing, branch on
   `rebirthDifficulty`. Evil scales are enormous vs Normal — Att/Def ÷1e30, Aug ÷2.5e12, TM ÷1e12, BM
   ÷1e9, Wandoos baseTime 1e9→1e21 (×1e6/OS), boss bonus 1.5^ (vs 2^), drop chance cube-rooted — so
   RE-CHECK any Normal-tuned magnitude threshold (%, "1% of cap", ratio): magnitudes diverge and a
   heuristic that read "cheap/done" on Normal can flip on Evil.

**Evil farm data (closed 2026-07-17):** GearFarmAdvisor Evil-era zones (20-43) were already sourced from
LootDrop (correct chances + caps). BoostFarmAdvisor Evil zones now sourced too (per-roll caps added from
LootDrop.zone{N}Drop; the almanac's zone-29 boost chance 1.5E-6 was the in-game tooltip's typo — the drop
code is 1.5E-5, matching GearFarm). Remaining approximation: BoostFarm models one of the two boost rolls
per zone (~2x low on absolute value, but Evil-zone ranking preserved). The drop FORMULA (cube-root for
Evil/Rooted zones) was already correct in both.
