# SpendPlanner (`Managers/SpendPlanner.cs`)

Guide-ordered spend plans for ITOPOD perks (PP), Beast quirks (QP) and Yggdrasil fruit tiers
(seeds) — the community guide's chapter orders (recorded in docs/NGU-KNOWLEDGE.md) as ordered
`Step[]` lists. Next buy = FIRST step that is unlocked, below target, chapter-allowed, and
difficulty-allowed. Chapter comes from `ProgressionAnalyzer` (canonical).

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
- `NextPerkPlanned` / `NextQuirkPlanned`: the first buy still QUEUED but chapter/difficulty-gated
  — what banked PP/QP is FOR. On Normal the guide's only pre-Evil quirk is Baby's First:
  Adventure (ch.4), so the plan idles for whole chapters; the advisor says "bank for X" instead
  of "plan complete".
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
