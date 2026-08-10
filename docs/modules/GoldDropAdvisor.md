# GoldDropAdvisor (`Managers/GoldDropAdvisor.cs` + `Managers/GoldDropTables.cs`)

Answers one question: **is a gold kill worth its gear swap?** Everything that swaps to the gold set
(zone snipe, titan gold bank) asks here first.

## Game truth

- **`LootDrop.goldDrop(baseGold)`** → `baseGold * Random.Range(4f, 5f) * character.totalGoldbonus()`,
  then `addGold` + `timeMachineController.setbaseGold(drop)`.
- **`TimeMachineController.setbaseGold(x)`** assigns `machine.realBaseGold = x` only when
  `bossID > 29` **and** `x > realBaseGold`. So `realBaseGold` is the **highest single drop of the
  current rebirth**, and TM output rides on that one number — a smaller drop changes nothing.
- Consequence, and the reason this module exists (user-reported): once a titan banks a drop, no
  reachable zone boss can match it, yet the snipe kept re-arming and flipping to gold gear forever,
  paying Power/Toughness for a drop the machine discards.
- `baseGold` is a per-zone, per-enemy-type constant. `GoldDropTables` holds them, extracted 1:1 from
  `LootDrop.zone{N}Drop` (0–45). Titan zones carry ONE value shared by every version of the titan;
  **zones 44/45 (BEAST, THE TRAITOR) call no `goldDrop` at all — they drop no gold.** ITOPOD has no
  gold path either, which is why zone ≥ 1000 is absent from the table.

`GoldDropTables` is deliberately game-independent (no Unity/`Main`) so the magnitudes are unit-tested
— `tests/NGUAdvisor.Tests/GoldDropTablesTests.cs` guards coverage, boss ≥ normal, the two no-gold
titan zones and the titan-beats-zone ordering.

## Prediction

`PredictedDrop(zone, bossOnly) = BaseGold * 5.0 * totalGoldbonus() * GoldGearFactor()`

- **`5.0` — the BEST end of `Random.Range(4f, 5f)`, and this is load-bearing.** The first version used
  4.0 ("be conservative") and that was a methodological error: the number it is compared against,
  `realBaseGold`, is a REALIZED drop that already contains its own 4–5 roll. Predicting at 4.0 against
  a bank realized at ~4.5 makes an identical kill look 11 % worse than itself, so nothing could ever
  beat its own previous drop. Observed live: T3 predicted 14.5M against the 22.5M bank T3 had produced.
  The costs are asymmetric as well — over-predicting wastes one gear swap, under-predicting forfeits the
  run's gold production — so optimism is the correct side to err on.
- **`bossOnly` = true for every snipe caller**: `CombatManager.CheckEnemy` runs boss-only while
  `IsCurrentlyGoldSniping`, so the boss value is the one a snipe realizes.
- **`GoldGearFactor()`** — what the swap itself will do to the drop, and the only reason a snipe can
  be worth it while the CURRENT gear says otherwise. `totalGoldbonus()` multiplies exactly one
  gear-dependent factor, `(1 + bonuses[GoldDropAmount] + bonuses[GoldDrop2] + cubeGoldBonus())`, and
  a single-stat "Gold Drops" score IS that factor (`rawTotal/100`, cube folded in) — so
  `Optimize("Gold Drops").Score / CurrentScore("Gold Drops")` is the multiplier. Cached 120 s
  (`Optimize` walks the inventory); it falls back to 1.0 on any failure, which UNDER-states the drop
  and can only leave a snipe armed.

## Decisions

| Caller | Rule |
|---|---|
| `Main.GoldSnipePays` (`SnipeZone`) | predicted > banked, else latch `GoldSnipeComplete` — logged once per (zone, bank) pair, this is per-frame code |
| `Main.SetResnipe` ("new zone fightable") | a higher zone only re-arms the snipe if its boss beats the bank; the zone is still recorded so it doesn't re-test every second |
| `AdvisorApply.ApplyGold` (starvation) | starving for augments is no reason to re-snipe when the bank is already out of reach — that gold must come from a titan |
| `AdvisorApply.ApplyGold` ("gold drop improved") | re-arms a latched snipe once the grown gold bonus clears `RebankMargin` × bank, with no new zone needed |
| `AdvisorApply.ApplyTitanGold` | **no payoff gate at all** — `TitanKillWorthGoldGear` says yes for any AK titan that drops gold and is not deny-listed; which of those to target is ranked by gold (below) |

`RebankMargin = 1.25` applies to the ZONE snipe only: re-arming it means fighting a zone in loot gear
for a while, so a sliver of predicted gain is not worth it.

