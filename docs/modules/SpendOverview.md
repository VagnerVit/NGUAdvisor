# SpendOverview + SpendPanel (`Managers/SpendOverview.cs`, `SpendPanel.cs`)

"What is each currency saving for, and who decided that?" — five rows (AP, PP, QP, seeds, EXP) on
Economy > PLANNERS, above the per-currency detail panels.

## It decides NOTHING — that is the whole design

No ordering, no thresholds, no plan of its own. Every field is a straight read of the module that
owns the decision:

| Currency | Owner | Source call |
|---|---|---|
| AP | `ApPurchaseAdvisor` | `Next()` → tier-list row, live ownership + price |
| PP | `SpendPlanner` | `NextPerk()`, else `NextPerkPlanned()` for what the bank is FOR |
| QP | `SpendPlanner` | `NextQuirk()` / `NextQuirkPlanned()` |
| Seeds | `SpendPlanner` | `NextFruit()` |
| EXP | `ExpBalancer` | `Analyze()` — a ratio WALK, so no single price to quote |

**If a row looks wrong, the bug is in the owner and that is where the fix belongs.** Adding a
correction here would create a second opinion about an irreversible spend, which is exactly the
failure this module exists to prevent.

## Why it exists

The advisor already knew all five answers, but each lived behind a different panel — so a currency
whose panel was closed had no visible plan, and advice got sourced from the guide instead of from
the owning module. On 2026-08-12 a digger slot was recommended for 100k AP while
`ApPurchaseAdvisor`'s own queue said an AP heart was next. **The owner column on every row is what
makes that class of mistake visible instead of plausible.** Do not drop it to save width.

`LogChanges` writes one `[SpendDbg] <currency> (owner <module>): <answer>` line per currency, only
when that currency's answer CHANGES — the panel refreshes about once a second and five lines per
refresh would bury debug.log.

## `Buys` — a rate is only a decision once it is a purchase

`Buys(row, perHour)` turns a farm rate into the thing it brings forward: `1.2k PP/hr` becomes
`Faster NGU Energy in ~3h`. This is the answer to the cross-currency question that has **no honest
scalar** — the exchange rate between PP, boosts and EXP is phase-dependent, so any constant would be
wrong for half a run (ItopodFarmAdvisor.md's "Open" section). Callers state the purchase instead.

Consumers: `AdvisorApply.ApplyZones` (the pod-parking log line, where PP/s already decides the combat
mode) and `AdventurePanel`'s floor info. Both call `RowFor(CurrencyPp)` rather than `Rows()` — each
owner is a live game read and a single-answer caller must not pay for the other four.

It returns **null** when there is nothing honest to say (unknown row, unread price, no rate), and the
callers append nothing in that case. `PpEta.HoursTo` supplies the same rule for the ETA itself.

## Rendering rules inherited from the owners

- **A missing price is absence of data, not a free purchase.** `CostKnown == false` prints
  `cost unknown` and the affordability verdict is printed only inside the `CostKnown` branch —
  "keep saving" beside an unread price would dress a data gap as a verdict (same rule as
  `ApPanel`, see ApPurchaseAdvisor.md).
- **"Nothing buyable" is not "plan complete."** For PP and QP the note falls through to
  `NextPerkPlanned`/`NextQuirkPlanned` and reads `banking for X (chapter N)`; only when the planned
  buy is also unknown does it say `plan complete` (SpendPlanner.md).
- EXP has no discrete purchase, so `Next` names the stats the next walk chunk feeds and `CostKnown`
  stays false by construction.

## The panel

Read-only, no controls at all. Heights are derived (`ContentHeight` from the provenance label's
bottom, row pitch chained off each row's own `Bottom`) per the DPI contract in ui-infra.md. Live
reads happen only in `SyncFromSettings()`, called from `SettingsForm.UpdateFromSettings` — the
deferred ≤1/s Unity-main-thread pass — and from `VisibleChanged`; a hidden panel does no work.

Registered as a `SettingsIndex` **Reference** (`NextBuy`), not a system: it owns no setting, no
automation, and no ordering. Route `Destinations.NextBuy = "Economy/Planners"`, same page as the
AP/PP/AT references, one row above them.
