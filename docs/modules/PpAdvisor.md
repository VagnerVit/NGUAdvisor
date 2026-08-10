# PpEta + PpPanel (`Managers/PpEta.cs`, `PpPanel.cs`)

"What is my banked PP for, and when will it be enough?" — the Economy > PERK POINTS surface over the
perk plan `SpendPlanner` already computes, plus the one number the codebase did not have: an ETA at
the pace the player is actually going. `PpEta` is the Unity-free arithmetic (linked into
`tests/NGUAdvisor.Tests`); `PpPanel` is the view. Same pure/live split as `ApTierTable` + `ApPanel`.

> **Nothing here has been run against the game.** Every runtime check — the live balance, real
> measured rates, the `UI AUDIT` pass, the toggle's effect on routing — was deferred by the owner.
> What is proven is that the project compiles against the game's `Assembly-CSharp.dll` (so every
> member named below exists with the stated type), that 5 unit tests pin `PpEta`, and that the
> Release build is clean at 0 warnings. Behaviour at runtime is unverified. Treat a first live run as
> the missing verification step, not as a regression hunt.

## This module computes no plan — and that is why there is no `PpAdvisor`

Every number on the panel is owned by somebody else:

| Fact | Owner |
|---|---|
| Banked PP | `Main.Character.adventure.itopod.perkPoints` (as `SpendPlanner` reads it) |
| Which perk is next, its levels and cost | `SpendPlanner.NextPerk()` → `Buy` (`SpendPlanner.cs:203`) |
| The next perk that is queued but **gated** | `SpendPlanner.NextPerkPlanned()` → `PlannedBuy` (`SpendPlanner.cs:239`) |
| **Measured** PP rate | `GrowthTracker.Rate(s => s.GPp, 60, false, out r)` |
| **Modelled** PP rate | `ItopodFarmAdvisor.ForMode(mode).PpPerSecond` |
| The ETA arithmetic | `PpEta.HoursTo` — the only thing this module adds |

`SpendPlanner` is **not modified** by this module and must not be. There is no decision taken here,
only a readout, and the repo's rule is that panels may read managers directly. A `PpAdvisor` manager
that only forwarded `SpendPlanner`'s answer would be a layer with no job — one more place for the
perk order to drift out of sync with the planner that owns it. The pure arithmetic still gets its own
file, because a second copy of the estimate inside the panel would be free to disagree with this one,
and because a file with no Unity types can be unit-tested without an NGU install.

## `PpEta` returns `null` for every "don't know" case, on purpose

`PpEta.HoursTo(long cost, long banked, double perHour)` (`PpEta.cs:15`) returns `null` when:

- `banked >= cost` — already affordable, there is nothing to wait for;
- `perHour` is `NaN` or infinite;
- `perHour <= 0` — no rate, so no estimate.

Otherwise `(cost - banked) / perHour`.

**The null is the contract, not a placeholder.** A rendered `0h` or an `∞` in that slot reads to the
user as a real prediction, and this module's entire value is that its numbers can be trusted; an ETA
that is sometimes fiction is worse than no ETA. `RenderCard` honours it literally — a null prints
`short <N> · no rate yet` with **no duration at all** (`PpPanel.cs:282-284`). Do not "improve" this
by substituting a large number, a dash rendered as a time, or a clamped zero.

The subtraction is `long` before it is promoted to `double`, so a very large shortfall stays finite
and positive (`AVeryLargeShortfallStaysFiniteAndPositive`).

## The two rates, and the rule that they never blend

They answer **different questions** and a reader who mistakes one for the other is being lied to:

- **Measured** — `GrowthTracker.Rate(s => s.GPp, …)`: what the player is actually earning right now,
  whatever they are doing. This is the headline, because the ask was "at the current pace".
