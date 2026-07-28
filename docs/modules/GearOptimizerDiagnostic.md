# GearOptimizerDiagnostic (`Managers/GearOptimizerDiagnostic.cs`)

Validation tool for the native gear optimizer. `Run()` writes `logs/gearopt-diagnostic.log` with:

1. **Per-item stat maps** of every equipped item (from `GameGearAdapter`, spec %s via the game's
   `getBonusFactor`) — compare against the site's per-item numbers.
2. **Per-objective scores**: current equipped loadout score (cube + nude base included, live
   offhand %) vs `GearOptimizer.Optimize()` score and its picks, for every objective in
   `GearObjectives.Objectives`.

## Oracle workflow

1. F3 quicksave → `NGUSave.json` in the AppData folder.
2. Load that save into the website (https://gmiclotte.github.io/gear-optimizer/).
3. Run the diagnostic; diff item stats and optimizer picks against the site.

Expected mismatches (documented, not bugs): no hardcap clamp natively; objective-set divergences
(AT/Augments/Beards/Wandoos composites) — see gear-optimizer-comparison.md. Item stat %s and the
matching objectives (NGUs, Wishes, Hacks, TM, E/M NGU, E/M Wandoos, Blood Rituals) should agree.

Never throws — failures land in `debug.log`. Main thread only.
