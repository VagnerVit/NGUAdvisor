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

Valid wishes = difficulty requirement met, level below max, not game-locked (`wishLocked(id)`: 28 / 45 until Evil troll completions 4 / 6), not in `Settings.WishBlacklist`. `CustomAllocation` runs wish allocation only while `buttons.wishes.interactable` (= `wishes.wishesOn`).

## Wish modes (`BestWishId`)

`Settings.WishMode`: 0 = priority order only; **1 Cheapest** (min `wishSpeedDivider`);
**2 Fastest** (max `ppt / (1 − progress)` — completion rate); **3 Balanced** (first slot behaves
like Cheapest; last slot prefers wishes whose base rate is within 10 % of `minimumWishTime()`;
otherwise max ppt then max divider). Unless `WeakPriorities`, `Settings.WishPriorities` membership
is a HARD pre-filter in modes > 0; priority index is the final tiebreak, then highest progress.

**4 Value** (`ValueOrder` / `AllocateByValue`): each wish with a `total*Bonus` factor is scored as
`e/(1+L·e)` (`HackMath.WishGainPerLevel`; wish 46 uses the `1−L·e` reducer form) × its levels per tick.
Exact because the game multiplies wish effects into products (`WishesController.cs:1122-1494`, the
id list is `HackMath.ProductWishIds`). Wish-speed wishes (1/21/43, `totalWishBonus`) are priced as their
gain × the summed value rate of the other candidates. Card/mayo wishes score only while cards are on.
Flat/unlock wishes (inventory, deck size, card tags, base PP, sadistic boss) have no score: they rank
after scored ones, cheapest divider first. With an empty priority list, wishes 0, 1, 4 come first
(guide ch.6); a non-empty list is the same hard pre-filter as modes 1-3.

⚠ Relative gain treats 1 % of any product as 1 % of any other (energy cap vs exp vs stats); wish 110's
cap of 2.0 is not modelled. Do not read a small gap between two wishes as meaningful.

Value mode also changes the SPLIT: all three biases are 0.17 (`:811-844`), so a wish given fraction f of
every pool runs at `K·f^0.51`; maximising Σ w_i·f_i^0.51 gives f_i ∝ w_i^(1/0.49) (`WishSplitExponent`,
≈ w²). w_i is the unclamped value rate at an equal reference split. Wishes are filled in descending
weight with share = f_i / Σ remaining f, so what a clamped (min-time) wish cannot use flows to the rest.
Other modes keep the equal split.

`UpdateWishMenu` keeps the game's wish page/selection stable while the advisor reallocates (page
derived from the first pod id's index in `curValidUpgradesList`), preferring a selected wish that
still has resources allocated.
