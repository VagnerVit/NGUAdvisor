# LscAdvisor (`Managers/LscAdvisor.cs`)

Laser Sword Challenge opportunity detector (user insight): LSC does NOT reset the number, and its
completion condition is just leveling the Laser Sword augment (augs[6]) + its upgrade to
`laserSwordTarget()` — finishable inside a normal run's Augs hour as free challenge progress.
Recommends when the estimate fits comfortably (≤ 48 min).

## Hard-won correctness rules (each reverses a real bug)

1. **Challenge state from the CONTROLLER, never the serialized data object.**
   `Character.challenges.laserSwordChallenge.maxCompletions` is `[NonSerialized]` and NOTHING in
   the game assigns it (its only writer has no callers) — it reads 0 forever; `curCompletions`
   there is the Normal-difficulty counter, unclamped. Use
   `allChallenges.laserSwordChallenge.currentCompletions()/maxCompletions/laserSwordTarget()`.
2. **Cost from SCRATCH** (`LevelSum(n) = n(n+1)/2`): engaging LSC is a rebirth
   (`Rebirth.engage → resetAll → Aug.reset`) that ZEROES the sword's levels. Discounting current
   levels read a maxed sword as a free challenge and auto-entered it.
3. **Rate from the GAME'S OWN function** (`AugTimeLeftEnergyMax` wrappers around
   `getAugProgressPerTick`), never a hand-copied formula — the gear Augs bonus, macguffin 12,
   hack/ITOPOD/card bonuses, noAugs multipliers AND the sadistic ÷5e7 divider are in the number
   by construction. The hand-rolled version overstated normal/evil by the whole bonus chain and
   understated sadistic ~5e7 (every LSC looked instantly free).
4. **Only the ESTIMATE is cached (120 s), not the gates.** Caching the early-outs served a stale
   `Known=false` right after a challenge completion (completions don't rebirth), so the
   auto-rebirth started a plain run instead of the LSC run.

Estimate: rate is linear in 1/(level+1) → seconds = per-level coefficient × LevelSum(target),
aug + upgrade, ×2 safety (start friction + gold). Energy basis = whole `curEnergy` (the sword
owns the Augs hour in-challenge; EXP-bought stats persist into challenges).
