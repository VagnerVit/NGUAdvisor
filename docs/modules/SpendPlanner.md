# SpendPlanner (`Managers/SpendPlanner.cs`)

Guide-ordered spend plans for ITOPOD perks (PP), Beast quirks (QP) and Yggdrasil fruit tiers
(seeds) — the community guide's chapter orders (recorded in docs/NGU-KNOWLEDGE.md) as ordered
`Step[]` lists. Next buy = FIRST step that is unlocked, below target, chapter-allowed,
cap-allowed and difficulty-allowed. Chapter comes from `ProgressionAnalyzer` (canonical).

## Name matching — deliberately name-based, with drift logging

Steps match against the game's LIVE name lists so ID drift between game versions can't mis-buy:
exact (trimmed, case-insensitive) → punctuation-insensitive exact (`Normalize`: letters+digits
only — debug.log caught the quoted `"Fruit of Knowledge sucks 1/5"` steps never resolving) →
unique contains. **Ambiguity refuses the match rather than mis-buying.** A step whose name never
resolves is skipped AND logged once ("name drift?") — silent skips looked like "plan complete"
while the guide still had buys queued (user-reported).

## Semantics that fixed real bugs

- `Chapter()` returns **0 when stage detection is unknown** → every chapter-gated step skips →
  nothing is bought. The old "unknown = chapter 1" default made a transient detection failure
  read as "plan complete".
- **A step scheduled in the WRONG chapter is silent the same way (user-reported 2026-09-13).** Every
  quirk but the Adventure baby quirk carried `MinChapter 5` while the guide puts them in ch.4, so a
  player sitting on 144 QP in ch.4 got no purchases and a panel reading "banking for X (chapter 5)" —
  the FUTURE-buy card, which looks like a recommendation and is not one. Guide ch.4 Questing, verbatim:
  "Quirks: Baby Quirks, Beast's Seed, Beasted Boosts 1, MPow/MCap 1, Gold, EPow/ECap 1"; ch.5 "Quirk
  Order": "Finish EM Pow/Cap 1 / Beard / AT Banks 1 / Beasted Boosts 2 / Adventure Quirk in LRB to T8".
  Four of that ch.4 list (Beast's Seed, Gold, and the Generic E/M Pow/Cap I quirks) were missing from
  `QuirkPlan` entirely. Because `MinChapter` is a FLOOR and steps are sequential, leaving the Generic I
  steps ahead of the banks is what implements ch.5's "Finish EM Pow/Cap 1" — no duplicate step needed.
- **Step names are SCENE data and cannot be checked from the decompile.** `FindByName` logs one miss
  per step to `debug.log` and skips it, so a typo costs a step silently. Read the live list first:
  the state export's BEAST QUIRKS / ITOPOD PERKS sections print every entry with its index and cost.
- **A WRONG chapter is as silent as an unknown one.** The whole `QuirkPlan` is `MinChapter` 4 or 5,
  so while `ProgressionAnalyzer.Chapter` was pinned at 3 by the `titan{N}Version` misread, this
  planner bought no quirk at all and QP banked forever, with no error anywhere (user-reported
  2026-09-12, fixed in ProgressionAnalyzer — see that doc). When a spend plan "does nothing",
  check the chapter it is being handed before looking at the plan.
- **A fruit step is gated on the CAP, not only the chapter** (`Step.MinCap`). The guide
  schedules the tier-24 push for ch4, but the game gate is `AllYggdrasil.capTier()` — 10 until
  Troll Challenge ×3 completions, then 24. The two come apart: a player can hold the cap while
  still pre-T6, and gating on the chapter alone stalled the plan with seeds banked and nowhere
  to spend them (user-reported, 2026-09-01: cap 24, ch3, 4.28K seeds idle). Those steps are now
  `MinChapter 3 + MinCap 24`. Do NOT drop `MinCap` and lower the chapter alone — without the cap
  gate, `Math.Min(target, cap)` would start a tier-10 push on Knowledge / Power α before TC3,
  which the guide schedules later.
- `NextPerkPlanned` / `NextQuirkPlanned` / `NextFruitPlanned`: the first buy still QUEUED but
  chapter/cap/difficulty-gated
  — what banked PP/QP/seeds are FOR. On Normal the guide's only pre-Evil quirk is Baby's First:
  Adventure (ch.4), so the plan idles for whole chapters; the advisor says "bank for X" instead
  of "plan complete". These REPORT the gates, they do not apply them — a step blocked by any of
  the three still gets named. `SpendOverview.Banked()` reports the cap gate FIRST: the cap is the
  hard game gate, the chapter is only the guide's schedule, so naming the chapter over a cap
  block points at the wrong cause.
- Boss-gated ch5 perks (Welcome to Evil B125, Adventure Boost III B150) are placed LAST in the
  plan — there is no perk boss-req field to guard on, so a still-locked one must never stall
  earlier steps.
- Quirk name id 6 carries a trailing space in game data — hence the `?.Trim()`.

## Buy execution mirrors the game's own click path (verified vs Assembly-CSharp)

`BuyPerks/BuyQuirks` replicate `doLevelUp(id)`: deduct points → increment level → `doEffect(id)`
(the derived-stat recompute — NEVER skip it). Only the UI-refresh calls (showTooltip, updateText,
changePage) are skipped — they carry no game state; no achievement/unlock hooks exist in that
path. `BuyFruitTier` mirrors `FruitController.upgrade()`: deduct seeds + `maxTier++` — that game
method has NO doEffect at all. Fruit cost = `baseSeedCost × ceil((tier+1)²)`; special-fruit
unlock gates: Numbers = Troll ≥ 5, Rage = itopodOn, MacGuffin = achievement 145.

Evil+ plan entries are intentionally partial — guide names need in-game verification when the
user reaches those chapters (ch5 perk names verified vs Blaze Rkkz).

Consumers: OptimizationAdvisor rows; AdvisorApply auto-buy toggles (`perks`, `quirks`,
`yggbuys`).
