# BoostFarmAdvisor (`Managers/BoostFarmAdvisor.cs`)

Where to farm boosts: ranks one-shottable zones vs ITOPOD by boost-value per kill. Modeled on
Farmer Sanc's Boost Almanac, but constants re-sourced from the CURRENT game decomp.

## Rate model — boost POINTS PER SECOND

```
rate = Σ_rolls min(chance_i × dcFactor, cap_i) × BoostSinks.ValueOfDrop(tier_i)
       × ZoneCadence.For(zone, mode).NormalKillsPerSecond

dcFactor = lootFactor()          (Normal zones)
         = lootFactor()^(1/3)    (Rooted = Evil+ zones; game's lootChanceDisplayRooted)
```

Verified: zones 20–45 use `lootFactorRooted()`, zones 0–19 use `lootFactor()`. Zone 20 is Chocolate
World, which matches the guide's "from Chocolate World onwards Drop Chance is cube-rooted".

Three things this model used to simplify, each of which changed the ranking — **read
`ZoneCadence.md` before touching any of them**:

1. **Drop value is priced through the live sinks** (`BoostSinks`), not taken flat from the ladder:
   gear overflow past a channel cap is destroyed, the cube is a `sqrt` soft sink past its softcap, and
   the recycling chain adds lower tiers back against the *same* channel. Flat tier values massively
   over-priced high tiers on a nearly-capped loadout.
2. **Cadence is measured per zone and per combat mode** (`ZoneCadence`), from the game's own spawn
   table, including multi-hit enemies, enemy regen and paralyzer downtime. The old "cadence is ~equal
   across one-shottable zones" shortcut hid the ~2× Idle/Offensive gap *and* excluded every zone that
   needs a second swing, however valuable.
3. **The gate is "can we kill the NORMALS and survive"**, not `attack ≥ OPower`. OPower is calibrated
   on the zone boss, and bosses roll no boosts at all — it demanded 1.79× too much attack in zone 0.

`Analyze()` evaluates each zone at both candidate modes (Idle and Offensive; Snipe pre-casts and
Defensive stalls, so neither can win on throughput) and returns `BestMode` alongside the rate.
`AdvisorApply.ApplyZones` applies it to `Settings.CombatMode` — the recommendation is worthless if the
mode it was costed at is not the one running. `RateAtCurrentMode` is the same winner re-priced at the
configured mode, so the UI can show what the current setting costs.

ITOPOD is priced by `ItopodFarmAdvisor.ForMode(mode, sinks).BoostPerSecond` — this advisor consumes
only the boost component and stays boost-only by design. The floor distribution over the attack
rotation, the tier ladder and the reward formulas all live there and in `ItopodRewards`; do not
reimplement any of them here. **Read `ItopodFarmAdvisor.md`** — in particular that the boost yield
stops improving at floor 1150 while PP and EXP do not, which is why a boost-only reading of the pod
is not a reading of the pod.

The old `ItopodRate` evaluated one floor from `OptimalFloorForMode` (regular attack, no buffs, no big
moves) and assumed one swing per kill at it.

## Data provenance — these bugs are encoded in the comments, don't regress them

1. **Per-roll caps (zones 0–18)**: the old table lacked the game's `Mathf.Min(cap, chance×DC)`
   caps and mis-ranked zones at high DC (user-reported: Almanac ranked Badly Drawn World over
   A Very Strange Place; AVSP saturates at its 0.25 cap while BDW keeps scaling).
2. **Zone 22 (Pretty Pink Princess Land) rolls do NOT share a cap.** `zone22Drop` caps roll 1 at
   `0.08f` and roll 2 at `0.06f`; we had 0.08 on both, over-pricing PPPL by up to 25% at high drop
   chance (the 1000-value roll is two thirds of the zone). The Boost Almanac had this right
   (`Max Drop Chance 8%`, `2nd Max DC 6%`) — we were the only ones wrong. Every other zone's chances
   and caps were re-verified verbatim against the drop code and agree with the almanac.
