# PP advisor panel

Date: 2026-08-10
Status: draft for owner review

Surfaces the perk-point plan that already exists in `SpendPlanner`, and answers the question the
existing code does not: **when does the next perk land at the pace I am actually going?**

Advise-only display. The auto-buy path already exists (`AdvisorApply`'s `perks` toggle) and is not
touched.

---

## Scope correction from the original request

The original ask was "what's needed to reach 3 hours of farming PP". The owner replaced that with an
estimate tied to the **next actual purchase** instead of a fixed horizon (2026-08-10). A three-hour
window answers "what will I manage", which is not actionable; "the next perk costs N, you are M short,
≈T at your pace" answers "what is next", which is.

## Everything needed already exists — nothing is recomputed

| Input | Source | Notes |
|---|---|---|
| PP balance | `Main.Character.adventure.itopod.perkPoints` | as `SpendPlanner` already reads it |
| Next perk, its cost | `SpendPlanner.NextPerk()` → `Buy { Known, Id, Name, CurLevel, TargetLevel, Cost, Affordable }` | guide-ordered, chapter- and difficulty-gated |
| Next perk that is queued but GATED | `SpendPlanner.NextPerkPlanned()` → `PlannedBuy { Known, Name, Cost, MinChapter, DifficultyGated }` | this is what banked PP is *for*; the module must show it, or a chapter-gated plan reads as "complete" |
| **Measured** PP rate | `GrowthTracker.Rate(s => s.GPp, window, perRun, out perHour)` | `GPp` is cumulative **gains**, so spending PP cannot depress the rate (`GrowthTracker.cs:8`, a standing user rule) |
| **Modelled** PP rate | `ItopodFarmAdvisor.ForMode(mode).PpPerSecond` | what the pod would pay at the advisor's floor for that mode |

`SpendPlanner` is not modified. This module reads it.

## The two rates, and why both appear

They answer different questions and must never be blended into one number:

- **Measured** — what you are actually earning right now, whatever you are doing. This is the headline,
  because the owner asked for "at the current pace".
- **Modelled** — what the pod would pay if you farmed it at the advisor's floor in a given combat mode.
  This is the "would switching help?" line, and it is labelled with its assumption (floor and mode), not
  presented as a prediction of what will happen.

**Fallback rule:** `GrowthTracker` samples only since load, so shortly after a reload or rebirth there is
no measured rate. The panel then shows the modelled figure and says which one it is using. It must never
silently substitute one for the other — a modelled ETA presented as measured is a wrong answer wearing
the right label.

## What the panel shows

```
PERK POINTS
  Banked: 1.23M PP
  Next: "Faster NGU Energy" 4 -> max · cost 250K · AFFORDABLE NOW
  ...or, when short:
  Next: "Faster NGU Energy" 4 -> max · cost 2.5M · short 1.27M · ~3h 20m at 380K PP/hr (measured, 30m window)
  Queued (gated): "Welcome to Evil" · needs chapter 5
  ITOPOD would pay 520K PP/hr (Offensive, floors 92-92)
  Order: community guide perk plan (docs/NGU-KNOWLEDGE.md)
```

Rules:
- A rate of zero or an unknown rate yields **no ETA at all**, not "infinity" and not a zero.
  `SpendPlanner.NextPerk()` returning `Known == false` yields "plan complete" only when
  `NextPerkPlanned()` is also unknown — otherwise it says what is banked for. That distinction already
  cost a user-reported bug once (`SpendPlanner.md`).
- Numbers through `NumberFormatter.Abbrev`; durations formatted like the existing panels do.
- Read-only. The auto-buy toggle lives where it already lives; this panel does not duplicate it.

## Architecture

One new file plus a small pure helper, following the repo's split:

- **`Managers/PpEta.cs`** — Unity-free, linked into the test project. Pure arithmetic:
  `Eta(long cost, long banked, double perHour)` → `double? hours` (null when the rate is ≤ 0 or the
  cost is already covered). This is the only place the estimate is computed, so it is unit-testable and
  cannot drift between the panel and any future consumer.
- **`PpPanel.cs`** — the view. Reads `SpendPlanner`, `GrowthTracker`, `ItopodFarmAdvisor`, calls `PpEta`.

No new manager: there is no decision to make here, only a readout, and the repo's rule is that panels
may read managers directly. Inventing a `PpAdvisor` that only forwards would add a layer with no job.

## Testing

- `PpEta` unit tests: cost already covered → null; rate 0 or negative → null; a normal case with a
  hand-checked number; a cost below the banked amount → null rather than a negative duration.
- Panel: build only. `UI AUDIT` needs the running game and is deferred by owner decision; the baseline
  for a future comparison is **70 lines, not zero** (measured 2026-08-10).

## Out of scope

Auto-buy (already exists). Changing `SpendPlanner`'s plan order. The Advanced Training module.