- **Modelled** — `ItopodFarmAdvisor.ForMode(…).PpPerSecond * 3600`: what the pod *would* pay at the
  advisor's floor for that combat mode. It renders on its **own separate line**
  (`ITOPOD would pay … (mode, floors N-M)`) as a "would switching help?" figure, and when
  `rates.Known` is false the line says the rate is unavailable rather than printing the zero.

They are never averaged, summed or otherwise combined. The ETA uses exactly **one** of them and
**always names which**, in parentheses at the end of the line (`PpPanel.cs:283`).

### The fallback, and why it must be labelled

`GrowthTracker` samples only since load, so shortly after a reload or a rebirth there is no measured
rate. The panel then falls back to the modelled figure — that is allowed. Doing it **silently** is
not: a modelled ETA presented as measured is a wrong answer wearing the right label.

Two no-measurement cases exist and they are told apart deliberately (`PpPanel.cs:226-229`):

| Condition | Label |
|---|---|
| `Rate` returned `false` (no samples yet) | `modelled — no measured rate yet` |
| `Rate` returned `true` with exactly `0` | `modelled — you are not gaining PP right now` |

**A measured rate of exactly 0 is treated as "no measured rate", but with a distinct label.** Zero is
what `Rate` reports when the player is not in the pod at all; feeding it to `PpEta` correctly yields
null, but that would have printed a bare "no rate yet" while a perfectly usable modelled figure sat
one line below. Both labels still say "modelled", so the blending rule is untouched — only the
diagnosis is sharper. Do not collapse the two strings back into one: "wait a minute" and "you are not
farming PP at all" are different instructions to the user.

The window is the constant `RateWindowMinutes = 60` (`PpPanel.cs:29`), reused from `GrowthPanel`'s
default "1H" chip (`GrowthPanel.cs:30-31`) rather than re-picked, so this ETA cannot disagree with the
PP/hr chip on the Status page.

### The rate reads `GPp`, never the `Pp` balance

`GrowthTracker.Sample` carries both a raw balance (`Pp`) and cumulative gains since load (`GPp`).
This module reads **`GPp`**. Spending PP must never count the rate down — buying a perk is progress,
and a rate that dips every time the user follows the panel's own advice is actively misleading. That
is a standing user rule recorded at `GrowthTracker.cs:8`. Do not "simplify" the selector to `s => s.Pp`.

## `NextPerk().Known == false` is NOT "plan complete"

`SpendPlanner.NextPerk()` goes unknown whenever the next guide step is gated by chapter or
difficulty, which on Normal is most of the run. **Only when `NextPerkPlanned().Known` is also false is
the plan actually finished** (`PpPanel.cs:249-260`):

| `NextPerk().Known` | `NextPerkPlanned().Known` | Card says |
|---|---|---|
| true | — | the perk, its levels, cost, affordability / ETA |
| false | true | `NOTHING BUYABLE RIGHT NOW` + "The next guide step is gated" |
| false | false | `PERK PLAN COMPLETE` |

Collapsing those two into one condition was a **user-reported bug** — a chapter-gated plan surfaced
as "complete" while later steps were still queued (`SpendPlanner.cs:236-238`, and see
`docs/modules/SpendPlanner.md`). The queued line beneath the card exists for the same reason: the
gated step is *what the bank is for*, so it is named with its gate
(`needs chapter N` [`and the next difficulty`]) and its cost.

**The planner is asked once per refresh.** `NextPerkPlanned()` is called a single time and the one
result is passed to both `RenderCard` and `RenderQueued` (`PpPanel.cs:234-236`), so the card and the
queued line cannot take two snapshots that disagree about whether the plan is finished.

## The toggle, and the four overrides it must disclose

The panel carries exactly one control: **"Farm ITOPOD for PP"**. The design spec
(`docs/superpowers/specs/2026-08-10-pp-advisor-design.md`) describes this panel as read-only with no
control; that is **stale** — the toggle was added at the owner's request. Everything else in the spec
still holds.

