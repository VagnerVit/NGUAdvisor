# BoostFarmAdvisor (`Managers/BoostFarmAdvisor.cs`)

Where to farm boosts: ranks one-shottable zones vs ITOPOD by boost-value per kill. Modeled on
Farmer Sanc's Boost Almanac, but constants re-sourced from the CURRENT game decomp.

## Value model

```
boost-value/kill = Σ_rolls value_i × min(chance_i × dcFactor, cap_i) × 0.77 (normal-enemy share)
dcFactor = lootFactor()          (Normal zones)
         = lootFactor()^(1/3)    (Rooted = Evil+ zones; game's lootChanceDisplayRooted)
```

Kill cadence is ~equal across one-shottable zones, so per-kill ranking = per-second ranking.
Only boss-unlocked zones with `attack ≥ OPower` (ZoneStatHelper) compete.

ITOPOD (`itopodDrop`): flat **14 % chance, NOT drop-chance scaled**; boost tier laddered from
optimal floor: tier = floor/50+1, mapped into the 13-value ladder {1…10000} with the game's
tier→index bends (tier ≥ 24→13, ≥ 18→12, ≥ 15→11, > 10→10).

## Data provenance — two real bugs are encoded in the comments, don't regress them

1. **Per-roll caps (zones 0–18)**: the old table lacked the game's `Mathf.Min(cap, chance×DC)`
   caps and mis-ranked zones at high DC (user-reported: Almanac ranked Badly Drawn World over
   A Very Strange Place; AVSP saturates at its 0.25 cap while BDW keeps scaling).
2. **Evil zone values (finding #21)**: the old single-roll `value` held the TOOLTIP's display-cap
   percent, not a boost value — Evil zones were undervalued 20–1000× and never won vs ITOPOD.
   Current values are the real boost ladder {200,500,1000,2000,5000,10000} keyed by the makeLoot
   item id (verified `LootDrop.zone{N}Drop` + ItemNameDesc). Each Evil zone fires TWO boost rolls
   with identical chance/cap (roll 2 = next tier up, `makeLoot id+1`); zones 36+ repeat the 10K
   ceiling. **Zone 29's in-game tooltip chance 1.5E-06 is a TYPO — the drop code says 1.5E-05.**

## Demand gate (`BoostDemandExists`)

Boosts only pay while something consumes them: an Infinity Cube under its softcap (game-truth:
`cubePower()/cubeToughness()` CLAMP effective cube stats at base + gear attack/defense — feeding
a capped cube adds nothing), or equipped/priority-listed gear with `GetNeededBoosts().Total() > 0`.
Otherwise ITOPOD PP/EXP beats boost farming. **Fails OPEN** (demand unknown → keep farming) —
the classic always-boost behavior on any read error.

## Consumers

`AdvisorApply.ApplyZones` (idle-farm routing between gear farm / boost farm / ITOPOD) and the
Advisor priorities list (`Verdict.Text`). `BestZone == -1000` means ITOPOD.
