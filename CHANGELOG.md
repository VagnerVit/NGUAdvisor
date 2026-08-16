# Changelog

All notable changes to NGU Advisor are documented in this file.

## [Unreleased]

## [1.2.30] - 2026-08-16

Existing settings and profile files remain compatible with version 1.1.0.

The theme is **one owner per setting**. Two of the fixes below are the same bug wearing different
clothes: a setting written by two modules that did not know about each other, and a spend allowed by
two checkboxes neither of which could stop it. Both were only visible live — one as a toggle that
flickered between types every half minute, the other as EXP draining while the user was banking it.

### Added

- **Auto Transform is now the advisor's (Boosts > Transforms).** The game rerolls every dropped boost
  into one chosen type; the right type is the one whose sinks still have room, and that moves as gear
  fills up. Five choices — `Advisor | P | T | S | X` — with the note under the strip always naming
  the type actually being written into the game, so Advisor mode is never a black box. Hidden behind
  the same 100-level challenge completion the game hides its own toggles behind.
- **ITOPOD floor mode (Adventure > ITOPOD): Optimal / Fixed / Max.** "Fixed" holds the floor you type
  and stops the game's Lazy ITOPOD drifting off it; "Max" is the old Auto-Push. Pushing survives as
  the underlying permission flag, so a death during a push revokes the climb without discarding the
  mode you chose — and a fixed target keeps farming the highest floor it reached.
- **"What drops here, and how far level 100 is"** on the ZONES and ITOPOD pages: the boost id this
  spot actually drops, drops held against the 101 a level-100 copy costs, and the ETA at the current
  kill rate. All from the decomp — a boost lands at level 0, a merge is `level + level + 1` capped at
  100, and the game refuses every merge of an id once it maxes.
- **Unload without touching the game: drop `unload.request` in the NGUAdvisor data folder.** The
  advisor notices it on the Unity thread and tears itself down. This exists because both `smi.exe
  eject` modes killed a live game — teardown on the injector's thread, or the assembly unloaded out
  from under a still-running MonoBehaviour. An outside caller may leave a request; it may never touch
  Unity state.
- **UI crashes now leave a stack trace.** WinForms answers an unhandled exception in a handler by
  tearing the window down, and Mono's driver logs nothing at all — the advisor kept running with its
  window simply gone and a clean log (user-reported). Two handlers now catch that.

### Fixed

- **The advisor transformed boosts into the one type the gear could not use.** It priced types against
  the cube, which accepts Power and Toughness equally, so below the softcap all three tied and the
  first branch tested won: with gear headroom `P=18700 T=0 S=564` it sat on Toughness. Gear decides
  the type now; the cube only breaks a tie, since it cannot tell P from T and has no Special channel
  at all.
- **Two modules were writing the auto-transform setting.** `InventoryManager.ManageBoostConversion`
  had owned it silently through the game's own setters, so once the user-facing control landed the
  two overwrote each other every ~30 s ("T jumps for a moment, then P overrides it"). There is one
  writer now, and the module doc says so.

### Changed

- **"Buy E/M (EXP)" is gone.** It spent EXP on custom energy/magic/R3 using amounts that
  `ExpBalancer` itself writes — one spender deciding for the other — and switching it off did not
  stop the advisor's own EXP buys, so EXP kept draining while the user banked for a digger. The
  ADVISOR toggle on the EXP row is the single control now; off means EXP banks.

## [1.2.29] - 2026-08-12

Existing settings and profile files remain compatible with version 1.1.0.

This release is about a single theme: **when the advisor decides something, you can now see who
decided it and why.** Every gap below came from a real session where the advice was right but its
basis was invisible — or where advice was given from outside the module that owns the answer, and
was simply wrong.

### Added

- **NEXT BUY (Economy > Planners)** — one row per currency (AP, PP, QP, seeds, EXP): what it is
  saving for, what that costs, whether you can afford it, and **which module decided**. The page
  holds no ordering of its own; every row is fetched from the owner. This exists because advice
  sourced from the guide instead of the owning module recommended a digger slot for 100k AP, while
  the AP planner's queue said a heart was next and the digger slot was in fact a **25 PP perk** —
  wrong currency, wrong price by four orders of magnitude.
- **Farm rates are stated as the purchase they bring forward.** "1.2k PP/hr" becomes "Faster NGU
  Energy in ~3h" on the Adventure panel and in the routing log. There is deliberately no scalar
  converting PP against boosts and EXP — the exchange rate between them is phase-dependent, so any
  constant would be wrong for half a run.
- **EXPORT STATE (Logs page)** — dumps the live game state to `state-export.txt`: progression,
  balances, NGU levels *with their allocation*, AT, augments, diggers, beards, and the owned perks,
  quirks and fruits **by name**. Those names live in the Unity scene, not in code and not in the
  save file, so no external save reader can produce them.
- **The profile recommendation now considers your own profile files**, not only installed presets.
  The preset decides which *kind* of run this is; a file on disk of the same kind that funds more of
  the plan's NGU lanes wins, and the recommendation carries the lane count. It has to beat the
  preset, not tie with it.

### Fixed

- **Gear respawn past 80% was scored as if it still helped.** The game floors the respawn factor at
  0.2, so it does not. The optimizer was paying real accessory slots for points that do nothing in
  game. (A deliberate divergence from the reference optimizer, which also scores it linearly.)
- **NGU levels were counted on the wrong difficulty track.** The growth chip summed the plain level
  field, so on the Evil track it read a flat `+0/hr` against a nonzero prediction while the run was
  in fact climbing.
- **Loading a save registered as a gain.** On the title screen every balance reads as a fresh
  character's zero, so the first sample after a load jumped the entire account into the rate —
  measured as `NGU +10.1K/hr` against a predicted `44.9`. It is now caught the way a rebirth already
  was, by the run clock disagreeing with the wall clock.
