using System;
using static NGUAdvisor.Main;

namespace NGUAdvisor.Managers
{
    public static class QuestManager
    {
        private static readonly Character _character = Main.Character;
        private static readonly BeastQuestController _qc = _character.beastQuestController;
        private static bool shouldQuest;
        private static bool questBankOverfill;

        private static BeastQuest Quest => _character.beastQuest;

        public static bool BankOverfill => questBankOverfill;

        // Capstone hold (advisor): a major quest is free forced-farming time in its zone — if the
        // zone's gear isn't all maxed, hold the turn-in and keep fighting so drops keep merging.
        // Guards: never against the bank-overfill predictor; 20-minute budget per quest.
        private static DateTime _capstoneStart = DateTime.MinValue;
        private static DateTime _lastHoldLog = DateTime.MinValue;

        public static string CapstoneItem { get; private set; }

        public static bool CapstoneHold()
        {
            CapstoneItem = null;
            try
            {
                if (Settings == null || !Settings.AdvisorQuests) return false;
                // OPT-IN (user 2026-07-11: a ready major parked at 100% for hours read as a hang —
                // and Gear Hunt is now the deliberate gear-farming tool, so the hold defaults off).
                if (!Settings.QuestHoldForGear) return false;
                // A pooled-major burst exists to CHAIN quests — never hold one open mid-burst;
                // and while the hunt owns farming, quest zones aren't where gear time goes.
                if (Settings.PoolMajorQuests && Settings.QuestBurstActive) return false;
                try { if (GearHunter.Active) return false; } catch { }
                if (!Quest.inQuest || Quest.reducedRewards)
                {
                    _capstoneStart = DateTime.MinValue;
                    return false;
                }
                if (questBankOverfill) return false;

                // A nearly-full inventory defeats the hold: no free slots means the zone gear we're
                // holding for can't even drop, while at-target quest items flood what's left (they
                // keep dropping on every manual kill and the game never counts them past target).
                if (FreeInventorySlots() < 4) return false;

                int zone = _qc.curQuestZone();
                if (!QuestZoneItems.ZoneItems.TryGetValue(zone, out var ids)) return false;

                // Unmaxed AND actually farmable: a loot-filtered item never drops, so holding for
                // it would wait forever (log-audit find: holds expiring without progress).
                var il = _character.inventory.itemList;
                int missing = -1;
                foreach (var id in ids)
                {
                    if (id >= il.itemMaxxed.Count || il.itemMaxxed[id]) continue;
                    bool filteredOut = false;
                    try { filteredOut = id < il.itemFiltered.Count && il.itemFiltered[id]; } catch { }
                    if (filteredOut) continue;
                    missing = id;
                    break;
                }
                if (missing < 0)
                {
                    _capstoneStart = DateTime.MinValue;
                    return false;
                }

                // Budget: the bank-overfill guard above is the real cost control (banked regen never
                // wasted); the clock is only a runaway stop. 20 minutes proved far too short to cap
                // an item (user: 10 majors, nothing capped) — 3 hours of forced farm time is cheap.
                if (_capstoneStart == DateTime.MinValue) _capstoneStart = DateTime.UtcNow;
                if ((DateTime.UtcNow - _capstoneStart).TotalMinutes > 180) return false;

                CapstoneItem = Main.ItemNameNice(missing);
                try
                {
                    var slot = LoadoutManager.FindItemSlot(missing);
                    if (slot != null) CapstoneItem += $" (lv {slot.level}/100)";
                }
                catch { }
                return true;
            }
            catch (Exception e)
            {
                Main.LogDebug($"CapstoneHold: {e.Message}");
                return false;
            }
        }

        private static int FreeInventorySlots()
        {
            try
            {
                var inv = _character.inventory.inventory;
                int free = 0;
                for (int i = 0; i < inv.Count; i++)
                    if (inv[i] == null || inv[i].id == 0) free++;
                return free;
            }
            catch { return int.MaxValue; }   // unknown -> don't trip the guard
        }

        public static void PerformSlowActions()
        {
            EnforceMajorNeverIdle();
            UpdateBankOverfill();
            UpdateShouldQuest();
            CheckQuestTurnin();
        }

