# NGUAdvisors (`Managers/NGUAdvisors.cs`)

NGU lane valuation — which NGUs deserve energy/magic right now. Revised after a user field
report (2026-07-11): the old chapter candidate lists excluded E7/E8/M5/M6 entirely and funded a
1.04×/hr NGU while a 1.95× one idled. Now EVERY unlocked NGU is a candidate; ranking uses the
game's exact math.

## Game-truth formulas (decomp `NGUController.progressPerTick`, `AllNGUController`)

- `levels/hr = power / speedDivider(id) × allocation × multiplierStack / (level+1) × 50 × 3600`
- **The multiplier stack matches the game term for term**: totalNGUSpeedBonus, itopod E/M NGU,
  macguffin[4]/[5], NGU-speed NGUs, diggers, hacks, beast quirks, wishes, cards, troll-challenge
  ×3 (Normal completions for magic / SADISTIC completions for energy), sadistic divider on the
  sadistic track. The old version missed the last six — keep the stack complete.
- Value: every NGU bonus is `1 + level × boostFactor` on the current track → x/hr score =
  `(1 + f(L+ΔL)) / (1 + fL)` — the same per-NGU rating the GO site shows.
- **Respawn (E2) is the one nonlinear curve** (decomp `respawnBonusNormal/Evil`): Normal ≤ 400
  linear floored at **0.8**, then asymptote to **0.6**; Evil/Sadistic ≤ 10000 floored at 0.925,
  then to 0.9. At a floor the ratio is 1.0 — a capped Respawn never earns a lane. (Related but
  distinct from the GEAR respawn floor 0.2 — see gear-optimizer-comparison.md.)
- Track-aware reads: `Level`/`Factor` switch on `settings.nguLevelTrack` (normal/evil/sadistic
  levels and boost factors are separate arrays).

## Selection — iterative equal-share prune (`Pick`)

Split the pool equally over the kept set; drop lanes whose ratio at their ACTUAL share is under
**1.05×/hr**; re-split (survivors' shares grow); repeat (≤ 12 iters, monotone → terminates).
Prune-only BY DESIGN — re-admitting on the larger share would oscillate. Nothing hot → deepen the
top two by rating. `Surplus` = positive-value lanes (> 1.0001) outside the hot set — the game
hard-caps every NGU at ONE level per tick, so a hot lane can't absorb extra pool; leftovers
belong in MORE lanes, not deeper ones.

Cached 30 s. Candidates come from `ChallengeOverlay.ChapterNguIds(resource)`. Consumers:
allocation (auto profile NGU targets), OptimizationAdvisor's NGUs row, GrowthPanel (Lph = the
predicted rate shown).

## `Diagnose` — why the measured rate can face a nonzero prediction

The plan describes what SHOULD run. `Diagnose(plan)` reads what the game is DOING with it, so a
`NGU LEVELS +0/hr` against `predicted 44,2/hr` names its cause instead of leaving the user to
decode the profile JSON by hand (which is exactly what happened 2026-08-12).

Game truth — decomp `NGUController.updateNGU` (energy) / its magic twin: a lane ticks **only**
while `NGU.skills[id].energy > 0` (`magicSkills[id].magic` for magic) **and** `reachedTarget(id)`
is false. With nothing allocated the tick returns immediately; at the target `autoAdvance` moves
the energy off the lane rather than leveling it. Four verdicts, in order:

| Condition | Short | Meaning |
|---|---|---|
| no lane anywhere holds anything | `no NGU allocation` | energy is in AT/augments/Wandoos/TM/wishes or idle |
| planned lanes hold nothing, others do | `fed elsewhere: …` | the profile funds different NGUs than the plan picked |
| a planned lane is at its in-game target | `at target: …` | auto-advance, not leveling — raise or clear the target |
| some planned lanes hold nothing | `partly unfunded: …` | measured rate falls short, not to zero |

The hard cap (`hardCapNormalLevel() == 1e9`) is deliberately NOT checked — unreachable in practice.

`TrackedLevelTotal(c)` is the same track rule applied to the whole tree, and exists because
`GrowthTracker` must count the levels the prediction is about: on Evil the normal `level` field
only moves with beast quirk 14, so summing it read a flat 0 while the run was climbing.
