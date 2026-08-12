# TitanTables (`Managers/TitanTables.cs`)

Hand-extracted titan requirement tables. **Unity-free by design** (pure `double[][][]`, no game
reference) so shape/monotonicity can be unit-tested without loading the game (review finding #33 —
see `tests/NGUAdvisor.Tests/TitanTablesTests.cs`). `OptimizationAdvisor` consumes them via its
`TitanAk`/`TitanGuide` aliases.

## `Ak` — autokill stat gates

`[titanIndex][version-1] = { attack, defense, HP-regen }`, extracted from the game's
`autokillTitan{N}V{V}Achieved` methods. **The regen column is a REAL gate from T4 up** (0 = no
check) — omitting it let the UI claim AK-ready while the game refused to fire.

Non-stat gates are NOT here (they live in `ZoneHelpers.AutokillAvailable`): T4 also needs item 135
maxed, T5 needs `boss5Kills >= 3`, T9+ can alternatively unlock by kill counts. This table is the
stat path you can push toward.

## `Guide` — the community kill ladder

`[titanIndex][version-1] = { manual atk, manual def, idle atk, idle def }` from the guide's
titan-list. **NOT derivable from `Ak`** — the old 45 %/80 %-of-AK scalars were calibrated on T1 and
overstated Beast first-kill by ~60 % and idle by ~2× (user report).

- `idle = 0/0` means the guide lists no idle numbers (Walderp, Godmother, T10–T12): those are
  fought manually until AK, so `StagedRequirementFor` skips the idle stage.
- Manual numbers assume max move-cooldown items + Beast Mode ON — which is why `AtHourPlanner`
  compares them against beast-INCLUDED attack (see AtHourPlanner.md).
- Walderp's manual figure is the FINAL form (first form is 800K/400K).

---

# NumberFormatter (`Managers/NumberFormatter.cs`)

The single canonical large-number abbreviator (review finding #31 consolidated six per-panel
`Fmt()` ladders with inconsistent "Q"/"Qa" suffixes, precision and negative handling). Unity-free
and pure → unit-tested (`NumberFormatterTests`).

- Suffix ladder K, M, B, T, **Qa**, Qi, Sx, Sp, Oc, No, De; ~3 significant figures; signed;
  NaN/Infinity/0 → `"0"`; beyond the ladder (≥ ~1e36) → scientific `0.##e+0`.
- Mantissa roll-up guard: rounding can produce "1000" (the double nearest 1e33 lands at ~999.99
  No) — it rolls up a tier so it reads "1De".
- Named `NumberFormatter`, not `NumberFormat`, to avoid colliding with the game's global
  `NumberFormat` type that the UI panels also reference.
- `Duration(hours)` renders a wait the same way everywhere ("45m", "3h 20m", "2d 4h"). It was private
  to `PpPanel` while there was one caller; `SpendOverview.Buys` is the second. **It refuses to quote
  what it cannot state**: `<1m` under a minute, `over a year` past one, `?` for NaN/Infinity — a
  rendered "0h" beside a purchase reads as "buy it now", which is the same rule `PpEta` follows.
- **Exception**: `GoldPanel` formats via the game's own `Character.display()` FIRST (to honor the
  player's in-game number-display setting) and only falls back here — that special case lives at
  GoldPanel's call site, not here.
