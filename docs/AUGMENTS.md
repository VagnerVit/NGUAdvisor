# Augments — the efficiency math

Source of truth for how the advisor prices augments. Distilled from three independent sources:
the game's own functions (`AugmentController`, read live), the reference Gear Optimizer
(`external/gear-optimizer/src/Augment.js`), and the community guide
(`external/ngu-guide/.../mechanics/augmentation.md`, `challenges.md`, chapters 1/2/5).

Implementation: `AllocationProfiles/Breakpoints/ResourceBreakpoints/BestAug.cs` (+ `AugmentBP.cs`).

## Game truth

Boost (`AugmentController.getTotalStatBoost`):

```
boost_i = base_i · augLevel^e_i · (1 + upgradeLevel²)
```

- `base_i` = 25^i, with Exoskeleton ×100 and Laser Sword ×10⁴
- `e_i` = `augTierBonus()` = **1 + 0.1·i** with no LSC completions
- Augments stack **additively** with each other → fund exactly one. This is not a heuristic: each
  augment's boost is X^(1+e/2) in the energy it receives, i.e. **convex**, so maximising a sum of
  convex terms under one energy budget puts everything in a single term (corner solution). The one
  exception is a gold-starved augment — a full bar waits for gold (`Augment.js:113-136`) and the energy
  behind it converts to nothing, so once a pair cannot pay for more levels, the surplus is better spent
  on a cheaper augment than on the starved one
- Levels reset on rebirth. Nothing carries between runs

Gold per level (`Augment.js:21-52`):

| half | next level | integrated |
|---|---|---|
| augment | `goldBase · (L+1)` | `goldBase · L²/2` |
| upgrade | `goldBase · (U+1)²` | `goldBase · U³/3` |

Energy clock (`Augment.js:54-68`, mirrored by `AugmentBP.CalculateAugCapCalc`):
seconds per level ∝ `energyBase_i · (L+1) / (Ecap · augSpeed)`, so a whole phase of length T reaches
**L ≈ √(2·E·s·T / energyBase)**.

### Bases (Normal; GO's tick-energy units)

| # | Augment | base boost | e | E-base aug | E-base upg | Gold aug | Gold upg |
|---|---|---|---|---|---|---|---|
| 0 | Safety Scissors | 1 | 1.0 | 2e7 | 2e7 | 10k | 10M |
| 1 | Milk Infusion | 25 | 1.1 | 3.4e8 | 2.4e8 | 200k | 500M |
| 2 | Cannon Implant | 625 | 1.2 | 5.78e9 | 2.88e9 | 4M | 25B |
| 3 | Shoulder Minigun | 15 625 | 1.3 | 9.83e10 | 3.46e10 | 80M | 1.25T |
| 4 | Energy Buster | 390 625 | 1.4 | 1.67e12 | 4.15e11 | 1.6B | 62.5T |
| 5 | Adv. Exoskeleton | 9.77e8 | 1.5 | 2.34e15 | 3.32e14 | 1.8e16 | see note |
| 6 | Laser Sword | 2.44e12 | 1.6 | 3.27e18 | 2.65e17 | 2.3e19 | see note |

Two discontinuities matter: the energy base steps ×17 (aug) / ×12 (upgrade) per tier **but ×1400 /
×800 at Exoskeleton**, and Exoskeleton's aug gold cost is hard-coded to 1.8e16 rather than the
formula's 1.6e12. Note: GO's closed form does not special-case *upgrade* gold for tiers 5-6 and the
guide's table disagrees with it — read those from the game (`getUpgradeCost()`), never from either.

Difficulty scales every energy base by the same factor (Evil ×2.5e12, Sadistic ~×2e27), so the
optimal choice is unchanged **when expressed in level counts**.

## The single variable

Substituting the level model into the boost formula:

```
boost_i ∝ base_i · X^(1 + e_i/2),   X = Ecap · augSpeed · phase length
```

Consequences, and they drive everything the advisor does:

1. **Ecap, aug speed and phase length are interchangeable.** Doubling any one is the same move.
2. **Higher tiers have a higher exponent on X**, so tiers cross over exactly once, in one direction.
   "Best augment" is monotone in progress — switch up, never back.
3. **Boost is superlinear in time** (T^1.5 … T^1.8). Stretching the phase from 1h to 2.5h is ~4-5×,
   not 2.5×. Combined with the rebirth reset: the phase must be one uninterrupted block.

## Optimal energy split within a pair

Maximising `L^e · U²` under an energy budget (both halves grow as √energy):

```
energy_aug : energy_upgrade = e_i : 2
```

The upgrade half should get *more* energy than the augment. GO's "Exponent" ratio button and
`BestAug.Split()` (`tier/(2+tier)` : `2/(2+tier)`) agree. GO's guide says "Ratio: Equal", but that is
only a simplification for comparing tiers against each other, not an allocation rule.

## Which constraint binds

ρ = gold base / energy base, i.e. gold consumed per unit of energy work:

| Augment | ρ (aug half) |
|---|---|
| Scissors … Buster | 5.0e-4 → 9.6e-4 |
| Exoskeleton | 7.7 |
| Laser Sword | 7.0 |

Inside Scissors…Buster gold never decides *which* tier to run (a 2× spread), only how far you get.
From Exoskeleton on it is ~10⁴× worse and gold becomes the wall.

