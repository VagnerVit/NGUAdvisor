# BloodMagicManager (`Managers/BloodMagicManager.cs`)

Blood spell CASTER (the planner is `BloodPlanner`). Three spells as `Spell` subclasses:
`ironPill`, `guffA`, `guffB` — each binding the game's cooldown + minimum-blood values at
construction.

## Two cast paths

- **`Cast(rebirth)`** — the threshold path: requires `CastBloodSpells`, unlock, cooldown, minimum
  blood, and the user's configured power `Threshold`. `rebirth && CastOnRebirth` FORCES the cast
  (use-it-or-lose-it — blood is wiped at rebirth) and bypasses both the threshold and the
  fail-safes.
- **`CastPlanned()`** — BloodPlanner decided the timing: safety checks only (unlock, cooldown,
  minimum blood) + fail-safes; NO user threshold.

Log noise control: cooldown/threshold misses only log inside a 10 s window after the cooldown ends
(`Time < cooldown + 10`), so the retry loop can't spam.

## Iron Pill fail-safes (shared constants, also read by BloodPlanner)

- `PillWorthFraction = 0.10` — refuse a cast whose gain is under 10 % of **base** adventure power
  (`adventure.attack`, not the gear-inflated total).
- `PillMinAvailableSec = 1800` — refuse for the first 30 min the pill is available past cooldown,
  so blood pools into a stronger pill.

BloodPlanner mirrors both so its "CAST NOW" advice can never contradict what the caster will do.

## Effect formulas (game-exact)

- Iron Pill: `floor(blood^0.25)`, ×`ironPillBonus()` on Evil+.
- MacGuffin α: `floor((log10(blood/minMacguffin1Blood) + 1) × totalBloodGuffbonus())`; unlock =
  perk 72 ≥ 1.
- MacGuffin β: `floor(log20(blood/minMacguffin2Blood) + 1)`; unlock = perk 73 ≥ 1 AND Evil+.
