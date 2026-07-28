# ZoneStatHelper (`Managers/ZoneStatHelper.cs`)

Zone stat thresholds for gold sniping / zone routing, with user overrides.

## Data model (`ZoneStats.FightType`)

Per zone five numbers decide how it can be fought at (attack, def):

- `attack > OPower` → **2** (one-shot; fast combat, full farm cadence)
- `attack ≥ IPower && def ≥ IToughness` → **2** (idle-stat fast combat)
- `attack ≥ MPower && def ≥ MToughness` → **1** (manual: pre-cast buffs first)
- else **0** (can't do the zone)

`Defaults` covers zones 0–41 (community-sourced; README links the wiki page with the canonical
values). Titan zones and some transitional zones are absent — `ZoneFightType` returns **2 for
unknown zones so they never block progress**.

## Overrides

`CreateOverrides(dir)` merges `zoneOverride.json` (note: singular — the README calls it
`zoneOverrides.json`, the actual file the code reads/creates is `zoneOverride.json`) over
`Defaults`. File is created with a sample zone 0 on first run; saving it triggers the config
watcher reload. `UserOverrides` is THE table other modules read (`GearFarmAdvisor`,
`BoostFarmAdvisor` one-shot gates use `OPower`).

## Key readers

- `EffectiveAdvAttack()` — total attack WITHOUT the beast-mode multiplier
  (`totalAdvAttack() / max(1, beastModeBonus())`): the conservative baseline every one-shot /
  fight-type gate must agree on (beast mode toggles during snipes).
- `GetBestZone()` — highest reachable zone clearable at the required fight type; requires
  fightType 2 unless Ultimate Attack + Ultimate Buff are unlocked (then 1 suffices, buffs get
  pre-cast). Single-pass scan, winner's fight type remembered — runs off SnipeZone every frame.
- `RecommendedDcPercent` — "farm-ready" total DC per zone = `1 / smallest regular roll` from the
  game's LootDrop tables (rolls within 20× of the zone's most common; ultra-rares like the 0.8 %
  Ring of Apathy excluded — capping those is a choice, not a baseline). Feeds the zone drop-chance
  advice line.
