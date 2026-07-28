# MoneyPitManager (`Managers/MoneyPitManager.cs`)

Money Pit throwing (with outcome prediction + prep loadouts) and the daily spin.

## Outcome prediction — reads the game's RNG state

`PredictMoneyPit(gold)` copies `pit.pitState` into `Random.state`, draws, and restores the
previous state. Ranges are the game's: ≥1e50 with wish 4 → `Range(1,6)` maps 1:1 onto the
`Outcomes` enum; ≥1e13 → `Range(1,11/12/13)` by tier where **4 = Worn** and **12 = Daycare**
(everything else None). Reward tiers: `moneyPitThresholds` = 1e5 … 1e70.

## THE LOCK INVARIANT (stage R3) — read before touching `CheckMoneyPit`

Every MoneyPit lock acquisition happens inside `CheckMoneyPit`, and it is the ONLY scope that ever
learns one succeeded (`TryMoneyPitSwap` acquires and forgets; `DoMoneyPit`/`ApplyPit` never see
it). So:

- The `try` opens **BEFORE** the acquisition calls — `TryMoneyPitSwap` sets the lock and THEN
  swaps gear/beards/diggers, so a throw during prep leaves the lock held before the call returns.
- `LoadoutManager.RestoreDaycare()` stays INSIDE the try, ahead of the release — a daycare fault
  used to take the release down with it.
- The `finally` is the ONLY release site, guarded by `HasMoneyPitLock()` so it is idempotent and
  self-selecting (paths that return before acquiring pass through untouched).

Cost of a stuck pit lock (why this is so defensive): `CanSwap()` goes false → no titan/ygg/gold/
quest/cooking swaps, no profile digger/beard/gear timelines, no advisor sets — **and
`RebirthAvailable()` returns false, so the run cannot end**. Worst case is a throw at/after
`DoMoneyPit`: the pit is spent, `MoneyPitReady()` is false for the whole cooldown, every later
call returns at line 1, nothing reaches a release. Hours, silent, only a reload clears it.

## Configuration vs execution inputs

`CheckMoneyPit(threshold, predict)` takes the minimum gold and prediction flag as ARGUMENTS.
`AdvisorThrow()` = `CheckMoneyPit(1e5, true)` — the advisor's terms (game floor, prediction forced
on) because the WHEN decision was already made by `AdvisorPlan`/the user.

**Never revert this to assigning Settings.** The old code set `Settings.MoneyPitThreshold = 1e5`
and `PredictMoneyPit = true`, restoring in a finally — but those setters PERSIST
(SavedSettings: log + IgnoreNextChange + disk write + UpdateForm). One throw cost four disk
writes, four "Saving Settings" lines and four form refreshes; `IgnoreNextChange` is a single bool
so three watcher events leaked; and a crash inside the window left 1e5 permanently written over
the user's threshold.

## Prep loadouts per predicted outcome

IronPill → digger {10} (Blood) + cap all rituals (if ManageMagic); Worn → Shockwave loadout with
daycare save (gear-leveling run); Exp → digger {11} (EXP); Pomegranate → Yggdrasil loadout;
Daycare → fill daycare. Non-prediction path: ≥1e50 with wish 4 → Shockwave + diggers {11,10}.

## Advisor throw policy (`AdvisorPlan`) — shared by ApplyPit and the PIT panel chip

HOLD when: pit on cooldown, TM unfunded (`realBaseGold <= 0`), or gold-starved for augs (the pit
must not eat aug spending). WAIT below 1e13 (outcome tiers start there) or when the next log10
reward tier is within 15 min of net GPS (tier-up jumps rewards). Otherwise THROW. The panel
displays the same struct so UI and behavior can never disagree.

## MoneyPitRunMode (the Worn/shockwave farm run)

`ShockwaveTier()` picks the highest tier whose predicted outcome is Worn/Daycare (1e50 → 1e18 →
1e15 → 1e13). `NeedsGold`/`NeedsLowerTier`/`NeedsRebirth` drive the run loop. The 1e15/1e13 tiers
add a **cadence pulse** (`gold % 8e16 < 1e15`, `gold % 4e14 < 1e13`) so the run keeps topping up a
reserve — hand-tuned constants of undocumented origin; do not retune without validating live. The
modulo stays meaningful only while gold is small enough for sub-window double resolution (fine at
these tiers).

`DoDailySpin()` — `startNoBullshitSpin()` once `spinTime >= targetSpinTime()`; result logged to
pitspin.log.
