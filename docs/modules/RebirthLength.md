# RebirthLength (`Managers/RebirthLength.cs`)

Pure, advisory model of what one more run hour is worth to the Number multiplier. Changes no rebirth;
surfaced as the `rebirth` line in state-export's PROGRESSION section.

## Model (decomp)

- `TimeMulti(t)` = `Rebirth.calculateTimeMulti`: tiered `t / divider / 3600` below an hour, then
  `1 + t / 172800` (48 h). `nextAttackMulti` is linear in `timeMulti` and in `oldTimeMulti` (the previous
  run's), so a run of T hours multiplies the Number term by M(T) and rebirthing every T compounds `M(T)^(1/T)` per hour.
- `MarginalHourGain(t)` = `M(t+1h)/M(t) − 1`; `AverageHourGain(t)` = `M(t)^(3600/t) − 1` (0 below an hour).
  At 24 h: +1.39 % for the next hour vs about 1.7 %/h over the run — the next hour is worth less than the average.
- `BeardTimeFactor` = `AllBeardsController.timeFactor`: `t/10800 × 24/(24−perk21)` capped at 8, 0 below an
  hour. The cap is reached at `3600 × (24 − perk21)` s: 24 h with perk 21 at 0. Past it beard trimmings stop growing.

## Limits

Only the timeMulti term is modelled; the other factors of `nextAttackMulti` (boss, blood, NGU, hacks, Ygg)
also move during the hour and are not. The beard cap is reported, not folded into the percentages
(trimmings feed `permLevel`, a different, per-beard sqrt curve). Pure file: tests in `RebirthLengthTests`.
