# QuestManager (`Managers/QuestManager.cs`)

Beast quest automation: accept/idle/manual, butter, turn-in, the quest gear lock, and the
advisor's "capstone hold".

## STRICT RULE: a major quest is NEVER idled

Idling ticks `idleProgress`, and the first full tick clears `allActive` — **permanently
forfeiting that quest's manual-completion QP/AP bonus**. Enforced twice: `SetIdleMode` coerces
`idle=false` for any major regardless of caller reasoning, and `EnforceMajorNeverIdle()` catches a
major already idling (e.g. the in-game toggle left on when it started).

## Bank overfill predictor (`UpdateBankOverfill`)

`slots = maxBankedQuests − curBankedQuests + 1`; time until the bank overflows =
`slots × timerThreshold() − dailyQuestTimer`; ETA to finish the current quest =
`expectedTimePerDrop() × idleDropFactor() × remainingDrops` (average drops 50 with perk 94 ≥ 610,
else 54.5). Overfill = `time × 1.1 < eta`. Overfill forces questing (banked regen must never be
wasted) and is the hard veto on the capstone hold.

## Capstone hold (`CapstoneHold`) — opt-in

A ready major quest is FREE forced-farming time in its zone, so hold the turn-in while any zone
item is still uncapped. `ZoneItems` is decomp-extracted per-zone droppable gear id data (same
provenance as GearFarmAdvisor's table). Guards, each from a report:

- **Opt-in** (`Settings.QuestHoldForGear`, default off): a major parked at 100 % for hours read as
  a hang; Gear Hunt is now the deliberate gear-farming tool.
- Never during a pooled-major burst (`PoolMajorQuests && QuestBurstActive` — bursts exist to CHAIN
  quests) and never while `GearHunter.Active` owns farming.
- Never against `questBankOverfill`.
- **Free inventory slots ≥ 4**: with a full inventory the held-for gear can't even drop, while
  at-target quest items keep flooding the remaining slots.
- **Skip loot-filtered items** — a filtered item never drops, so the hold would wait forever
  (log-audit find: holds expiring without progress).
- Budget **180 min** (was 20 — user: "10 majors, nothing capped"); the overfill guard is the real
  cost control, the clock is only a runaway stop. Hold logged at most every 5 min.

## Turn-in (`CheckQuestTurnin`)

`readyToHandIn()` → capstone hold check → **one butter attempt per quest** (`_butterAttempted`;
`tryUseButter` can fail on AP — the old at-target-minus-2 window retried every pass, 45 min of
log spam) → `completeQuest()` → release the quest lock when the bank is empty, or when majors are
off and minors aren't manualed.

## Routing

`UpdateShouldQuest`: majors (and forced overfill) outrank adventure zones; otherwise questing
yields to an unlocked snipe zone unless ITOPOD-targeting or zone fallthrough is allowed.
`IsQuesting()` returns the quest zone (equipping the quest loadout) or −1 — the routing hook
`Main`/`CombatManager` use.
