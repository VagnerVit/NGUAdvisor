# GearOptimizerDiagnostic (`Managers/GearOptimizerDiagnostic.cs`)

Validation tool for the native gear optimizer. `Run()` writes `logs/gearopt-diagnostic.log` with:

1. **Per-item stat maps** of every equipped item (from `GameGearAdapter`, spec %s via the game's
   `getBonusFactor`) — compare against the site's per-item numbers.
2. **Per-objective scores**: current equipped loadout score (cube + nude base included, live
   offhand %) vs `GearOptimizer.Optimize()` score and its picks, for every objective in
   `GearObjectives.Objectives`.
3. **ITEMS PICKED BUT NOT WORN** — the same NOW/MAXED pair for every id a pick named that is not
   equipped, read off `GearOptimizer.BestCopy(id)`, i.e. **the copy `BuildPools` scored**. "Why that
   item and not this one" is unanswerable from the worn block alone — the challenger's stats are
   exactly what is missing — and reading them off an arbitrary copy would answer a different
   question than the optimizer asked.

4. **`Level:` per objective** (2026-10-01) — every pick's level and `@100 xN`: the objective's score of
   that pick set at cap with ONLY that item raised to level 100, over the same set at cap as it is
   (`GameGearAdapter.BuildItemAtLevel`, existing scorer). Level debt is invisible otherwise: caps scale
   `cap × (1 + level/100)` and boosts never raise level. Diagnostic only — nothing reads it back.

Every number in the log is formatted with `CultureInfo.InvariantCulture`.

## Triggering it

Two ways, and the disk one exists because the question is asked from outside the game:

- **F10** in the game window (Unity `Input`, so the game must have focus).
- **`gearopt.request`** dropped in the settings dir. Same request/acknowledge shape as
  `unload.request` and `state-export.request`: `Main.Update()` notices it on the Unity thread,
  deletes it as the acknowledgement, and sets the pending flag the one drain below runs. Wait for the
  file to vanish, then read the log. `Run()` reads live `Character`/inventory, so it is Unity-thread
  only — never call it from a watcher, a WinForms handler, or an external tool.

## Reading it: NOW vs MAXED

A pick that appears **only under MAXED** normally means boost debt — its cap beats what you wear, its
current fill does not. But it can also mean the pools held the wrong COPY of the worn item: a weak
duplicate makes the incumbent look beatable, and a maxed challenger shows up to "replace" it.
User-reported 2026-09-16, and the reason `BuildPools` now replicates `MaxItem()` (GearOptimizer.md):
`Sir Looty McLootington III` sat in the MAXED Drop Chance pick until the pool started scoring the
right copy of `Ascended Ascended Forest Pendant`, after which the challenger vanished and NOW rose to
match MAXED (6.67 → 7.07). **So: challenger under MAXED only ⇒ check the incumbent's copy before
concluding boost debt.**

## Oracle workflow

1. F3 quicksave → `NGUSave.json` in the AppData folder.
2. Load that save into the website (https://gmiclotte.github.io/gear-optimizer/).
3. Run the diagnostic; diff item stats and optimizer picks against the site.

Expected mismatches (documented, not bugs): no hardcap clamp natively; objective-set divergences
(AT/Augments/Beards/Wandoos composites) — see gear-optimizer-comparison.md. Item stat %s and the
matching objectives (NGUs, Wishes, Hacks, TM, E/M NGU, E/M Wandoos, Blood Rituals) should agree.

Never throws — failures land in `debug.log`. Main thread only.