        // STRICT RULE (user): a major quest is NEVER run in idle mode. Idling ticks idleProgress,
        // and the first full tick clears allActive — permanently forfeiting this quest's manual
        // completion QP/AP bonus. SetIdleMode coerces new requests; this catches a major that is
        // already idling (e.g. the in-game toggle was left on when the quest started).
        private static void EnforceMajorNeverIdle()
        {
            try
            {
                if (Quest.inQuest && !Quest.reducedRewards && Quest.idleMode)
                {
                    Log("Major quest was in idle mode — forcing manual (strict rule: majors never idle)");
                    Quest.idleMode = false;
                    _qc.updateButtons();
                    _qc.updateButtonText();
                }
            }
            catch (Exception e) { Main.LogDebug($"EnforceMajorNeverIdle: {e.Message}"); }
        }

        private static void UpdateBankOverfill()
        {
            if (!Settings.AutoQuest)
            {
                questBankOverfill = false;
                return;
            }

            var slots = _qc.maxBankedQuests() - Quest.curBankedQuests + 1;
            var time = slots * _qc.timerThreshold() - Quest.dailyQuestTimer.totalseconds;
            var averageDrops = Settings.FiftyItemMinors || _character.adventure.itopod.perkLevel[94] >= 610 ? 50f : 54.5f;
            var remainingDrops = Quest.inQuest ? Quest.targetDrops - Quest.curDrops : averageDrops;
            var eta = _qc.expectedTimePerDrop() * _qc.idleDropFactor() * remainingDrops;
            // Give a bit of extra time for safety
            questBankOverfill = time * 1.1f < eta;
        }

        // A banked major beats a farm zone (user rule 2026-09-10). Banked majors are CAPPED and
        // regenerate on a timer, so farming while one waits throws that regen away — while the farm
        // zone keeps paying whenever we return to it. Without this, majors reached the game only
        // through the overfill predictor: any zone the boost/gear farm routed read as a committed
        // snipe, `majorQuests &= shouldQuest` cleared them, and pooling could not get past it either
        // (the burst is computed above that same line). Two owners still outrank a waiting major:
        //   * Gear Hunt — the deliberate gear-farming tool, which the capstone hold also yields to.
        //   * pooling before its burst — banking to cap IS the pooled strategy; spending one major
        //     early is exactly what pooling exists to prevent.
        private static bool BankedMajorOutranksFarming()
        {
            if (!Settings.AdvisorQuests || !Settings.AllowMajorQuests) return false;
            if (Quest.curBankedQuests <= 0) return false;
            if (Settings.PoolMajorQuests && !Settings.QuestBurstActive) return false;
            return !GearHuntOwnsFarming();
        }

        // A MANUAL minor progresses ONLY from kills in the quest zone: an idle quest advances its own
        // bar, a manual one needs the drops. So "Manual Minors" is itself a request for the zone —
        // without this the toggle produced a quest parked at 0/N while the farm kept the character in
        // its boost zone (user-reported, screenshot: MINOR Forest 0/50 · fighting). Deliberately NOT
        // gated on AdvisorQuests: in MANUAL decisions the user's rulebook is the decision, and this
        // toggle is that rulebook saying it.
        private static bool ManualMinorNeedsTheZone()
            => Settings.ManualMinors && !GearHuntOwnsFarming();

        // Levelling a quest item to 100 needs kills IN THE QUEST ZONE — the drops are the levels — so
        // questing has to win the zone or the padlock means nothing. Also ungated on AdvisorQuests:
        // the padlock is the user's own signal either way (InventoryManager.LevellingQuestItem).
        private static bool LevellingQuestItemOutranksFarming()
            => InventoryManager.LevellingQuestItem()
            && !GearHuntOwnsFarming();

        private static bool GearHuntOwnsFarming()
        {
            try { return GearHunter.Active; } catch { return false; }
        }

        private static void UpdateShouldQuest()
        {
            if (!Settings.AutoQuest)
            {
                shouldQuest = false;
            }
            // Major quests take precedence over adventure zones
            else if (Quest.inQuest && !Quest.reducedRewards
                  || Settings.QuestsFullBank && questBankOverfill
                  || BankedMajorOutranksFarming()
                  || LevellingQuestItemOutranksFarming()
                  || ManualMinorNeedsTheZone())
            {
                shouldQuest = true;
            }
            else if (Settings.CombatEnabled)
            {
                // Don't quest if combat is enabled, the zone that would route is unlocked, it is not
                // the ITOPOD, and Fallthrough is not allowed. The zone comes from
                // Main.ResolveIntentZone — the single owner of the routing cascade — instead of the
                // old hand copy of its Target ITOPOD row; QuestStandDown carries the argument,
                // including why THIS consumer wants the intent rather than the routed zone.
                int intentZone = Main.ResolveIntentZone(out _);
                var isSniping = QuestStandDown.IsSniping(intentZone,
                    CombatManager.IsZoneUnlocked(intentZone), Settings.AllowZoneFallback);

                if (isSniping)
                {
                    if (LockManager.HasQuestLock())
                        LockManager.TryQuestSwap();

                    SetIdleMode(Quest.reducedRewards && !Settings.ManualMinors);
                }

                shouldQuest = !isSniping;
            }
        }

