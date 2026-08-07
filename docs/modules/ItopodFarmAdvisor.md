# ItopodFarmAdvisor + ItopodRewards (`Managers/ItopodFarmAdvisor.cs`, `ItopodRewards.cs`)

What ITOPOD pays per second, in all four currencies it produces. `ItopodRewards` is the Unity-free
game-truth layer (linked into `tests/NGUAdvisor.Tests`); `ItopodFarmAdvisor` weights it by
`ITOPODManager.ProfileForMode`'s rotation slices.

## Why this exists

The pod used to enter the farm comparison as one boost-points-per-second number, computed at one
floor, from the regular attack alone (`BoostFarmAdvisor.ItopodRate` → `OptimalFloorForMode`). Both
halves were wrong:

1. The floor is not one number — see `ITOPODManager.md`, `ProfileForMode`.
2. **Boosts are the currency that saturates soonest.** Everything else about the pod keeps improving
   long after they stop.

## The reward table (decomp, `LootDrop` + `ItopodPerkController`)

`tier T = 1 + floor/50` (cap 40, but `maxItopodLevel()` is 1600, so `T ≤ 33`).

| currency | rule | behaviour with the floor |
|---|---|---|
| Boost | flat **14 %**, NOT drop-chance scaled; ladder index bends `T≥24→13, ≥18→12, ≥15→11, >10→10` | **saturates at T=24 = floor 1150** |
| AP | 1 AP every `max(40−T, 20)` kills | **saturates at T=20 = floor 950** |
| EXP | every `max(40−T, 20)` kills, award `T<3 ? T : (T−1)(T−2)+2` | **quadratic in T, never saturates** |
| PP | EVERY kill: `(200 \| 700 \| 2000 + floor) × totalPPBonus()`, threshold `1e6` per point | linear in the floor |
| Macguffin | every `killsPerMacguffin()` (5000 × perks 69/70/71 × Purple Heart) | flat — not modelled |

Three consequences the old model could not see:

- **Above floor 1150 the pod is an EXP/PP machine.** A boost-only rate reads that as a plateau and
  routes away from it.
- **`killsPerEXP` IS `killsPerAP`** — same formula, same `enemiesKilled % n` counter — so AP and EXP
  always land on the same kill. Mode 3's "AP dance" is therefore an EXP dance too, and since AP is
  always exactly 1 while the EXP award is quadratic in the tier reached on that kill, **EXP is the
  real payoff of the dance, not AP**. `ITOPODManager.PlanBuffs` used to skip the dance at
  `tier >= 20` with a fast respawn — a rule whose only premise was that `killsPerAP` bottoms out
  there, i.e. it switched the dance off exactly where its EXP payoff was largest. The gate is now
  "does the burst reach a higher TIER than the floor we already farm", which is the question every
  ITOPOD reward actually keys off. The `bestFloor >= 1550` guard stays: it is a ceiling
  (`maxItopodLevel()` is 1600), not an AP argument.
- **PP weighting is difficulty-dependent.** Normal's base is 200 against a floor of up to 1600 (the
  floor is 6× the base); Sadistic's is 2000 (the floor adds at most 80 %). How much a floor is worth
  in PP is not a constant.

`totalPPBonus(usePills: false)` — the pill multiplier only applies while `buffedKills` remains, so it
is a burst, not a farm rate.

## Rates

`ForMode(mode, sinks)` → `Rates { KillsPerSecond, DefaultFloor, PeakFloor, BoostPerSecond,
PpPerSecond, ApPerSecond, ExpPerSecond }`. Each currency is priced per rotation slice and weighted by
that slice's share of kills, because every one of them is a step function of the floor.

`ForMode(mode)` without sinks leaves `BoostPerSecond` at zero — for callers that only want PP/AP/EXP
and should not pay for a `BoostSinks.Current()` snapshot.

`Best(currency, sinks)` picks the mode maximizing one currency, over the same two candidates
`BoostFarmAdvisor` compares (Idle and Offensive; Snipe pre-casts and Defensive stalls).

**`ApPerSecond` is computed but is not a decision input and is not displayed.** AP is always exactly
1 per award, so it cannot discriminate between floors the way PP and EXP do; it stays in the model
because it is cheap and it is what the game does.

**`ExpPerSecond` is the raw award**, before `Character.addExp()`'s own bonuses — comparable across
floors and modes, not against a banked EXP total.

## Consumers

- `BoostFarmAdvisor.ItopodRate` — the boost component only; that advisor stays boost-only by design.
- `AdvisorApply.ApplyZones` — when the router parks in the pod *because nothing consumes boosts*, the
  boost rate is exactly the wrong thing to pick the combat mode on, so **PP decides** (the one
  currency nothing else in the game produces) and the other three go in the log.
- `AdventurePanel` floor info line.

## Open

There is deliberately **no single scalar** combining the four. The exchange rate between PP, boosts
and EXP is phase-dependent, so any constant would be wrong for half a run; callers pick the currency
their goal implies instead.
