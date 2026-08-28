# BloodPlanner (`Managers/BloodPlanner.cs`)

Blood Magic planner: Iron Pill cast timing + investment-spell routing from breakpoint math, all
live game reads. Executor is `BloodMagicManager` (via AdvisorApply's `blood` toggle).

## Game-truth formulas (decomp)

- **Iron Pill** effect = `floor(blood^0.25)`, ×`ironPillBonus()` on Evil+, display-capped 1e8.
  Grants FLAT base Adventure Power/Toughness (`adventure.attack/defense += num` — gear
  multipliers then scale the summand). Breakpoints: next power point needs `(e+1)^4` blood.
- **NUMBER** (`RebirthPowerSpell`): `rebirthPower += blood` — LINEAR, uncapped, a straight
  multiplier on the whole next-run attack/defense multi; re-based to 1.0 every rebirth.
- **Counterfeit Gold**: `1 + floor((log2(b/min)+1)²)/100` % GPS — LOG, **NO game cap**
  (user-corrected; an old "<100 %" cutoff discredited Counterfeit far too early). Needs TM base
  gold to multiply.
- **Spaghetti**: +1 % drop chance per DOUBLING of invested blood — LOG.
- **All three investment pools are WIPED at rebirth** (`bloodMagicController.reset()`) — an
  earlier comment claimed they persist; they do not. Only NUMBER leaves anything behind (the
  multi banked by `setNewMultis()` a moment earlier).
- The game's auto-spells split blood EVENLY among enabled toggles every second → enabling several
  DILUTES them → **single-sink routing**: exactly one toggle on at a time.

## Pill decision rules (each labeled with its origin)

- **Worth gate**: yardstick is BASE `adventure.attack`, not `totalAdvAttack` — measuring against
  the gear-inflated total made the pill look worthless long after it stopped being so
  (user-caught). Threshold `BloodMagicManager.PillWorthFraction`.
- **Unreachable-this-run**: cooldown outlasting the TRUE time to the scheduled rebirth
  (`RunLeftSeconds`, NOT the ≥10 min-clamped `RunHorizonMinutes`) → don't pool (user-reported:
  magic was poured into blood for a pill that could never cast).
- **Pooling horizon 1 h** (user rule): the pill is a live blood consumer only inside the final
  hour of its cooldown; earlier ritual feeding is pure NGU-magic loss. Pool window opens 15 min
  before ready while autos drain (`poolStart = cdLeft − 900`).
- **Cast-now logic**: two-plan comparison — cast now + brew a second pill vs hold for one bigger
  cast; pills are flat adds so casts SUM (`(T/CD)^0.75` favors frequent casts). Also cast when
  the next breakpoint can't be reached before rebirth. Mirrors the caster's fail-safe (first
  30 min hold, refuse casts < 10 % of base adv power) so "CAST NOW" is never advertised for a
  cast the caster will refuse.
- **Magic-cap growth sampler**: EMA of relative cap growth/s (60 s windows) — ritual bps grows
  with cap over the run, so pooled-blood projections use `PoolOver(t0,T)` with the measured rate.
  Statics reset on reload → growth reads 0 for the first minute (conservative).

## Routing priority (`FillRouting`; game gates auto-spells until boss 37)

1. **Pool for pill** — only when the advisor owns blood (`AdvisorBlood && CastBloodSpells`,
   mirroring ApplyBlood's gate), pill worthwhile, reachable, cd < 15 min.
2. **NUMBER floor**: `BloodNumberThreshold` is a FLOOR, not a ceiling — below it NUMBER outranks
   the in-run sinks. (Old code stopped at the target, capping a linear uncapped multiplier AND
   cutting ritual funding for the rest of the run.)
3. **Gold** — the user allows it (`BloodWantCounterfeit`) and its bonus is under the user's
   ceiling (`CounterfeitThreshold`), the investment window is open (first 50 % of the run; log
   sinks must earn back before the wipe), TM has base gold, gold demand exists (augs ×2 hysteresis
   OR digger upgrades), and the next +1 % is within ~20 min of full income (`GoldBelowKnee`).
4. **Spaghetti** — allowed + under `SpaghettiThreshold`, and zone-farming below the zone's
   `RecommendedDcPercent` (GoldCBlockMode only).
5. **NUMBER default sink** — rebirth scheduled and not NORB; the rebirth force-cast banks
   leftovers anyway, so routing early costs nothing.
6. **All off** — NORB / no rebirth: nothing to bank; keep rituals from draining the marathon.

## `BloodMatters()` — the deadlock fix

The auto profile funds BR-30 rituals only while blood has a live consumer. This must answer with
the routing INTENT, not the toggles ApplyBlood last wrote (throttled 60 s, lag up to a tick):
intent-reads broke a real deadlock — NUMBER gated behind a default-0 threshold → no live toggle
→ no rituals → no blood → NUMBER stuck at 1.0 forever. When the advisor does NOT own blood, the
live toggles ARE the intent. Cached 10 s; fail-safe returns true (keep rituals).

## User targets — permission and ceiling (2026-08-28)

Neither log sink is capped by the game, so once one wins the routing it holds the pool for the rest
of the run. The two Systems > BLOOD fields are now that ceiling:

- **Checkbox** (`BloodWantSpaghetti` / `BloodWantCounterfeit`) = permission. Unchecked -> never routed.
- **Number** (`SpaghettiThreshold` / `CounterfeitThreshold`) = ceiling in %, **0 = no ceiling**
  (mirroring `BloodNumberThreshold`'s 0 = no floor). Reached -> the sink drops out of the routing.
- Inside what they allow, every existing gate still decides — the targets FILTER the candidates,
  they do not override the math.
- Both defaults are **true**: a settings file written before these existed routed gold/loot freely,
  and a false default would silently switch a sink off on upgrade.
- NUMBER carries no checkbox: it is the FALLBACK branch, so "off" is not a state it can be in, and
  its number stays a FLOOR.

`CounterfeitPercentNow()` / `SpaghettiPercentNow()` read `bloodMagicController.goldBonus()` and
`lootBonus()` — the same values Main's manual `AutoSpellSwap` path uses, so a target means the same
thing in both modes, and BloodPanel renders its rows through them rather than recomputing.

**Why this existed as a bug (user-reported):** in ADVISOR mode the two % fields were read by nothing
at all. Their only reader is `Main.cs`'s `if (Settings.AutoSpellSwap && !Settings.CastBloodSpells)`
branch, which is dead whenever automation is on — so the panel offered two knobs, plus a lit-green
Auto Spell Swap button, that could not affect anything.

**Rejected, with evidence (2026-08-28):** feeding BR rituals from BB-capped magic. The idea was that
when the magic lanes hit their blitz-boost ceiling the surplus idles, making rituals free, so
`ChallengeOverlay`'s `bloodMatters` gate should not drop BR-30 there. Every `[WandoosDbg] magic`
sample in the 2026-08-24 session reports `bb out of reach` with `held` 4x-136x below `bb`, and the
two `STOOD DOWN` lines are both `energy` — which rituals do not consume. There is no such surplus at
this scale; revisit only if the magic cap grows an order of magnitude.