Within a pair it is the **upgrade** half that gold stops, because its cost integrates cubically.
Diagnostic: at the energy-optimal split, `U/L` should be √(2/e_i) — for Scissors ≈ 1.41. An upgrade
level well *below* that means gold-limited, and the fix is GPS (TM speed past level 49, diggers, NGU
Gold, Counterfeit Gold during the phase), not a different augment.

## Tier crossovers

From the model above (optimal split, no gold limit):

| Switch | when the lower tier would reach ~ | new tier starts at ~ |
|---|---|---|
| Scissors → Milk | 3 800 (upg 5 400) | 960 |
| Milk → Cannon | 15 000 | 3 800 |
| Cannon → Minigun | 68 000 | 17 000 |
| Minigun → Buster | ~240 000 | 64 000 |
| Buster → Exoskeleton | needs X ×3e28 | — |

That last row is the ×1400 energy step plus ρ: **Exoskeleton and Laser Sword are never the
boost-optimal pick in Normal.** The Laser Sword is run for LSC, not for its multiplier. It also
explains why the guide says "most expensive augment you can finish in 30m" in Ch.1 (level ~10, only
`base_i` matters) and then "Scissors will outscale them" in Ch.2 — the crossover moved with gold.

Sanity check: if the augment you run gains fewer than ~1 000 levels in a phase, the tier is too high.
The optimum usually lands in the 1 000 – 50 000 level band.

## Permanent multipliers, by leverage

1. **Laser Sword Challenge** — raises the exponent itself (`Augment.js:7-19`):
   `e_i = 1 + (0.1 + 0.05[first] + 0.05[last] + min(LSC,20)·0.01)·i`. All 20 completions give
   `e_i = 1 + 0.4·i` (Laser Sword 3.4 instead of 1.6, Milk 1.4 instead of 1.1) — and because boost
   goes as X^(1+e/2) it also raises the payoff of every future energy/time gain. LSC does **not**
   reset the Number, so it rides along on a normal run's augment phase (see `LscAdvisor`).
2. **No Augs Challenge** — +25% Augmentation Power per completion, +10% speed on the first, and the
   5th completion **halves augment gold** (`nacfactor = 0.5`): ≈ +41% aug levels, +26% upgrade levels
   across all tiers.
3. **Aug speed stack** — gear (`specType.Augs`), macguffin 12, hacks, ITOPOD, cards, NAC bonuses.
   All of it multiplies X, so it lands with exponent 1.5-1.8. Gear by chapter: Badly Drawn Gun (Ch.3,
   good until late Ch.5) → Meeple (Ch.5) → Glove of Power (Ch.7).

## Energy quantisation

Progress is per tick (50/s) and overfill past a level boundary is dropped. That is why
`AugmentBP.CalculateAugCap` computes the energy needed for exactly one level per tick and then
allocates the largest integer *sub-multiple* of it that fits the budget. Manual play: keep the cap
time on whole ticks, or lose a fraction of every level.

The panel's **Advance Energy** checkbox plus the Target column is the game's own auto-advance. The
advisor reads those same `augmentTarget`/`upgradeTarget` fields (`AugmentBP.TargetMet`), so with a
profile running, leave them at 0 and drive it with tokens.

## What BestAug does with all this

- ranks by **projected boost gain over the phase**, not by cost — an expensive-but-steep aug can win
- **horizon** = end of the augment phase (first later energy breakpoint funding no augment) or the
  scheduled rebirth, whichever comes first, clamped to 3h; a fixed 1h horizon underpriced steep augs
  in Ch.5's 2.5h phase and overpriced them in a 30m run
- **splits energy by elasticity** (`e_i : 2`) and yields a dead half's share to the live one
- **caps levels by the gold budget** (`gold + netGPS·horizon`, split in the same ratio as energy),
  with the per-level base derived from the live cost so NAC's −50% is included by construction
- **rebalances the split when gold starves a half**: levels grow as √energy, so a half held to `n_G` of
  the `n_E` levels its share would clock keeps only `(n_G/n_E)²` of that share and the rest goes to the
  half that can still convert it — usually the augment, since the upgrade's gold is cubic. The gold
  split itself stays on the elasticity ratio, otherwise the correction feeds back into its own input
- **floors levels at a hard stop** — a level in flight at the rebirth or at the end of the phase is
  never completed, so it is worth nothing

`ProfileValidator.Warnings()` reports a profile whose energy breakpoint funds more than one augment
out of the shared pool (advice only — it never blocks a load; it lands in the log and in the Profile
Editor status line). Two things it deliberately does not flag:

- `AUG-8`, `AUG-9` is *not* two augments. The token index is flat over 0-13, even = augment, odd = its
  upgrade, so that pair is one augment's two halves. Every shipped profile is written that way.
- **CAP** tokens. A `CAPAUG` takes `min(need, idle)` and stays out of the equal-share divisor, so it is
  a bounded reservation, not a split — and it is the only way to force a specific augment.
  `CBlock2.0-LSC` pairs `CAPAUG-12:80` (the Laser Sword the challenge requires) with `BESTAUG` for the
  remainder, which is correct.

Note that a hand-written pair splits 50/50 rather than `e_i : 2`, which costs ~3% of the boost;
`BESTAUG` gets the split right by construction.
