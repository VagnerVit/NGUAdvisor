# WandoosAdvisor (`Managers/WandoosAdvisor.cs`)

EXACT Wandoos OS comparator mirroring the game's decompiled `Wandoos98Controller` math. Projects
each OS's A/D bonus over a run-matched window and recommends the best.

## Game-truth formulas (verified against the decomp)

- Level rate per OS: `min(alloc × totalWandoosSpeed / baseTime, 1)` per tick, **50 ticks/s**
  (1 level/tick hard max — the game adds progress once per 0.02 s tick).
- `baseTime`: Normal `1e9 / 1e12 / 1e15` (98/MEH/XL); **Evil+ `1e21 / 1e27 / 1e33`**.
- Bonus (`wandoosBonus`): 98 `((1+E/100)(1+M/25))^0.8`; MEH `(1+E/5)(1+2M)`;
  XL `((1+6E)(1+40M))^1.05`.
- Unlocks: MEH = `itemList.jakeComplete`; XL = `wandoos98.XLLevels > 0`.

## Projection assumptions (documented, deliberate)

- Allocation = whole current E/M cap (how CAPWAN behaves when it can).
- **Full-boot speed**: live speed ÷ `bootupSpeedFactor()` — right after rebirth the bootup factor
  is ~0, which would zero every projection and silently block the auto-switch exactly when
  switching is free. `boot < 0.02` → verdict `Known=false`, retry next tick.
- **The current OS keeps its banked levels; the other two start from 0** — the game's `changeOS`
  wipes the target's levels. Without this the current OS is understated and the advantage ratio
  is biased toward switching.
- `RunHorizonMinutes()`: remaining time to the profile's time-based rebirth target, clamped
  10 m–4 h; 120 m when no rebirth target (NORB/LRB).

Consumers: the Wandoos auto-switch + Systems HUD line (`Advantage` = best/current bonus ratio).