- **A profile edited outside the editor was never structurally checked** on the path the game
  actually loads from. SimpleJSON does not throw on a malformed profile, it misparses silently, so
  the run could allocate to something the file never said. It now reports and still loads.
- **The autopilot divider ran past the bottom of its card** whenever the content settled shorter
  than its fixed height.

### Changed

- **`NGU LEVELS +0/hr` now names its cause.** Read live from the game rather than inferred: a lane
  only ticks while it holds allocation and is under its target, so the tile says which of those is
  failing ("no NGU allocation", "fed elsewhere: …", "at target: …") instead of printing a prediction
  the measurement plainly contradicts.
- **Decisions that override other decisions say so.** The zone line reports the layer that actually
  decided and what it overrode; the digger line says whether the order came from the advisor or the
  profile (the profile's digger list is *not* consulted while the advisor owns diggers); the Loot
  Hunter pool logs the ids it discarded. New `[SpendDbg]`, `[ProfileDbg]` and `[GrowthDbg]` channels
  follow the same rule — including when the answer is "nothing changed", because a silent module is
  indistinguishable from one that never ran.
- **Adventure gear scoring halves the Toughness weight** (`Power¹ × Toughness⁰·⁵`). Kill rate is
  linear in Power and Toughness only has to clear a survival threshold; equal exponents priced a
  point of Toughness about 2.5× above a point of Power, which is backwards.

## [1.2.27] - 2026-08-07

Existing settings and profile files remain compatible with version 1.1.0.

### Fixed

- **The ITOPOD floor formula overshot, and it overshot worst exactly where you use it hardest.** The
  advisor solved the one-shot floor as if the enemy's defense shrank along with your attack multiplier.
  It does not — the game subtracts defense *before* multiplying — so the stronger the rotation, the
  higher the floor it claimed you could hold: about three floors too high on a full Offensive buff
  stack, ten at very high multipliers. Those are floors where kills silently stop being one-shots and
  your kill rate quietly halves. The floor is now solved from the game's own mob table and damage
  formula.
- **Piercing attacks were priced with the wrong multiplier when ranking zones.** The game's piercing
  attack multiplies by the Strong Attack multiplier, not the Piercing one; zone kill-speed estimates
  used the latter, so zones and ITOPOD were being compared on two different scales.

### Changed

- **ITOPOD is no longer valued on boosts alone.** Its boost drops stop improving at floor 1150 and its
  AP at floor 950, but EXP keeps growing quadratically and PP keeps growing with every floor up to
  1600 — so the old boost-only reading saw a plateau that is not there and routed away from the pod
  too early. The advisor now prices PP, EXP, AP and boosts separately, and averages each over the
  attack rotation the pod actually runs (the floor is re-picked between kills, so a single-floor
  estimate was never what happened). When the advisor parks you in ITOPOD *because nothing is
  consuming boosts*, it now picks the combat mode on PP rather than on boosts, and logs the PP and
  EXP rates with the floor band. The Adventure panel shows them too. AP is modelled but neither
  displayed nor used to decide anything — an AP award is always exactly 1, so it cannot tell two
  floors apart the way PP and EXP can.
- **The ITOPOD buff burst is no longer switched off where it pays best.** The floor-jump burst in
  optimize mode 3 stopped firing above tier 20 with a fast respawn, because that is where the AP
  reward interval bottoms out. But the EXP award lands on the very same kill and keeps growing
  quadratically with the tier, so the burst was being disabled exactly where it earned the most. It
  now fires whenever it reaches a higher reward tier than the floor being farmed.

## [1.2.26] - 2026-08-06

Existing settings and profile files remain compatible with version 1.1.0.

### Fixed

- **Automatic transform climbing destroyed padlocked items.** A transform consumes the item and mints
  the next tier at level 1, and the game itself refuses to spend a padlocked copy on that — but the
  advisor's climb never checked the padlock. Worse, merging deliberately funnels copies *into* the
  padlocked one so it survives, which means the padlocked copy is the one that reaches level 100 first
  and then got eaten: a protected maxed Ascended Forest Pendant was consumed to create a level-1
  Ascended x2 while a weaker unprotected copy was kept. The padlock is now an absolute veto on climbing,
  outranking the "keep one maxed copy" slot heuristic — so padlocking a copy is a reliable way to pin
  exactly the one you want kept.

- **Farming in Idle mode was costing you up to half your kills, and the advisor could not see it.**
  Idle attacking restarts its attack timer every time an enemy spawns, so every single kill waits a full
  attack cycle before the first swing lands — whereas a manual mode swings on the frame the enemy
  appears. With respawn gear that is a factor of two in boosts, gear drops and PP per hour. Both farm
  advisors now measure kill speed per zone and per combat mode, report which mode they costed their
  recommendation at, and automatic zone routing switches Adventure combat to it.
- **Pretty Pink Princess Land was over-valued for boost farming.** Its two boost rolls have different
  drop-chance ceilings (8% and 6%); we used 8% for both, over-stating the zone by up to a quarter at high
  drop chance.
- **Boost farming demanded far more Power than it needed.** The requirement was "one-shot the zone boss",
  but bosses do not drop boosts at all — only ordinary enemies do. Zones now qualify on killing the
  enemies that actually pay, which unlocks earlier zones roughly 1.8x sooner.
- **The zone table's one-shot power was wrong for every zone past Ancient Battlefield — badly wrong in
  late Evil.** It turned out to hold three different quantities: the reliable one-shot power up to The
  2D Universe, the best-case-luck one from Ancient Battlefield to The Fad-lands (a factor of 1.5 too
  low), and from JRPGVille onward not a one-shot number at all but the idle-survival power — a factor
  of 10 to 18 too low, e.g. The Rad-Lands claimed a one-shot at a twenty-seventh of the real
  requirement. Anything that asked "do I one-shot this zone?" now measures it from the game's own enemy
  data instead. The most consequential fix is post-fight recovery: it used to skip healing to 20% HP in
  zones it wrongly believed were trivial, and it now also credits manual combat's stronger swing, so it
  wastes less time in the Safe Zone where that is genuinely safe. Your own `zoneOverride.json` values
  still take precedence.
- **Zones you kill in two hits were invisible to both farm advisors.** They were excluded outright rather
  than costed, so a zone with far better drops could never be recommended just because it needed a second
  swing. They now compete on their real rate, and a zone whose enemies out-regenerate your damage is
  correctly reported as unfarmable instead of being silently treated as fast.

- **"Bosses Only" silently made boost farming pointless.** Bosses drop no boosts at all — only ordinary
  enemies do — so with that toggle on an adventure zone yields exactly nothing. The advisor now accounts
  for it, routes to the Pod (which the toggle does not affect) and says why. Blacklisted enemies are
  likewise counted as the Safe-Zone round trip they cost rather than ignored.

### Changed

- **Fights that take more than one hit are no longer over-estimated.** Kill times were computed as if
  every single swing rolled its worst possible damage, which inflated a ten-swing enemy to thirteen
  swings, and manual combat was priced as if it only ever used the regular attack. Longer fights now use
  average damage and the real attack rotation, so zones that need a second swing are ranked on what they
  actually do. Anything that decides whether you *reliably* one-shot still uses the pessimistic number —
  that call must not rest on a lucky roll.
- **A boost drop is now valued by what it can actually absorb.** Power and Toughness boosts are clamped
  to the item they land on and the overflow is destroyed, so a 10,000 boost dropped when your hungriest
  item needs 300 was never worth 10,000. The advisor now prices every drop against your real gear
  headroom, credits the Infinity Cube with its diminishing-but-never-zero returns instead of writing it
  off at the softcap, counts Special boosts' ability to spill across all three special slots, and adds
  the expected value of the Boost Recycling chain. Boost-farm figures are now shown as boost points per
  second rather than per kill.
- Enemy-mix assumptions are gone: the share of ordinary enemies per zone is read from the game (it ranges
  from 71% to 81%, not the flat 77% both advisors assumed), as is the boss share, enemy regeneration and
  the downtime paralyzing enemies cost.

## [1.2.25] - 2026-08-01

Existing settings and profile files remain compatible with version 1.1.0.

### Fixed

- **A gear hunt now actually runs the drop chance digger.** The hunt already promoted it ahead of the
  Perk Points digger, but the rule that puts the Adventure digger first ran afterwards and won — so on a
  character with a single digger slot the hunt farmed drops with no drop chance bonus at all. An active
  gear hunt now outranks even the Adventure digger, because farming drops is the entire point of camping
  a stage. Titan windows are unchanged: there the Adventure digger still leads.

### Changed

- **Automatic allocation no longer parks most of your energy and magic in Wandoos.** The Normal
  long-rebirth preset was fixed for this in 1.2.24, but automatic allocation was not: it asked for 40% of
  both caps in most phases and 60% during the NGU marathon, and because a Wandoos lane requests its whole
  ceiling on every pass, that is what it took. The ceiling is now 30% in every phase. The phases where
  Wandoos genuinely is your power source — the No-NGU, No-Time-Machine and No-Augment challenge templates
  and the all-systems-dead fallback — keep their original share on purpose.
- **A Wandoos lane that cannot pay for itself now steps aside instead of holding its share.** The lane
  used to be unable to ever drop out, so it kept its allocation whatever the payoff was. It now projects
  what its allocation would earn over the run and gives the share back unless that is worth at least one
  boss, measured against how much stronger bosses get as you climb. This retires it on every Evil-and-up
  run, where the levels are unreachably expensive, while leaving it running where it pays — and it is
  never retired inside a challenge block, a No-Rebirth or No-Augment run, or when gold is too low to
  maintain augments, since those are exactly the runs Wandoos is the power source for. The projection
  follows each operating system's own bonus formula, so switching between 98, MEH and XL is accounted
  for rather than assumed.

## [1.2.24] - 2026-07-31

Existing settings and profile files remain compatible with version 1.1.0.

### Changed

- **`BESTAUG` now prices an augment over the phase you actually run it for, and against the gold you
  will actually have.** It projected a fixed one hour ahead, which underrates the steep high-tier
  augments during a long augment phase (boost grows faster than linearly with run time) and overrates
  them in a short run; the horizon now ends where augment funding ends — at the next energy breakpoint
  that funds no augment, or at the scheduled rebirth. The projection also stops at what gold can pay
  for, using the run's net gold per second and the game's own level costs, instead of the old check
  that only asked whether one second of the current level was affordable. In practice that stops the
  advisor from committing a whole phase to an augment whose upgrade half runs dry after a few levels.
- **`BESTAUG` no longer feeds energy to a half that has run out of gold.** The energy split between an
  augment and its upgrade followed the boost formula alone, which is right while both halves can pay
  their way — but an upgrade level costs gold in proportion to the square of its level, so the upgrade
  half runs dry long before the augment does, and everything after that point is a full bar waiting for
  gold with the energy behind it doing nothing. The split now shrinks a starved half to the share its
  gold can actually convert and hands the rest to the other half, which is where the last few percent of
  an augment phase were being lost.

### Added

- **Profiles now warn when one energy breakpoint funds several augments.** Augment multipliers add
  together rather than multiply, so energy split between two augments buys less than the same energy
  concentrated in one — but nothing said so, and the profile still loaded and ran. Loading a profile (or
  opening it in the Profile Editor) now names the breakpoint and the augments involved. It is advice, not
  an error: nothing is blocked. Reserving a specific augment with a `CAPAUG` token is not flagged, since
  that is how a run that must level a particular augment — a Laser Sword challenge, say — is written.
- **The Normal long-rebirth preset no longer parks spare energy in Wandoos.** A trailing `WAN` lane
  never releases its share, and on a measured ch.3 run it held 62.5% of the energy cap for the entire
  3h55m rebirth — for a bonus that applies to Fight Boss attack/defense only (so it cannot help kill
  the titan the preset exists to kill) and that is wiped on rebirth anyway. Advanced Training now leads,
  because titans are killed with adventure stats, and the tail goes into the Adventure/Power NGUs so a
  long run leaves permanent progress behind. Challenge blocks where Wandoos genuinely is the power
  source, such as `CBlock2-Normal`, still lead with it.

## [1.2.23] - 2026-07-31

Existing settings and profile files remain compatible with version 1.1.0.

### Fixed

- **"Keep Max Lvl" froze a maxed copy of every tier in a transform chain, not just the one worth
  keeping.** With Climb and Keep Max both on, the first at-100 copy of each tier became the protected
  copy — so a Forest Pendant sat in the inventory reading TRANSFORMABLE forever while the chain was
  already two tiers further along, with 100 levels of merges stranded inside it. Every later merge
  then piled up around that frozen copy instead of climbing. Keep Max now protects only the highest
  tier you actually own, which is the only one whose stats you can wear; lower tiers climb freely.
  Turning Climb off still freezes the whole chain, as before.

## [1.2.22] - 2026-07-30

Existing settings and profile files remain compatible with version 1.1.0.

### Fixed

- **Basic Training left the defense tree starved when the game's "sync training" option was on.** With
  sync enabled, `ALLBT`/`CAPALLBT` only allocated the six attack skills and let the game mirror each
  amount into the matching defense skill. But the mirror copies the *attack* number, and the two cap
  tables shrink independently as levels accumulate — so a Charge that caps at 46 Energy received the 21
  that Piercing Attack needed, and every defense skill trained below capped speed for the whole rebirth
  (the sync path also silently halves the input). The advisor now allocates all twelve skills itself,
  each from its own cap, using the game's non-mirroring energy call. The in-game toggle is untouched and
  still works for manual clicks.

## [1.2.21] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0.

### Changed

- **EXP purchases now follow the guide's ratio for the chapter you are actually in.** One fixed set of
  numbers (an even E/M split, pow:cap:bars 5:160k:4) was used from the first rebirth to the last, so in
  chapters 1-2 the advisor aimed half of every EXP at magic — which the guide does not want bought there
  at all — and used the mid-game power:cap:bars ratio instead of the early `1:37.5k:1`. The targets now
  come from a phase table transcribed from the guide: energy-only through chapter 2 and pre-T5 chapter 3,
  then 5:1 E:M after T5, 3:1 from chapter 4 (2:1 between T6v2 and T6v4, as before), and `4:150k:1` from
  chapter 7. The advisor row and the Growth EXP tile name the phase they are working toward, and the
  magic custom-purchase boxes are left at zero in the phases that do not want them.
- **A small EXP bank is no longer unspendable.** Two guards compounded into a dead zone: a flat "under
  100 EXP, skip" floor meant a 959 EXP bank offered a 95 EXP tick budget and bought nothing, forever —
  and because nothing was ever spent the bank never grew past the floor either. Even above it, the
  waterfill could slice a budget into per-stat crumbs that each rounded down to zero units. The floor is
  now the cheapest unit actually on offer, and if the walk buys nothing the whole budget goes to the
  most-behind stat one unit is affordable for. Waiting was never an advantage: a purchase is an instant,
  permanent stat.

### Fixed

- **Adventure regen was priced but never bought.** With `Buy Adventure (EXP)` on, the regen cost was
  added to the EXP needed per purchase cycle — lowering how many cycles were affordable — while the buy
  itself was skipped, because the loop never called it and the log line was gated on the HP flag. (The
  game spells that method with a lowercase `r`.)

## [1.2.20] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0.

### Changed

- **The gold snipe now checks whether it can pay off before swapping gear.** The Time Machine runs on
  the single highest gold drop of the rebirth and discards every smaller one, so once a titan has banked
  a drop, re-fighting the best zone in Gold Drops gear buys nothing — it only costs the Power and
  Toughness that gear is not carrying. The advisor predicts the drop of the zone's boss from the game's
  own tables (base gold × the worst multiplier roll × the gold bonus the gold set would have) and skips
  the snipe unless it beats what the machine already holds. The Gold panel shows `NO GAIN` with both
  numbers instead of claiming the snipe is complete, and the "new zone fightable" and gold-starvation
  triggers no longer re-arm a snipe that cannot win.
- **A new re-snipe trigger, "gold drop improved":** the gold bonus keeps growing during a run (NGU
  Gold, cube, new gear), so a snipe that was pointless earlier re-arms by itself once the predicted drop
  clears the banked one by 25 %, with no new zone needed.
- **Titan gold banking now repeats on every autokill cycle.** Previously one bank per titan per run
  (re-banked only when its autokill version rose), which meant an auto-killed titan went down in loot
  gear for the rest of the run and its drop was thrown away. The kill happens whether the advisor swaps
  or not, so there is nothing to weigh against: the gold set goes on for every autokill of the highest
  AK titan, and each cycle is another shot at a bigger drop as the gold bonus grows.
- **Drops are predicted at the top of the game's multiplier roll, not the bottom.** The game rolls
  4–5× on every gold drop, and the number being compared against — the Time Machine's banked drop — is
  a realized roll. Predicting at 4× made an identical kill look 11 % worse than itself, so nothing could
  beat its own previous drop (a titan predicted 14.5M against the 22.5M bank it had produced itself).
  Over-predicting costs one gear swap; under-predicting costs the run's gold production.
- **Safety around that swap:** autokill thresholds are live-stat checks and a gold set spends the very
  stats they measure. After the gold set goes on, the autokill is re-tested; if it was lost, the titan
  set goes straight back on (rather than waiting out the ten-minute watchdog) and that titan's gold swap
  is skipped for the next thirty minutes.

## [1.2.19] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0.

### Fixed

- **The keyboard hint under the priority list keeps its last word.** It read `… · Del remov`, because the
  text measured as fitting and then painted past the column edge — the renderer does the cutting, so no
  ellipsis appears and the sentence simply stops. Shortened to sit comfortably inside the column.

### Changed

- Release packaging needs no environment variables. The injector binaries moved out of `dist/` (where
  every cleanup destroyed them) into a git-ignored `tools/injector/`, sample profiles come from the
  source tree, and `--inject` builds straight into the running game without producing a zip. See
  BUILD.md.

## [1.2.18] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0.

### Fixed

- **Long system names no longer collide with the AUTOMATION caption** in the system index — `YGGDRASIL`
  painted over it and printed its `A` as `ʌ`. The column was positioned using a measured longest title
  plus a small pad, and the pad was smaller than the amount the renderer exceeds the measurement by.
- **Truncated text gets its ellipsis** where the renderer would otherwise clip it mid-word (a status line
  ending `…and the abando`). Fitting now works against a slightly narrower budget than requested. The
  measurement itself is deliberately unchanged: widening it would move every auto-sized control.

## [1.2.17] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0.

### Fixed

- **The layout audit's horizontal-overflow check now recurses.** It inspected only a section's direct
  children while every panel nests its content, so the one rule that catches content running past the
  right edge could not see the controls it exists for, and reported clean on the pages where it happens.

## [1.2.16] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0.

### Fixed

- **Number fields are easier to hit.** A `NumericUpDown`'s spin arrows are half its height each, so a
  control sized exactly to the text line gave two ~19px arrows on a 200% display — the reported "fields
  are small and hard to click". They now carry a deliberate allowance above the line; rows that hold one
  derive their height from it and follow.
- The digits inside a number field are centred instead of pinned to the top of the control.

### Changed

- **The layout audit stops crying wolf about number fields.** Four releases were spent trying to size the
  inner edit box of a `NumericUpDown` (1.2.7, 1.2.12, 1.2.13, 1.2.15). It cannot be sized: it is a
  single-line `TextBox`, and in both WinForms and Mono the font owns its height. The startup probe proves
  it in the log — `num inner 32` against a 38px line, on a control the advisor builds and stretches
  itself. The audit now holds the OUTER control to the rule, where the height is real and where the click
  target lives, and says so where the rule is written so the next reader does not re-fight it.

  This is a narrowing of the oracle, not a silencing: a genuinely short number field is still reported —
  through the control a panel actually created, which is also the one a fix can change.

## [1.2.15] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0.

### Fixed

- **Number fields, fourth attempt — and this time the log says which control was wrong.** The audit's ten
  `UpDownTextBox h=32 < 38` findings survived 1.2.7 (state an explicit height), 1.2.12 (measure the chrome)
  and 1.2.13 (turn AutoSize off), because all three sized the OUTER control — and the outer control was
  never the problem. The startup line proves it: `num chrome 4`, meaning the height was accepted and the
  client area is the full line. The inner edit box simply does not keep the height it is given, because
  Mono re-runs the control's own layout afterwards and there is no Resize to hang a re-apply on. The
  stretch is now re-applied on `Layout`, which fires after that pass.
- The startup `UI metrics:` line also reports `num inner`, the height the inner box keeps on a control the
  advisor owns. If a report ever shows a short field again, that number says whether the stretch failed or
  something else undid it — three of these rounds were spent inferring the mechanism from the audit alone.

## [1.2.14] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0.

### Added

- **Cube only.** A tick box on the Boosts page sends every boost to the Infinity Cube and parks the
  priority list. The Cube dropdown beside it only ever chose HOW the cube is fed once boosts reach it;
  there was no way to say that it should get them instead of your gear. Merges, filters and convertibles
  keep running — a switch labelled "cube only" turning those off would be a surprise. While it is on the
  live readout says the list is parked rather than showing an empty list next to a full one.

## [1.2.13] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0. Number fields, third attempt —
this time against the actual cause.

### Fixed

- **Number fields fit their text.** `AutoSize` was the answer all along. `UpDownBase.SetBoundsCore` runs
  `if (AutoSize) height = PreferredHeight`, so a `NumericUpDown` silently DISCARDS every height assigned
  to it and keeps the one it derives from `Font.Height` — the 96-DPI value. Both earlier attempts were
  writing to a property the control threw away: 1.2.7 stated an explicit height, 1.2.12 measured the
  chrome and stated a bigger one. The log said so plainly — `num chrome 4` where the real chrome is 9,
  because even the measuring probe had been resized behind its own back. `AutoSize` is now turned off
  before the height is set, on the control and on the probe.
- The IDs row on a Loadouts card no longer clips its text box by a pixel. The row was a scaled `S(28)`
  = 43px holding a box floored at the measured line, which ends at 44; it is now derived from where its
  children actually end, like the lists and cards already were. The box also states its height instead of
  letting Mono choose one.

## [1.2.12] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0. Number fields are the right
height on a scaled display — for real this time.

### Fixed

- **Number fields fit their text.** The layout audit had ten standing findings across Loadouts, Blood,
  Gold, Pit, Yggdrasil and Quests — every `NumericUpDown`'s inner edit box at 32px against a 38px line,
  so digits sat visibly off-centre and descenders clipped. 1.2.7 claimed this fixed and it was not: the
  control's height was set to the line plus a **guessed** 3px allowance, but Mono's chrome (border plus
  internal padding) spends about 9px, so a 41px control had only a 32px client area — and the inner box
  can never exceed that, however hard the advisor stretches it. The chrome is now MEASURED at startup
  from a probe control, like every other metric in the interface, and the field is sized to the line plus
  that. It is logged in the `UI metrics:` line so the next report can be diagnosed from the log alone.

  Number fields are therefore ~6px taller than in 1.2.11. Rows that contain one derive their height from
  it and follow automatically; if a fixed-height card somewhere no longer fits its row, the audit will now
  say so instead of clipping in silence.

## [1.2.11] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0. Follow-up to 1.2.10 from
in-game testing of the Boosts page.

### Fixed

- **The page no longer judders.** The priority list and the "will boost now" readout were rebuilt on
  every settings write anywhere in the advisor, coalesced to once a second — and each rebuild repaints a
  whole owner-drawn list, while the readout re-resolves every item through the inventory. Both now
  compare against what is already displayed and touch nothing when it matches.
- **Del removes the selected item**, the keyboard equivalent of Remove.
- **Trashing an item in game takes it off the boost list.** The list is pruned of items you no longer own
  (equipped, inventory, daycare and MacGuffin slots all count as owned — a set in daycare is not gone),
  and every removal is logged. It never prunes while the inventory reads as empty, because at that moment
  everything would look unowned.

### Removed

- Drag & drop reordering. It was built as a layer over the buttons and does not work in the game's Mono
  runtime; the buttons and Alt+arrows are the reorder path.

## [1.2.10] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0. Boosting now follows one list
instead of three implicit sources, and the Boosts page gets a picker and proper reordering.

### Boosts

- The priority list is now the **only** thing that gets boosted. Equipped and locked-inventory items are
  no longer boosted implicitly, and the boost blacklist is gone from the page — it is emptied once on
  startup, since with one source there is nothing left for it to exclude. On first run your list is
  seeded once from what you currently have equipped and locked, so nothing changes silently — check the
  log line and prune the list to taste.
- New **Add from inventory** picker: search, multi-select, and see level, remaining boosts and where each
  item is. No more typing item IDs.
- Reordering: multi-select, Top/Up/Down/Bottom, Alt+↑/↓ and Alt+Home/End. **The selection stays put after
  a move**, so you can click Up repeatedly to walk an item several places — previously the row deselected
  itself and you had to re-pick it every time. The page also shows a live "will boost now" readout.
- Merging is now governed only by the transform-chain toggles.

## [1.2.8] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. The scrollbar is now sized like
the rest of the interface.

### Changed

- **The advisor draws its own scrollbar.** `AutoScroll` draws the operating system's scrollbar, and its
  width is a system metric with no property behind it — so on a display at 200% scaling it stayed a thin
  sliver beside 38px text, as hard to grab as it looked, and nothing short of replacing it could change
  that. Scrolling surfaces now own a scrollbar whose width is derived from the measured text like every
  other dimension in the interface, beside a content panel they offset themselves.

  This also removes the last of the scroll faults inherited from `AutoScroll`, since the offsetting is
  ours: no blit, so no streaks left behind by children that paint outside it.

  One structural consequence, documented in `docs/modules/ui-infra.md`: children of a scrolling surface
  belong to its `Content`, not to the surface itself, which holds the scrollbar. A single indirection in
  `SettingsForm` keeps every placement call site unaware of it.

- Scrolling surfaces watch their children for size and visibility changes. A card that grows when a plan
  wraps, or the settings list shrinking to its search results, changes how far the page can scroll, and
  nothing else was in a position to notice.

## [1.2.7] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. Scrolling is fixed.

### Fixed

- **Touchpad scrolling works.** WinForms turns a wheel *notch* into scrolling, but a precision touchpad
  does not send notches — it sends a stream of deltas far smaller than one, each of which rounded to zero
  or to a whole notch, so a page either refused to move or jumped. Scroll delta is now accumulated and
  spent in measured line units, so a slow drag scrolls slowly.
- **Scrolling no longer leaves streaks across the window.** Scrolling blits the client area and invalidates
  only the newly-exposed strip, so children that paint outside the blit — the owner-drawn lists especially
  — left their old pixels behind. The scrolling surfaces are double-buffered now.
- **The wheel no longer gets stuck on a list.** A list consumes the wheel even when it is already at its
  end, which stopped the page dead as soon as the pointer crossed the priority list. A list now scrolls
  while it still can and hands the page back the moment it cannot.
- The TRANSFORMS column no longer scrolls independently inside the Boost page. A scroller inside a
  scroller sends the wheel to whichever region the pointer is over, which is why scrolling stalled when
  the cursor crossed into it; it grows to its content and lets the page scroll, one owner per screen.
- Number fields are no longer short. Setting the control's height left its *inner* edit box sized from
  `Font.Height` at 32px against a 38px line — the last seven findings in the layout audit, now zero.

### Known limitation

- The scrollbar itself is still thin on a scaled display. `AutoScroll` draws the OS scrollbar and its width
  is a system metric with no property behind it; widening it means replacing `AutoScroll` with a custom
  scrollbar, which is a much larger change and worth far less now that the wheel behaves properly.

## [1.2.6] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. Shortened values now tell you what
they hid, and three layout faults from the high-DPI work are fixed.

### Fixed

- **Shortened text carries its full value as a tooltip.** Where a value genuinely cannot be given more room
  it still gets ellipsized — but the complete string existed nowhere the user could reach, so
  `UNLOCK: 100 SEE…` and `WAIT — 1e15 i…` were dead ends. Every fitted value now hangs its full text on the
  control, and only when it actually had to shorten it, so hovering never shows a tooltip repeating what is
  already on screen. This covers the wrapped two-line captions as well, which were the last place a value
  could be cut silently.
- The Adventure panel no longer cuts off everything below COMBAT STYLE. It has no scrollbar of its own by
  design — one there eats client width and re-wraps the content it was added to reveal — and its pages size
  themselves from their last row, so the host was placing it at a height that no longer described it.
- `Mode` and its dropdown stay together in the COMBAT STYLE row. The row wraps in narrow columns, and with
  all five controls in one wrap the break landed between the caption and the dropdown it labels.
- The gold pipeline arrows are back in place. 1.2.5 widened the arrow to the width its glyph measures
  without widening the gap reserved between the chips or the step past each arrow, so the arrows sat over
  the following chip. There is now one measured width used everywhere it is spent, and each arrow is centred
  on the chip it joins rather than at an offset that only looked centred at the old line height.

## [1.2.5] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. Dropdowns, lists and checkboxes
now scale with the display, and the Boost priority editor gets the room it always implied it had.

### Fixed

- Dropdowns, lists and number fields are no longer left at their 96-DPI size. A ComboBox, ListBox and
  NumericUpDown take their height from `Font.Height` — the same DPI-unaware value that already had to be
  worked around for measurement — so on a 200% display every dropdown was a 21px box of small text beside
  38px labels, and every number field was 32px against a 38px line. They were not merely ugly: they were
  hard to hit. There is no property for it, so these are now drawn by the advisor, the same lever the tab
  strip has always needed under this Mono. The look is unchanged; only the size is.
- **The Boost priority editor has usable space.** Its two lists were specified in pixels — 90px and 56px —
  against rows that pitch from the rendered line, so they showed three entries and one, with a scrollbar,
  while hundreds of pixels sat unused below them. Lists are now specified in ROWS (priority 8, blacklist 4)
  and everything beneath them positions off the list's real bottom edge, so the buttons and the apply-order
  row follow instead of being overlapped.
- Checkboxes scale with the text. The check glyph is a fixed ~13px system metric that ignores both the font
  and the DPI, so it cannot be enlarged by any setting — these are now drawn, and the whole control is the
  click target. They remain ordinary checkboxes: toggling, keyboard, tab order and every binding are
  untouched.
- The TRANSFORMS notes no longer lose their ends ("…spare copies keep mergin"). They were auto-sizing
  labels running past the panel edge, which clips silently; they now wrap, per the no-ellipsis rule.
- The gold drain ledger no longer clips its rates ("1,000E+012/s · 5%" needed 205px in a 186px column). The
  value column is measured and the bar absorbs the difference — a shorter bar reads fine, a cut number does
  not. The pipeline arrows are measured too, instead of trusting a 16px slot.
- Rows that contain a number field are derived from it rather than scaled beside it, so the field no longer
  overhangs its row by a pixel or two at every scale.
- The log slivers read as many lines as they can actually show, now that list rows are pitched at the
  measured line height rather than an assumed one.

## [1.2.4] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. This finishes the high-DPI work
started in 1.2.3: the window scaled correctly there, but text still clipped inside individual boxes.

### Fixed

- Headings, card titles and button captions no longer clip on high-DPI displays. The layout sized many
  single-line labels at 18-22px for text that renders 25px, so descenders were already being shaved by a
  pixel or two — invisible at that size. Scaling those heights preserved the shortfall and multiplied it, so
  at 200% an 18px box became 27px for 38px text and lost 8px of every "g" and "y". Buttons showed the same
  fault differently: the caption looked vertically off-centre rather than short. Any control that holds text
  now takes its height through a floor that scales *and* guarantees what the renderer needs.
- The growth tiles no longer cut their third line, and the fruit, light, chip and priority cards no longer
  cut their contents. A fixed-height card is now derived from the lines it holds instead of being scaled
  alongside them — the two drifted apart as soon as the line heights were floored.
- Yggdrasil fruit labels no longer lose the end of their text ("UNLOCK: 100K SEEDS" arrived as
  "UNLOCK: 100K SEE"). The text was measured in the 7.5pt chip font but painted in the 9pt one, so the fit
  believed more fitted than did — and an overflowing fixed label paints cut, with no ellipsis to hint at it.
  This was wrong at every scale; high DPI only made it visible.
- The refresh (`↻`) and dismiss (`✕`) buttons size to their glyph instead of a fixed width they had already
  outgrown.

### Changed

- The layout auditor no longer skips hidden controls when checking whether text fits. A box too short for
  its own font is too short whether or not it is on screen, and panels that build their content hidden and
  reveal it on refresh — the Yggdrasil orchard builds every tile hidden — were consequently the only panels
  never checked. That is exactly where the clipping was reported while the audit reported "clean".
  Geometry checks still apply only to visible controls, since alternate views deliberately share coordinates.
- The auditor also reports content past a card's bottom edge (for containers that have no scrollbar to reach
  it with) and any section wider than its viewport. A horizontal scrollbar's only visible symptom is the
  scrollbar itself — whatever causes it is off-screen — so it is now named in the log.