        // One butter attempt per quest, made right before the actual turn-in (log-audit find: the old
        // at-target-minus-2 window retried a failing tryUseButter every pass — 45 minutes of
        // "Buttering Major Quest" spam while the capstone hold kept the quest at target).
        private static bool _butterAttempted;

        private static void CheckQuestTurnin()
        {
            if (!Quest.inQuest)
            {
                _butterAttempted = false;
                return;
            }

            if (_qc.readyToHandIn())
            {
                if (CapstoneHold())
                {
                    if ((DateTime.UtcNow - _lastHoldLog).TotalMinutes >= 5)
                    {
                        _lastHoldLog = DateTime.UtcNow;
                        Log($"Holding quest turn-in — maxing {CapstoneItem} while the zone is free farm time");
                        ChallengeOverlay.Record("QUEST", $"quest hold: {CapstoneItem}", "maxing zone gear before turn-in");
                    }
                    return;
                }

                if (!Quest.usedButter && !_butterAttempted)
                {
                    _butterAttempted = true;   // tryUseButter can fail (AP) — never retry-spam
                    if (Quest.reducedRewards && Settings.UseButterMinor)
                    {
                        Log("Buttering Minor Quest");
                        _qc.tryUseButter();
                    }
                    else if (!Quest.reducedRewards && Settings.UseButterMajor)
                    {
                        Log("Buttering Major Quest");
                        _qc.tryUseButter();
                    }
                }

                Log("Turning in quest");
                _qc.completeQuest();
                _butterAttempted = false;

                // Check if we need to swap back gear and release lock
                if (LockManager.HasQuestLock())
                {
                    // No more quests, swap back
                    if (_character.beastQuest.curBankedQuests == 0)
                        LockManager.TryQuestSwap();
                    // Else if majors are off and we're not manualing minors, swap back
                    else if (!Settings.AllowMajorQuests && !Settings.ManualMinors)
                        LockManager.TryQuestSwap();
                }
            }
        }

        public static int IsQuesting()
        {
            if (!Settings.AutoQuest)
                return -1;

            if (!Quest.inQuest)
                return -1;

            if (!shouldQuest)
                return -1;

            if (Quest.reducedRewards && !Settings.ManualMinors)
                return -1;

            int questZone = _qc.curQuestZone();
            if (!CombatManager.IsZoneUnlocked(questZone))
                return -1;

            EquipQuestingLoadout();
            return questZone;
        }

        private static void SetIdleMode(bool idle)
        {
            // STRICT RULE (user): a major quest is NEVER run in idle mode (see EnforceMajorNeverIdle).
            // Coerce here so no caller can idle a major, whatever its reasoning.
            if (idle && Quest.inQuest && !Quest.reducedRewards)
                idle = false;

            if (Quest.idleMode != idle)
            {
                Quest.idleMode = idle;
                _qc.updateButtons();
                _qc.updateButtonText();
            }
        }