3. **Evil zone values (finding #21)**: the old single-roll `value` held the TOOLTIP's display-cap
   percent, not a boost value — Evil zones were undervalued 20–1000× and never won vs ITOPOD.
   Current values are the real boost ladder {200,500,1000,2000,5000,10000} keyed by the makeLoot
   item id (verified `LootDrop.zone{N}Drop` + ItemNameDesc). Each Evil zone fires TWO boost rolls
   with identical chance (roll 2 = next tier up, `makeLoot id+1`) and — **except zone 22** — an
   identical cap; zones 36+ repeat the 10K ceiling. **Zone 29's in-game tooltip chance 1.5E-06 is a
   TYPO — the drop code says 1.5E-05.**

## Demand gate (`BoostDemandExists`)

A coarse on/off switch behind the `AdvisorFarmBoost` setting: boosts only pay while something consumes
them — an Infinity Cube under its softcap, or equipped/priority-listed gear with
`GetNeededBoosts().Total() > 0` that is **not on `Settings.BoostBlacklist`** (checked inside the local
`NeedsBoosts`, not in the two loops, because equipped gear is walked independently of the priority
list — 2026-08-26). Otherwise ITOPOD PP/EXP beats boost farming. **Fails OPEN** (demand
unknown → keep farming) — the classic always-boost behavior on any read error.

Note this gate is now mostly belt-and-braces: `BoostSinks` prices drops against the same headroom
continuously, so a saturated loadout drives `BestRate` toward zero and ITOPOD wins on its own. The gate
survives because it is a user-facing toggle with documented semantics, and because its cube test is
deliberately stricter than the value model's — `cubePower()` does not hard-clamp at the softcap, it
returns `softcap + sqrt(raw − softcap)`, so the value model still credits an over-cap cube with the
diminishing remainder while the gate calls it done.

## `DropHere(zone)` — which boost id this spot drops, and how far level 100 is

Separate from the rate model: `Analyze()` answers "where should I farm", `DropHere` answers "what is
landing in my inventory, and how long until it maxes". Game truth, all from the decomp:

| Rule | Source |
|---|---|
| a dropped boost arrives at **level 0** | `Equipment` ctor; `ItemNameDesc` adds `bonusLootLevels()` only to items already above level 0, so boosts never get it |
| merging is `level = level + other.level + 1`, capped at 100 | `Equipment.mergeItem` |
| ⇒ one copy holds `level + 1` drops, and **level 100 costs 101 drops** of that exact id | the two rules above |
| reaching 100 marks the id maxed and **merges of it are refused forever** | `checkItemTransform` → `markItemAsMaxxed`; `mergeable()` tests `itemMaxxed[id]` |
| the id is `tier + (type − 1) × 13` | ids 1-13 Power, 14-26 Toughness, 27-39 Special |

The type comes from `TransformManager.EffectiveBoostType` (the user's forced P/T/S/X or the advisor's
pick), because every boost drop is rerolled into it before it lands. `zone < 0` means "wherever the
character is"; pass `1000` for ITOPOD, whose tier comes from the FLOOR (`ItopodRewards`) rather than a
zone drop table. Drops/second reuses this advisor's own inputs — roll chance × `lootFactor` under the
per-roll cap × `ZoneCadence` kills/s — so it introduces no second rate model.

Only the HIGHEST tier a zone rolls is reported: a two-roll zone's lower roll is a different id, and it
is the high one worth counting toward a max.

## Consumers

`AdvisorApply.ApplyZones` (idle-farm routing between gear farm / boost farm / ITOPOD) and the
Advisor priorities list (`Verdict.Text`). `BestZone == -1000` means ITOPOD. `DropHere` feeds the
AdventurePanel ZONES and ITOPOD pages — deliberately NOT the Boosts panel's transform strip: the
answer changes with the zone, and the transform type is only what the drop is rerolled into.