- The measured DPI metrics are logged culture-invariantly, so the diagnostic line reads `scale 1.52` rather
  than `scale 1,52` on comma-decimal locales.

## [1.2.3] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. This release fixes the settings
window on high-DPI displays. Nothing about how the advisor plays the game changed.

### Fixed

- The settings window is now readable on displays with Windows scaling above 150%. Every position and size
  in the window was hand-placed in pixels against a display where 9pt text renders about 25px tall, but the
  game's Mono renderer sizes text from the real screen DPI — so at 200% scaling the text grew by half again
  while the boxes holding it did not. Headings were cut off, stacked lines overlapped each other, and the
  status strip along the bottom was clipped out of the window.

  The layout metrics are now measured at startup from the text the renderer actually produces, and every
  hand-placed dimension is derived from that measurement. On a display at the original scaling the measured
  values reproduce the previous numbers exactly, so nothing moves there. The measurement is written to
  `debug.log` as the first `UI metrics:` line, and it never scales below the previous values — an
  under-reporting renderer keeps the old layout rather than shrinking it.

## [1.2.2] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. This release is a performance
pass on the advisor's own overhead, plus one timing fix. Nothing about how the advisor plays the game
changed: the gear optimizer picks the same loadouts, just far faster.

### Fixed

- Titans are no longer wrongly given up on when the system clock shifts. The "titan still available after
  10 minutes" check measured elapsed time against local time, so a daylight-saving change (or any clock
  correction) jumped it by an hour and the advisor immediately lowered the titan's version or dropped it
  from your swap and gold targets — and saved that.

