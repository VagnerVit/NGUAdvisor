# WishManager (`Managers/WishManager.cs`)

Wish resource allocation (energy/magic/R3 across wish slots) + wish-menu page keeping.

## Game-truth progress formula

```
basePPT(id) = (ePower×energy)^energyBias × (mPower×magic)^magicBias × (r3Power×res3)^res3Bias
              × totalWishSpeedBonuses() / wishSpeedDivider(id)
```

`ProgressPerTick` clamps to `minimumWishTime()` and applies the game's **499-tick offset**
(`progress + ppt × 499`) plus a 2^−25 floor — matching the game's own "will this wish ever
finish" test. Below 1e−8 the wish contributes nothing (returns 0) and is skipped as a candidate.

`AllocateToWish` corrects for the clamp: `multi = (basePPT/PPT)^(1/3/energyBias)` and divides the
input by it, so a wish already at the minimum time gets only the resources it can actually use.
The `×1.000002` fudge covers the game's own rounding on `energyMagicInput`.

## Allocation loop

Pools = `idle{Energy,Magic,Res3} × Settings.Wish{Energy,Magic,R3}%` (or the full idle pool when
`overCap`). Slots = `min(curWishSlots(), Settings.WishLimit, validWishes)`. Each iteration splits
the REMAINING pool by the remaining slot count (with a `Math.Sign(remainder)` +1 so nothing is
lost to integer division), picks the best wish, allocates, and subtracts what the game actually
took.

Valid wishes = difficulty requirement met, level below max, not in `Settings.WishBlacklist`.

## Wish modes (`BestWishId`)

`Settings.WishMode`: 0 = priority order only; **1 Cheapest** (min `wishSpeedDivider`);
**2 Fastest** (max `ppt / (1 − progress)` — completion rate); **3 Balanced** (first slot behaves
like Cheapest; last slot prefers wishes whose base rate is within 10 % of `minimumWishTime()`;
otherwise max ppt then max divider). Unless `WeakPriorities`, `Settings.WishPriorities` membership
is a HARD pre-filter in modes > 0; priority index is the final tiebreak, then highest progress.

`UpdateWishMenu` keeps the game's wish page/selection stable while the advisor reallocates (page
derived from the first pod id's index in `curValidUpgradesList`), preferring a selected wish that
still has resources allocated.
