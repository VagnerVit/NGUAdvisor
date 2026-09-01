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
