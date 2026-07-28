# GearFarmAdvisor (`Managers/GearFarmAdvisor.cs`)

Farm Gear Zones advisor: finds zones whose droppable EQUIPMENT is not yet level-100 (each drop
merges +1 toward the permanent item-max bonus), ranks them by time-to-cap at the current drop
chance, and when nothing caps inside the budget, reports the drop chance that would.

## The drop table is decomp game-truth — do not "tune" it

`Table` is extracted VERBATIM from the game's `LootDrop.zone{N}Drop` functions (scratchpad
`extract-geardrops.js` against the decomp). Per roll:

```
P(per kill) = min(Base + Chance × dcFactor, Cap), then 1-of-Span outcomes
```

- `dcFactor` = `lootFactor()` for Normal zones, `lootFactor()^(1/3)` for Evil+ zones — the
  **`RootedZones` set** {20,21,22,24,25,27,28,29,31,32,33,35,36,37,39,40,41,43} is the list of
  zones whose LootDrop uses `lootFactorRooted()`. This is the same cube-root rule the Evil-era
  checklist documents.
- `Span` counts SWITCH OUTCOMES, not items — junk/consumable cases ride along in some pools,
  which is why `Span` is stored instead of `Items.Length`. Non-equipment ids are filtered at
  runtime (`itemInfo.type[id] <= 5`, same test SavedSettings uses).
- Rolls fire per enemy-type branch: `Boss`/`Normal` flags, unset = any kill.
- Items with NO roll (guaranteed early drops, quest/titan specials, dead rolls like item 66 in
  zones 5/7 whose in-game chance multiplies a zeroed variable) are deliberately absent — they
  have no farmable rate.
- `Cap` matters: some items can NEVER cap inside a time budget no matter the DC (the "roll caps
  hold them past budget" verdict) — the honest-answer branch in `Analyze`.

## Rate model (shared with BoostFarmAdvisor)

`KillsPerHour = 800` (respawn ~4.5 s, one-shottable zones), enemy mix ~77 % normal / 10 % boss.
Only zones the character one-shots (`EffectiveAdvAttack() >= OPower` from
`ZoneStatHelper.UserOverrides`) and has boss-unlocked (`bossID > ZoneUnlocks[zone]`) compete.
Titan zones excluded. `TargetHours = 3.0` — the "worth farming now" budget (same hours-scale
ruling as the quest capstone hold).

## Analysis details

- `DropsNeeded(id)` = `max(1, 100 − highestOwnedLevel)`; unowned = 100 (a fresh drop is level 1,
  each merge +1). Maxed (`itemMaxxed`) and **loot-filtered** (`itemFiltered`) items are skipped —
  a filtered item never drops.
- Zone time-to-cap = the SLOWEST missing item (max over items of `needed / perHour`).
- `ReqLootFactor`: rates are monotonic in DC → binary search, **geometric midpoint**
  (`sqrt(lo*hi)`, the range spans decades), 60 iterations, up to `lootFactor × 1e9`; −1 when even
  that can't make budget (per-roll `Cap` binds).

## Consumers

`AdvisorApply.ApplyZones` routes idle farming to `Verdict.Best`; the Advisor priorities list
shows `Verdict.Text`. Never throws — `Analyze()` returns `Known=false` on failure.