### Changed

- The advisor no longer repaints its window while that window is closed. Closing the window hides it, and
  the whole live-status pump — status strip, rail, sliver logs, growth graph, progress bar — kept running
  and force-repainting four times a second for the entire session. The growth graph still samples with the
  window closed, so its history is unaffected.
- Settings are written to disk at most once a second instead of once per changed setting. The advisor
  rewrites settings continuously (titan targets, snipe state, banked versions), and each one previously
  serialized and wrote the whole file. Pending changes are always flushed on Unload and on game exit.
- The gear optimizer is substantially faster. Scoring no longer allocates per candidate evaluation, and the
  "keep one respawn item" pass no longer runs a complete optimization for every respawn item you own — only
  for the ones that can actually win. Both rewrites were verified to select the same result as before.
- Live log views (Combat, Economy, and the Logs SESSION tab) read only the end of their log file instead of
  the whole file on every refresh. This mattered most for the pit/spin and cards logs, which are appended
  to across sessions and so grew without limit.
- Smaller overhead reductions on the per-frame path: the in-game overlay rebuilds its text ten times a
  second rather than several times per frame, loot-log scanning no longer re-processes lines it has already
  reported, and repeated inventory and titan scans that recomputed the same answer were consolidated.

### Build

- The build finds NGU Idle's `Managed` folder automatically and can be pointed anywhere with
  `-p:NGUManagedDir=...` or a `Directory.Build.props`, instead of requiring seven hardcoded paths to be
  edited. A missing install now fails with that as the error.
