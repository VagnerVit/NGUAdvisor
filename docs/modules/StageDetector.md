# StageDetector (`Managers/StageDetector.cs`)

Heuristic progression-stage detection: difficulty + `ZoneHelpers.CurrentHighestBoss` → the
guide's 8 chapters. A HINT only — never changes anything automatically; feeds the STAGE HUD cell
and a non-binding `SuggestedProfile` (auto-installed `Goal-*` preset name).

## TWO CHAPTER ENGINES — do not merge them

This `Stage.Chapter` is the coarse **boss-threshold** chapter. It intentionally DIVERGES from
`ProgressionAnalyzer.Chapter` (the canonical, **titan-kill**-based chapter used by HUD stage,
perk plan, profile recommendation). Two consumers NEED the boss-anchored version and are why this
engine is retained: ChallengeOverlay's boss-gated EVIL CLIMB / AUGMENTATION segment logic, and
LevelPlanner's NGU-track switch. Example expected divergence: Evil Boss 200 with T7 unbeaten →
here Ch.6 (boss < 250), there Ch.5 (T7 not beaten). **Never swap one Chapter for the other.**

## Thresholds (approximate, tunable — see docs/NGU-KNOWLEDGE.md)

Normal: <58 Ch.1, <100 Ch.2, <129 Ch.3, else Ch.4. Evil: <166 Ch.5 (spans the whole re-climb
THROUGH the IDP/T8 unlock — the old <150 boundary cut it short; sub-label tracks the re-unlock
ladder climb→EV(58)→PPPL(100)→T7(125)→Meta/IDP), <250 Ch.6, else Ch.7. Sadistic: Ch.8.

All game reads guarded → `Unknown` when the game isn't ready.