        public static void ManageQuests()
        {
            if (!Settings.AutoQuest)
            {
                if (LockManager.HasQuestLock())
                    LockManager.TryQuestSwap();
                return;
            }

            // While a locked quest item is being levelled, minors are the farm: they are unlimited and
            // the bank is not, and both quest types drop the item equally per manual kill. Overfill
            // below still overrides — banked regen must never be wasted — and so does a pooled burst,
            // which is an explicit request to empty the bank.
            var majorQuests = Settings.AllowMajorQuests && Quest.curBankedQuests > 0
                && !InventoryManager.LevellingQuestItem();
            // Check if Quest Bank will overfill before we can finish the current idle quest
            majorQuests |= Settings.QuestsFullBank && questBankOverfill;

            // POOL MAJOR QUESTS (user rule): bank to cap, then burn the WHOLE bank in one burst,
            // then pool again — and never start a major while Gear Hunt owns routing (the burst
            // waits at cap until the hunt is done; the wasted regen at cap is the accepted cost).
            // Burst state persists in settings so a reload mid-burst keeps burning the bank.
            if (Settings.PoolMajorQuests && Settings.AllowMajorQuests)
            {
                bool burst = Settings.QuestBurstActive;
                int cap = 10;
                try { cap = Math.Max(1, _qc.maxBankedQuests()); } catch { }
                if (!burst && Quest.curBankedQuests >= cap) burst = true;
                else if (burst && Quest.curBankedQuests <= 0) burst = false;   // in-flight major still completes
                if (burst != Settings.QuestBurstActive)
                {
                    Settings.QuestBurstActive = burst;
                    Log(burst
                        ? $"Advisor: quest bank at cap ({cap}) — bursting every banked major"
                        : "Advisor: major burst complete — pooling the quest bank again");
                    ChallengeOverlay.Record("QUEST",
                        burst ? "major quest BURST" : "burst done — pooling majors",
                        burst ? $"bank {cap}/{cap}" : "bank spent");
                }
                bool hunting = false;
                try { hunting = GearHunter.Active; } catch { }
                majorQuests = burst && !hunting && Quest.curBankedQuests > 0;
            }
            else if (Settings.QuestBurstActive)
            {
                // Pooling toggled off mid-burst: clear the persisted flag, or re-enabling pooling
                // later would burst immediately at a part-filled bank instead of pooling to cap.
                Settings.QuestBurstActive = false;
            }
            majorQuests &= shouldQuest;

            // First logic: not in a quest
            if (!Quest.inQuest)
            {
                var startQuest = false;

                // If we're allowing major quests and we have a quest available and we should quest
                if (majorQuests)
                {
                    _character.settings.useMajorQuests = true;
                    SetIdleMode(false);
                    EquipQuestingLoadout();
                    startQuest = true;
                    // Starting a major was the one quest action that logged nothing, so a major that
                    // never started and a major that started looked identical in the log.
                    Log($"Starting a major quest ({Quest.curBankedQuests} banked)");
                }
                else if (!Settings.ManualMinors || shouldQuest)
                {
                    _character.settings.useMajorQuests = false;
                    SetIdleMode(!Settings.ManualMinors);

                    if (Settings.ManualMinors && shouldQuest)
                        EquipQuestingLoadout();
                    else if (LockManager.HasQuestLock())
                        LockManager.TryQuestSwap();

                    startQuest = true;
                }

                if (startQuest)
                {
                    _qc.startQuest();
                    _qc.refreshMenu();
                }
                // If we're not questing and we still have the lock, restore gear
                else if (LockManager.HasQuestLock())
                {
                    LockManager.TryQuestSwap();
                }

                return;
            }

            // Second logic, we're in a quest
            if (Quest.reducedRewards)
            {
                // While POOLING (not bursting), overfill can't start a major anyway — abandoning
                // the minor for it would just skip-loop at the cap boundary. Sitting at cap is
                // the pooling trade-off; the burst is what spends the bank.
                var abandonQuest = Settings.QuestsFullBank && questBankOverfill
                    && !(Settings.PoolMajorQuests && !Settings.QuestBurstActive);
                // Never abandon on progress while levelling a quest item: the merge pass eats the
                // drops before they can count, so progress STAYS under the threshold by design and
                // the minor would be skipped and restarted forever.
                if (majorQuests && Settings.AbandonMinors && Quest.targetDrops > 0
                    && !InventoryManager.LevellingQuestItem())
                {
                    float progress = Quest.curDrops / (float)Quest.targetDrops * 100;
                    // If all this is true get rid of this minor quest
                    abandonQuest |= progress <= Settings.MinorAbandonThreshold;
                }
                // The 50-item re-roll is OFF while levelling a quest item: curDrops never moves (the
                // merge pass eats the drops), so the test would hold forever, and every re-roll can
                // hand back a DIFFERENT zone — which stops the locked item dropping at all.
                abandonQuest |= Settings.FiftyItemMinors && Quest.targetDrops - Quest.curDrops > 50
                    && !InventoryManager.LevellingQuestItem();

                if (abandonQuest)
                {
                    _qc.skipQuest();
                    _qc.refreshMenu();
                }
                else
                {
                    SetIdleMode(!Settings.ManualMinors);
                }
            }
            else
            {
                SetIdleMode(false);
            }
        }

        public static void EquipQuestingLoadout()
        {
            if (!Settings.ManageQuestLoadouts)
                return;

            if (!LockManager.HasQuestLock())
            {
                if (!LockManager.TryQuestSwap())
                    Log("Tried to equip quest loadout but unable to acquire lock");
            }
        }
    }
}
