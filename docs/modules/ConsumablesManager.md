# ConsumablesManager (`Managers/ConsumablesManager.cs`)

Executes the profile's `Consumables` breakpoints. One `Consumable` subclass per item, each bound
to the game's arbitrary pod by ID with its use-method name and duration. Profile token → class in
`CreateInstance` (EPOT-A/B/C, MPOT-*, R3POT-*, EBARBAR, MBARBAR, MUFFIN, LC, SLC, MAYO).
A pod that isn't unlocked throws in the ctor and is logged + skipped (returns null).

| Token | Pod id | Duration |
|---|---|---|
| EPOT-A / MPOT-A / R3POT-A | 0 / 2 / 59 | 3600 s |
| EPOT-B / MPOT-B / R3POT-B | 1 / 3 / 60 | rebirth-scoped (`*InUse` flag, no timer) |
| EPOT-C / MPOT-C / R3POT-C (delta) | 26 / 27 / 61 | 86400 s — **shares the alpha's timer** |
| EBARBAR / MBARBAR | 5 / 6 | 3600 s |
| MUFFIN | 43 | 86400 s AND rebirth-scoped (both) |
| LC / SLC | 4 / 30 | 1800 / 43200 s (shared timer) |
| MAYO | 79 | 86400 s |

## The reload problem (`ShouldUse`) — the README's long warning in code

The injector cannot remember consumable usage across sessions or detect manual use, so a
restart/profile reload re-runs the current breakpoint. `ShouldUse` reconstructs what SHOULD be
running and buys only the shortfall:

1. **Rebirth-scoped** (`IsActive()` non-null): already active → skip; not timed → use exactly 1
   (a quantity modifier is ignored, and logged).
2. Compute `timeAtConsumableEnd = breakpointTime + duration × quantity`. Current rebirth time
   within 60 s of (or past) that → skip entirely ("Reload detected").
3. `ConsumeIfAlreadyRunning` off and the buff is running → skip.
4. `expectedTimeLeft = totalConsumableTime − timeSinceBreakpoint`; running buff within 60 s of it
   → skip ("Active buffs detected").
5. Otherwise `quantityToUse = round((expectedTimeLeft − timeLeft) / duration)` (away from zero) —
   partial top-ups; rebirth-scoped items still clamp to 1.

`CanUse` buys the shortfall when `AutoBuyConsumables` and AP allow, else logs the exact AP/count
gap. `Eat = ShouldUse → CanUse → Use`.

`EatConsumables(dict, time)` de-dupes: the same consumable SET at the same breakpoint time is
executed once (statics `_lastConsumables/_lastTime`; `ResetLastConsumables()` on
rebirth/profile reload). Alpha and delta potions sharing one timer is a game fact the model does
NOT reconcile — see the README's limitation list.
