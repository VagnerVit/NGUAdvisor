# NguBbSplit

Splits an energy/magic pool between the equal-share NGU lanes along BB breakpoints. Called from
`NGUBP.Rebalance` in every Energy/Magic breakpoint walk (`Main.QuickStuff` → `DoAllocations`), so
it runs on the Unity main thread every allocation tick. `RebalanceAfter` picks the point: right after
the last non-cap token when it is an NGU lane (the CAP tokens behind it get only what is left at BB),
otherwise after the whole walk.

## Game truth

An NGU gains at most one level per tick and discards the overfill (decomp `NGUController.updateNGU`).
A lane holding `BB/k` levels one every `k` ticks; anything between two such amounts buys nothing.

## Order

1. Lanes reach BB (`k = 1`) in priority order while the pool covers them.
2. The rest is split equally; each lane takes the fastest `k` its share affords.
3. Leftover lifts lanes, in priority order, to the fastest `k` their amount plus the leftover affords.

## Invariants

- **Never walk `k` one tick at a time.** Right after a rebirth the pool is a sliver of BB, so `k` lands in
  the millions, and each `bbAt` call is `NGUBP.MagicBb`/`EnergyBb` — dozens of game getters. A linear
  walk froze the game at a rebirth (2026-10-13, found from a live managed stack). `Cost(k)` never
  rises with `k` (BB only shrinks as the `500 / k` lookahead does), so `FastestWithin` bisects; both
  steps 2 and 3 go through it. `PoolFarBelowBbFindsTheSpeedWithoutWalkingEveryTick` caps the call count.