- `SettingsForm.resources` is regenerated from the `.resx` automatically when it is out of date; this was a
  manual step that silently embedded stale form resources when forgotten.

## [1.2.0] - 2026-07-22

Existing settings and profile files remain compatible with version 1.1.0. This release is a large
correctness and robustness pass from a full external review, plus the first automated test coverage.

### Fixed

- Profiles no longer risk silent number corruption on comma-decimal system locales, and large integers
  round-trip exactly (culture-invariant JSON number handling).
- The EXP planner no longer overflows at high (Evil-scale) values.
- Boost-farm zone values were corrected — Evil zones were dramatically undervalued against ITOPOD, which
  suppressed zone recommendations; the advisor now compares them on the true boost-value scale.
- The final Sadistic titan zone (THE TRAITOR) is now reachable, and Sadistic zone-unlock thresholds are
  corrected (a missing zone had shifted several later zones).
- Iron-pill blood advice now matches what the caster will actually do (no more "cast now" for a pooled cast).
- Money-pit lock, inventory transform-chain protection, settings-form resilience, and numerous smaller
  correctness fixes across combat, gear, diggers, quests, wishes, and consumables.

### Added

- An automated test project (53 tests) guarding JSON round-trip, number formatting, and the titan tables.
- A single consistent large-number formatter across all panels.

