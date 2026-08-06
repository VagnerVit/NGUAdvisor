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

## `DumpWorthwhile(energy, alloc, minMultiplier = 10)` — the lane's right to exist

`WandoosBP.TargetMet()` asks this instead of the old hardcoded `false` that made the lane a
leftovers black hole (AllocationProfiles.md §WAN). Projects `alloc` at full-boot speed on the
CURRENT OS and answers whether the resulting levels clear `minMultiplier` A/D.

**Bosses gained = `log10(A/D multiplier)`** — boss requirements grow ~10× per boss (`bossAttack`
1.98e72 at boss 74 vs 1.98e77 at boss 79), the same arithmetic AllocationProfiles.md uses. Hence
the 10× default = one boss = the smallest unit of progress the A/D lever exists to buy.

Two deliberate choices, both forced by the dump being **wiped at rebirth** — the question is "should
this run carry a Wandoos lane at all?", which has one answer per run:

- **Whole run, not the remainder** (`RunSeconds()`, same 10 m–4 h clamp / 120 m default as
  `RunHorizonMinutes()`).
- **From level 0, not marginally over banked levels.** A marginal read retires the lane ~30 s after
  the rebirth on any concave bonus: at 1 805 levels/run the break-even sits at **8 banked levels**,
  however well the lane pays over the run. Measured during implementation, not assumed.

**Why not a fixed rate/allocation threshold** (the obvious cheaper design): levels-per-10× spans
three orders of magnitude across the OSs — 98 needs ~1 678 energy levels, MEH ~45, XL ~2 — so a
constant tuned on 98 silently retires a good MEH/XL dump. `baseTime` moves the opposite way
(1e9/1e12/1e15), so only the full formula gets both right.

Verified numerically against the ch.3 Normal measurement in AllocationProfiles.md (cap 5 571 250,
speed 3.0):

| case | levels/run | bonus | bosses | verdict |
|---|---|---|---|---|
| 98, `CAPWAN:30`, 2 h | 1 805 | 10.6× | 1.02 | lives (just clears) |
| 98, `CAPWAN:30`, 4 h | 3 610 | 18.0× | 1.26 | lives |
| MEH, `CAPWAN:30`, 2 h | 2 | 1.4× | 0.13 | retired |
| XL, `CAPWAN:30`, 2 h | 0 | 1.0× | 0.00 | retired |
| 98 Evil (`baseTime` 1e21), cap 1e9 | 0 | 1.0× | 0.00 | retired |

Any unreadable input (no character, `boot < 0.02`, exception) answers **TRUE** — never retire a lane
on a failed read. `WandoosBP` additionally skips the whole check when
`OptimizationAdvisor.WandoosIsPowerSource()` is true (challenge block / NORB / NOAUG /
gold-starved), where the dump earns its lane however slow it looks.

Consumers: the Wandoos auto-switch + Systems HUD line (`Advantage` = best/current bonus ratio), and
`WandoosBP.TargetMet()` via `DumpWorthwhile`.