It is a **routing preference, not a purchase**: it can be turned straight back off and it never spends
a perk point. `ToggleClicked` writes `Settings` and **nothing else** — it sets
`Settings.AdventureTargetITOPOD`, and **only on the way ON** also sets
`Settings.ITOPODOptimizeMode = 2` ("PP"). Turning it off restores `AdventureTargetITOPOD = false` and
**leaves the optimize mode alone**: `AdventurePanel` owns that choice, and turning the toggle off is
meant to restore routing, not to rewrite the pod's optimisation target behind the user's back.
`ITOPODCombatMode` is never touched. `OptimizeModePp = 2` is a named local constant because
`AdventurePanel.cs:359` builds the list `{ Disabled, Default, PP, EXP/AP }` and stores the raw
`SelectedIndex` — there is no shared enum in the codebase to reference.

**Main-thread rule**: the handler must not call allocation or routing code. Doing so would run Unity
calls off the Unity main thread and hard-crash the game. It sets the flag; the next `Main.Update()`
pass reads it.

`RenderToggle()` reads the **live** setting on every refresh rather than a cached boolean, so flipping
"Target ITOPOD" on the Adventure page moves this button too — one property, one owner.

### The four preconditions, all read off the routing code itself

A control that silently fails to do what its caption says is worse than no control. `RenderToggle()`
therefore renders up to four notes into a **four-line reserved box** (all four can hold at once), and
turns the box `UiTheme.Danger` whenever any of the three blocking conditions applies.

1. **Combat off** — `Main.cs:1386` returns before any routing runs. The toggle changes nothing until
   combat is enabled. Shown regardless of the toggle's own state.
2. **Gear hunt outranks ITOPOD targeting** — `Main.cs:1391`: `GearHunter.Active &&
   GearHunter.ZoneReachable()` wins over `AdventureTargetITOPOD`, which wins over `SnipeZone`.
   **This precedence is itself the fix for a user-reported bug**: Target ITOPOD silently overrode the
   hunted stage, when the hunt toggle *is* the routing intent while it is on. The panel reads the same
   predicate the router reads (in its own `try/catch`) and *surfaces* the precedence — it never works
   around it. Do not "simplify" the ordering at `Main.cs:1391`.
3. **EVIL CLIMB ignores the toggle outright** — `Main.cs:1404`. **Also a fix for a user-caught bug**:
   honoring Target ITOPOD during a climb parked the run in the pod after one kill and collapsed gross
   gold, i.e. the digger budget (the reasoning is written out at `Main.cs:1397-1403`). A climb needs
   bosses and gold, which the pod pays neither of. The warning is **conditional and cannot fire on a
   manual profile** — `ChallengeOverlay` blanks `Segment` whenever AutoProfile is off
   (`ChallengeOverlay.cs:142-143`) — and the wording says so in a parenthetical, so Evil players on a
   manual profile are not warned that their toggle is dead when it is not. It is also bounded in time
   ("until the segment ends"), so it reads as a pause rather than as breakage.
4. **While it is ON it bypasses the advisor's zone routing** — `Main.cs:1393` prefers the ITOPOD flag
   while `ApplyZones` keeps writing `SnipeZone`, so **gear and boost farming stop**. This is the thing
   a user would otherwise spend days not understanding, and it is stated in **both** states: telling
   someone only after they have flipped it is disclosure after the fact.

Nothing on this panel buys a perk. Auto-buy already exists in `AdvisorApply` and is configured there;
a second spend path on a read-only advice surface would be two owners for one irreversible action.

## Registered as a `SettingsIndex` Reference with EMPTY fields

`SettingsIndex.cs:428` adds a `Ref`, not a `Sys`, for the same reason `ApPurchases` is one: the panel
owns no setting of its own, has no automation and no advisor/manual choice, so a System entry would
promise state and a gate that do not exist.