### Changed

- The two progression-chapter engines are now documented as distinct, non-interchangeable concepts.

## [1.1.0] - 2026-07-15

Existing settings and profile files remain compatible with version 1.1.0.

### Added

- Two-level navigation with Overview and Priorities.
- Dedicated Profile page for allocation source, profile selection, editing, and file access.
- Searchable Settings interface.
- Persistent eight-cell status strip.
- Redesigned Loadouts interface covering Titan, Gold, Quest, Yggdrasil, Cooking, Loot Hunter, and Shockwave.
- Configured, WILL EQUIP, and CURRENTLY EQUIPPED snapshot displays.
- Contextual activity feedback for supported user actions.

### Changed

- Advisor home workflow is split between Overview and Priorities.
- Automatic Money Pit actions use a single configured owner, preventing competing automatic throw paths.
- Public release builds no longer embed local build-machine paths.
- Current-equipment snapshots update explicitly through REFRESH STATE rather than implying a live feed.
- The advisor now holds automatic Iron Pill casts until the pill has been available for at least 30 minutes and would add at least 10% of current base adventure power.

### Fixed

- A failure in one advisor operation no longer prevents later operations from running.
- Temporary Money Pit, equipment-lock, Yggdrasil, and MacGuffin state is restored after failures.
- Repeated faults are reported without flooding the log.
- Settings filtering and layout restoration no longer produce false overlap reports.
- Profile Editor paste operations validate before replacing the current loadout and retain the accepted undo behavior.
- Profile, Loadouts, status, and other updated views received layout and audit corrections.

### Removed

- Obsolete mode-loadout UI infrastructure.
- Superseded legacy Profile selector controls.
- Repetitive Yggdrasil fruit-state debug output.