**Why titan gold has no gate** (user-reported bug, `[TitanGoldDbg]` caught it): the auto-kill happens
whether we swap or not, so the gold set cannot cost a fight — only `LockManager`'s post-swap autokill
re-test can veto it. With nothing to weigh against, comparing the predicted drop to the bank could only
produce false negatives, and it did: a titan that had already banked once stayed on loot gear for the
rest of the run. The `TitanMoneyDone` latch therefore no longer gates targeting either — it records
that a bank was collected (for `ZoneHelpers`' kill detection) and `ApplyTitanGold` re-arms it for the
next spawn. Every AK cycle is another shot at a bigger drop as the gold bonus grows.

## Ranking, not height (`AdvisorApply.BestGoldTitan`)

**Titan gold is not monotone in titan index**, so "the highest AK titan" is the wrong target — it was
the original `ApplyTitanGold` rule and it silently banked the smaller drop (user-reported "snipes when
it shouldn't"). From `GoldDropTables`:

| Titan | Zone | baseGold |
|---|---|---|
| T2 | 8 | 400 000 |
| T3 | 11 | **300 000** — less than T2 |
| T4 | 14 | 500 000 |
| BEAST / THE TRAITOR | 44 / 45 | **0 — no `goldDrop` call at all** |

With T3 as the highest AK titan the advisor banked 300k where 400k was on offer; a zero-gold titan at
the top would have banked nothing. `BestGoldTitan` ranks the eligible candidates by
`PredictedDrop(zone, bossOnly: true)` instead and returns the winner plus the runner-up (for the log).

- **Eligibility is unchanged** and still belongs to `TitanKillWorthGoldGear`: auto-killable (the same
  `ZoneHelpers.AutokillAvailable` sweep `HighestAkTitan` uses, one 30 s cache for both), not inside its
  `DenyGoldSwap` cooldown, and `baseGold > 0`. Zero-gold titans therefore can never win a gold ranking,
  which is the point.
- **Ranking picks WHICH titan, never WHETHER** — there is still no payoff gate against the bank, for the
  reasons in the next section.
- The live factors (`totalGoldbonus`, `GoldGearFactor`) are common to every candidate and cannot reorder
  them, which is why the ordering is unit-testable straight off the table
  (`GoldDropTablesTests.Titan2_out_drops_titan3`).
- Nothing eligible → fall back to `HighestAkTitan()` with `worth = false`, so the pass that CLEARS a
  denied titan out of `TitanGoldTargets` still runs. `GoldTitanTarget()` hands the panels the cached
  pick: they run on the WinForms thread and `PredictedDrop` reaches the gear optimizer.

## Gold gear vs. the autokill (safety)

The AK thresholds are **live-stat** checks and a gold set spends the very stats they measure. After
`LockManager.TryTitanSwap` equips the gold set it re-tests via
`ZoneHelpers.GoldTargetLosingAutokill()`; if a target lost its autokill the titan set goes straight
back on (no waiting out the 10-minute snapshot watchdog) and the titan enters `DenyGoldSwap` — a
30-minute runtime-only cooldown, no settings written.

The deny also feeds `ZoneHelpers.GetTitanSnapshot`, so `ShouldUseGoldLoadout` goes false for that
titan. That matters: the kill-detection branch in `RefreshTitanSnapshots` sets `TitanMoneyDone` for
any spawning gold target that dies, and without the deny a kill made in the TITAN set would be
recorded as a bank that never happened — then re-banked, re-denied, forever.

## Diagnostics

`[TitanGoldDbg]` in `debug.log` (`AdvisorApply.LogTitanGoldState`, 60 s cadence, emitted only when the
line CHANGES) carries every input of the decision: AK titan + version, the `done` latch, predicted drop,
bank, verdict, gear factor, spawning/targeted counts, gold-swap flag, lock, and the `SnipeZone` gates
(`snipeComplete`, `adv`, `global`, `tmOn`). It exists because all of those are transient — after a titan
has been auto-killed in the wrong gear there is otherwise nothing left to inspect. It is what identified
the 4.0-roll error above.

`[GoldSnipeDbg]` in `debug.log` is the ZONE side, added because the only evidence of a snipe decision
used to be `Main.Log`'s single user-facing skip line (advisor output log, once per `(zone, bank)` pair) —
so "it snipes constantly when it shouldn't" was unfalsifiable. Every line carries the bank
(`machine.realBaseGold`), the candidate's predicted drop, **`vsBank=` — the percentage the drop would move
the TM's basis** (`PctVsBank`; `new-bank` when the bank is 0), a verdict (`SNIPE` / `SKIP` / `RE-ARM` /
`IDLE`) and the reason:

```
[GoldSnipeDbg] SKIP zone=21 (Jake From Accounting) bank=22.50M pred=14.50M vsBank=-35.6% why=below bank (latching complete)
```

Decision points: `GoldSnipePays`, the latched-complete steady state and "no fightable zone" in
`Main.SnipeZone`; the new-zone test in `SetResnipe`; the `RebankMargin` re-arm and the starvation trigger
in `AdvisorApply.ApplyGold`.

**Cadence discipline is mandatory here**: `GoldSnipePays` and the latch state run from `SnipeZone` EVERY
FRAME, so those go through `LogGoldDecisionThrottled` — a 60 s cap plus emit-only-when-the-line-CHANGES,
exactly `[TitanGoldDbg]`'s rule. An unconditional log there buries the decision it was added to show. The
rare, event-driven call sites (re-arm triggers, new-zone test) call `LogGoldDecision` directly. The
`IDLE` lines deliberately do NOT predict: `PredictedDrop` reaches the gear optimizer.

`[TitanGoldDbg]` additionally carries `pick=` (the ranked winner), `beat=` (the runner-up and its
predicted drop) and `vsBank=`; `ak=` still shows the height pick, so a disagreement between the two is
visible in one line.

`ResetRun()` (called from the rebirth branch in `Main.SnipeZone`, next to the `TitanMoneyDone` wipe)
clears the denies and the cached gear factor: a rebirth wipes `realBaseGold` and re-grows the stats
every deny was measured against.
