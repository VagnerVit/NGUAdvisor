# BloodPlanner (`Managers/BloodPlanner.cs` + `Managers/BloodRouter.cs`)

Blood Magic planner: Iron Pill cast timing + investment-spell routing from breakpoint math, all
live game reads. Executor is `BloodMagicManager` (via AdvisorApply's `blood` toggle). The blood
BUDGET itself lives in `BloodRouter.Plan` — Unity-free and unit-tested (`BloodRouterTests`);
BloodPlanner only gathers the game reads into a `BudgetInput` and maps the plan to the toggles.

## Game-truth formulas (decomp)

- **Iron Pill** effect = `floor(blood^0.25)`, ×`ironPillBonus()` on Evil+, display-capped 1e8.
  Grants FLAT base Adventure Power/Toughness (`adventure.attack/defense += num` — gear
  multipliers then scale the summand). Breakpoints: next power point needs `(e+1)^4` blood.
- **NUMBER** (`RebirthPowerSpell`): `rebirthPower += blood` — LINEAR, uncapped, a straight
  multiplier on the whole next-run attack/defense multi; re-based to 1.0 every rebirth.
- **Counterfeit Gold**: `1 + floor((log2(b/min)+1)²)/100` % GPS — LOG, **NO game cap**
  (user-corrected; an old "<100 %" cutoff discredited Counterfeit far too early). Needs TM base
  gold to multiply.
- **Spaghetti**: `1 + floor(log2(b/min)+1)/100` drop chance — +1 % per DOUBLING, 1 % at the minimum.
- **All three investment pools are WIPED at rebirth** (`bloodMagicController.reset()`) — an
  earlier comment claimed they persist; they do not. Only NUMBER leaves anything behind (the
  multi banked by `setNewMultis()` a moment earlier).
- The game's auto-spells split blood EVENLY among enabled toggles every second → enabling several
  DILUTES them → **single-sink routing**: exactly one toggle on at a time.
- Pre-rebirth top-up (`BaseRebirth.CastBloodSpellsForRebirth`) skips float residue below 1e-6 of
  `rebirthPower`: the cast gains exactly `blood / rebirthPower` and costs a whole re-allocation pass.

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

## The blood budget (`BloodRouter.Plan`, 2026-09-23)

The run's blood — in the spells, on hand, and the income still to come by the rebirth (`bps`,
growing with the magic cap) — is split **equally** between the enabled spells (user decision
2026-09-23, after a marginal-value version priced gold only through augments and drop chance at 0,
so it routed everything to NUMBER). Blood already cast cannot be taken back, so a spell above the
level keeps what it has and the rest is levelled across the others (water-filling, `Level`).

- **Whole steps only.** Both bonuses are floored by the game, so blood between two thresholds buys
  nothing: each in-run share is snapped DOWN to the last whole +1 % it pays for, and the remainder
  goes to NUMBER, where every point counts.
- **Exact casts, toggles off.** `BloodPlanner.Spend` casts with `castGoldSpell/castLootSpell(amount)`
  only once the pool covers the whole next step, and `castRebirthSpell(pool)` once both in-run spells
  hold their share. The game's auto-spell toggles stay off: they cast the whole pool every second,
  into a step not yet paid for. The ritual gate (`ChallengeOverlay`) therefore reads
  `BloodMatters()` intent — a toggle read would see "nothing live" and drop the rituals.
- **Order**: the in-run spell with the cheaper next step first, then NUMBER (time-indifferent; the
  rebirth force-cast banks whatever is still pooled). An Iron Pill cast takes the whole pool.
- **No ceilings, no floor, no Push.** `CounterfeitThreshold`, `SpaghettiThreshold`,
  `BloodNumberThreshold` and `BloodPush*` are no longer read in advisor mode (Main's manual
  AutoSpellSwap path still reads the three thresholds). The dropdown is Off / Equal share.
- **Known cost of the rule**: NUMBER is linear and multiplies the whole next run, the other two are
  log bonuses for this run, so a third each gives up roughly two thirds of the next run's NUMBER for
  a small bonus gain. The user chose it knowing that.
- **Blood income depends on rituals being funded.** In the auto profile `BR-30` sits LAST in the
  magic list, after NGU lanes that take the whole pool, so outside the augment hour `bps` is 0 and
  the budget has nothing new to split (open, 2026-09-23).

### Rebirth outlook — "is one coming", not "when"

`NUMBER` is only worth banking if a rebirth will cash it: not NORB, and either the profile
arms an entry (`CustomAllocation.RebirthArmed`, `RebirthTime >= 0`) or money-pit run mode is on — or Auto Rebirth is off, because then the player rebirths by hand. A
Number/Bosses entry without a `Time` key parses to `RebirthTime 0` — armed, no clock — which the old
`NextRebirthTargetSeconds() > 0` test read as "no rebirth" and idled blood on CBlock profiles. Without
a clock the plan projects one hour (`UnclockedHorizonSec`, BestAug's default) — an assumption.

## `BloodMatters()` — the deadlock fix

The auto profile funds BR-30 rituals only while blood has a live consumer. This must answer with
the routing INTENT, not the toggles ApplyBlood last wrote (throttled 60 s, lag up to a tick):
intent-reads broke a real deadlock — NUMBER gated behind a default-0 threshold → no live toggle
→ no rituals → no blood → NUMBER stuck at 1.0 forever. When the advisor does NOT own blood, the
live toggles ARE the intent. Cached 10 s; fail-safe returns true (keep rituals).

## User targets — permission, push and ceiling (2026-08-28, extended 2026-09-12)

Neither log sink is capped by the game, so once one wins the routing it holds the pool for the rest
of the run. The two Systems > BLOOD fields are now that ceiling:

- **Dropdown** = intent, stored as two flags: `BloodWantSpaghetti` / `BloodWantCounterfeit` =
  permission, `BloodPushSpaghetti` / `BloodPushCounterfeit` = push. Two flags rather than one enum so a
  settings file written before Push reads back as the Off/Auto it already meant.
- **Number** (`SpaghettiThreshold` / `CounterfeitThreshold`) = ceiling in %, **0 = no ceiling**
  (mirroring `BloodNumberThreshold`'s 0 = no floor). Reached -> the sink drops out of the routing.
- Superseded 2026-09-23: the numbers and Push are no longer read in advisor mode (see the budget).
- Both defaults are **true**: a settings file written before these existed routed gold/loot freely,
  and a false default would silently switch a sink off on upgrade.
- NUMBER carries no checkbox: it is the FALLBACK branch, so "off" is not a state it can be in, and
  its number stays a FLOOR.

`CounterfeitPercentNow()` / `SpaghettiPercentNow()` read `bloodMagicController.goldBonus()` and
`lootBonus()` — the same values Main's manual `AutoSpellSwap` path uses, so a target means the same
thing in both modes. BloodPanel renders its rows from the plan (`Plan.Budget`), never recomputing.

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

## `BloodNumberThreshold` must be FINITE (2026-09-12)

Found in a live `settings.json` as `NaN`, and in that morning's log as a floor of `1e+308`
(`NUMBER (below floor 1e+308)`) — a floor that can never be reached, so NUMBER owned the routing
unconditionally and no other sink could ever run. The panel's free-text box took any double and
round-tripped huge values back through `ToString("0")`. Guarded in three places now: the panel's
parse, the `SavedSettings` setter, and the load-time predicate (which rejects Infinity explicitly —
NaN already fails `>= 0`, Infinity does not).