**The fields column is deliberately empty.** The toggle writes `AdventureTargetITOPOD` and
`ITOPODOptimizeMode`; `AdventurePanel`'s catalogue entry (`SettingsIndex.cs:248`) already claims
`AdventureTargetITOPOD` as its own. Naming it again here would put two catalogue rows behind one
switch and trip the duplicate-surface audit, and the panel that owns the setting is the one the
catalogue should route to. `Destinations.PerkPoints = "Economy"` — its own name even though it shares
AP's and Gold's route, per that file's standing rule that sharing a route is not being the same
destination.

## Layout: derived heights, and where the panel sits

Read-only shape copied from `ApPanel` line for line. Every hand-placed pixel goes through
`UiTheme.S(n)`; the toggle's width is `UiLayout.BtnWidth(caption)`, never a number.

- **Height is derived, never tuned.** The card's `Height` comes from `_cardNote.Bottom` *after* its
  children exist, and `ContentHeight` from `provenance.Bottom` — every Y chains off the previous
  control's `Bottom`. This is why growing the note box from three reserved lines to four
  (`SHead(54)` → `SHead(72)`, `WrapInto(…, 3)` → `WrapInto(…, 4)`) needed **no** manual height edit.
  A card has no scrollbar, so a fixed card height would clip its note silently at real DPI.
- **`ContentHeight` is reported once, in the constructor.** The consequence is a hard rule: constant
  prose (`provenance`) goes through `UiLayout.FitOrGrow` **once in the ctor**, and every *refreshed*
  string uses a fixed-height fit — `FitInto` for one-line values, `WrapInto` for the reserved
  multi-line boxes. `FitOrGrow` on a refreshed string would resize a label after `ContentHeight` had
  already been handed to `SettingsForm`, so the panel's frame would stop matching its contents.
  Nothing in `SyncFromSettings` may call `FitOrGrow`.
- `_queued` clears its tooltip (`UiLayout.Tip(_queued, null)`) when it empties, so a stale full-text
  tooltip cannot outlive the line it belonged to.
- **Main-thread rule**: the only live reads are in `SyncFromSettings()`, called from
  `SettingsForm.UpdateFromSettings` (`SettingsForm.cs:1597` — the deferred ≤1/s Unity-main-thread
  pass) and from `VisibleChanged`. The whole body is one `try/catch` that logs, because a throwing
  read in that pass aborts every panel after it.
- `Duration()` is a private local formatter. The only other duration formatter in the codebase is
  private to `ProfileValidator`, and one caller does not justify a public utility.

## Tests, and the invalid audit baseline

`tests/NGUAdvisor.Tests/PpEtaTests.cs`, 5 facts: already affordable → null (both `==` and `>`);
zero and negative rate → null; `NaN` and `+∞` → null; the normal division case hand-checked
(1.27M / 380K = 3.342h); a `long.MaxValue / 2` shortfall stays finite and positive.
`PpPanel` is Unity-dependent and cannot be unit-tested — build only.

**The `UI AUDIT` oracle has never been run against this panel.** Both `PpPanel` and `ApPanel` were
missing from `SettingsForm`'s `UiLayout.Audit` list and were added in the fix round
(`SettingsForm.cs:416-417`).

> **MEASURED 2026-08-10 against build `260810-1616`: `UI AUDIT` reports ZERO issues across all 26
> audited panels**, this module's included. The docs' standing "the audit must be zero" rule holds —
> earlier notes in this repo claiming a non-zero baseline of 70 were wrong, and are corrected here.
>
> Those 70 came from a run that calibrated at `scale 1.00` (`UI metrics: … line 25, head 22`), where
> every single finding was the same class: `CONTROL TOO SHORT FOR TEXT 'ComboBox:<text>' h=24 < 25`.
> On the real display calibration (`scale 1.52`, `line 38, head 33`) they all fit and the audit is
> clean. **So read the `UI metrics` line BEFORE believing a dirty audit** — a wall of
> `CONTROL TOO SHORT` lines is far more likely to mean the advisor calibrated on an unscaled context
> than that the panels are broken.
