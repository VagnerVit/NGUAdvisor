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

## The 50-item minor re-roll — free, and therefore default ON

`startQuest` rolls `targetDrops = Random.Range(50, 60)` — **50..59, mean 54.5** — collapsing to a
flat 50 once itopod **perk 94 ≥ 610** (`[DECOMP] BeastQuestController.cs:450-454`). Three facts make
re-rolling a minor strictly dominant:

- **The reward never reads `targetDrops`.** `giveRewardsAndClear` pays
  `minorQuestReward() * questRewardFactor() * allActiveModifier()` — a 59-item minor pays exactly
  what a 50-item one does (`[DECOMP] BeastQuestController.cs:482-503`).
- **`skipQuest()` is just `clearQuest()`** — no cost, no cooldown, no confirmation on this path
  (`[DECOMP] BeastQuestController.cs:949-954`), and a minor consumes no bank.
- `ManageQuests` runs from `QuickStuff` every 0.5 s, so the ~10 rolls it takes to land a 50 cost
  about ten seconds.

The test is **marginal, not average**: `targetDrops - curDrops > 50` compares the work REMAINING
against a fresh 50-item quest, so drops already banked are correctly treated as sunk. Average saving
≈ 8 % of quest kills for identical QP/AP.

**With perk 94 the rule is a no-op** (50 − 0 is not > 50), which is why `ApplyQuests` asserting it
from `perk94 >= 610` was inverted: on exactly the accounts where re-rolling pays, it turned the rule
off. It is now always on, and the SavedSettings default is ON.

It stands down while a quest item is being levelled (below): `curDrops` never moves there, so the
test would hold forever, and each re-roll can return a different zone — which stops the locked item
dropping at all.

## Levelling a quest item to 100 — the padlock is the whole switch

`InventoryManager.LevellingQuestItem()` (a LOCKED, un-maxxed id 278-287) is the intent, and there is
no setting: the same padlock already decides which quest items the merge pass touches
(InventoryManager.cs:268 merges locked copies only). **Why quest strategy has to ask:** an IDLE quest
drops NOTHING — `updateIdleQuest` ticks `idleProgress` and advances `curDrops` directly
(`[DECOMP] BeastQuestController.cs:787-799`) — so while the advisor forced `ManualMinors = false`
(AdvisorApply.cs:600), no copy ever reached the inventory and the item could not gain a level.

While one is being levelled, four things change, each because the previous one alone does nothing:

| Change | Why |
|---|---|
| `ManualMinors = true` (AdvisorApply) | manual is the only mode that drops items at all |
| questing outranks the farm zone (`LevellingQuestItemOutranksFarming`) | the drops are kills IN the quest zone |
| banked majors are NOT started | minors are unlimited, the bank is not; both drop the item equally per kill |
| no progress-based minor abandon | `mergeAll` consumes every unlocked copy before it can count, so progress stays under the threshold BY DESIGN — abandoning would skip-loop forever |

It ends itself: at level 100 the item is `itemMaxxed`, the predicate goes false, and the ordinary
strategy (idle minors, majors from the bank) resumes. **Gear Hunt still outranks all of it**, and an
imminent bank overflow still forces a major through — the pooled burst too, since that is an explicit
request to empty the bank.

## Quest gear — the most quest items per second (`GearOptimizer.ResolveQuestGear`)

Switch: `ManageQuestLoadouts` ("Quest Gear", on the Quests panel row visible in both modes — the
advisor never sets it, so it must not live in the manual-only rulebook). Game truth: every manual kill in the quest zone rolls
`questDropChance() = 0.05 × (1 + gear QuestDrop) × sigil × ITOPOD` (`[DECOMP] BeastQuestController.cs:865-879`,
the tail of each `LootDrop.zone<N>Drop`, no early return), and gear Respawn enters `respawnTime()` as
`max(0.2, 1 − R)` (`[DECOMP] AdventureController.cs:796-814`). So items/s = `(1 + QD) × kills/s`, and
kills/s is `ZoneCadence.For(zone, QuestCombatMode, projectedAttack, projectedRespawn)`.

The search walks `Adventure(k) > Respawn(r) > Quest Drops(all) > Adventure(all)`, `k` down from all
accessories, `r` up until an extra slot buys no respawn, and keeps the best rate. **A candidate may
give up Adventure accessories only while every spawn stays a projected one-shot** — nothing gets a
turn, so no Toughness model is needed; a zone the full Adventure set does not one-shot keeps it.
Ties keep the stronger set. Pins and `QuestObjectiveRespawn` apply as everywhere.

The QUEST card still wins where it says something else: objective empty + static list → the list;
an objective other than `Quest Drops`/`Respawn` (the rate's own two halves) → that plain objective.

The set is solved AFTER `startQuest()` (before it the zone is unknown), and `LockManager.RefreshQuestGear`
re-solves it when the next quest under the same lock rolls a different zone — the lock is released
only once the bank is empty. Only one main-slot item carries QuestDrop (weapon 415, A Giant Scythe);
the Adventure lead owns the main slots and picks it only on Power.

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
