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
item is still uncapped. The table lives in `QuestZoneItems.cs` — its own Unity-free file so
`QuestZoneItemsTests` can link it — and carries the extraction rule plus a `[DECOMP] LootDrop.cs:<line>`
cite per row. **Two rules the old table broke, both of which defeated the feature:** the ids are
every `makeLoot(id)` AND `makeLevelledLoot(id, ...)` in `LootDrop.zone<N>Drop` (the old one had only
the levelled ones, so zone 9 held one id of eight and zones 2/5/12/13 each missed a boss set), and
they are filtered to EQUIPMENT types — a `part.Misc` id can never be maxed (never equipped → never
merged → level stays 0, and `itemMaxxed` is only set at level ≥ 100,
`[DECOMP] InventoryController.cs:2374`), and since the consumer breaks on the FIRST un-maxed id, the
five Misc ids (66, 339, 367, 369, 370) pinned the hold for its whole budget — zones 12 and 13 listed
one first, so they could never finish a hold on gear at all. Only ten zones are reachable:
`curQuestZone()` returns `{1,2,5,9,12,13,15,20,21,22}` or −100
(`[DECOMP] BeastQuestController.cs:997-1013`), so the other 24 rows are gone.

Guards, each from a report:

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
yields to an unlocked snipe zone unless that zone is the ITOPOD or zone fallthrough is allowed.

**A banked major outranks a farm zone** (`BankedMajorOutranksFarming`, user rule 2026-09-10):
banked majors are capped and regenerate on a timer, so farming while one waits throws that regen
away. Before this, a major reached the game ONLY through the overfill predictor — any zone the
boost/gear farm routed read as a committed snipe, `majorQuests &= shouldQuest` cleared them, and
pooling could not get past it either (the burst is computed above that same line), so with the farm
routing a zone the answer to "why won't it take a major" was "it never can". Two owners still
outrank a waiting major: Gear Hunt (the deliberate gear-farming tool, which the capstone hold also
yields to) and pooling before its burst (banking to cap IS the pooled strategy).

**The stand-down predicate is `QuestStandDown.IsSniping`, fed by `Main.ResolveIntentZone`** — never a
local copy of a routing row, and never the zone that actually routed. Read that file's header before
touching it: the old inline expression carried its own `!Settings.AdventureTargetITOPOD` term, which
could not see the gear-hunt row above it (quests pre-empted a running hunt), and asked
`IsZoneUnlocked(Settings.SnipeZone)`, which calls a character parked in the pod by the boost farm
"sniping" and refused to quest there. Taking the ROUTED zone instead would close an oscillator:
questing sits above adventure routing, so it would be reading its own output.
`IsQuesting()` returns the quest zone (equipping the quest loadout) or −1 — the routing hook
`Main`/`CombatManager` use.
