using System;
using static NGUAdvisor.Main;

namespace NGUAdvisor.Managers
{
    public enum LockType
    {
        Titan,
        Yggdrasil,
        MoneyPit,
        Gold,
        Quest,
        Cooking,
        None
    }

    public static class LockManager
    {
        // Explicit idle default: LockType.Titan is enum value 0, so an uninitialized static would read as
        // Titan (CanSwap() false, HUD "Titan", a spurious titan restore) until Main.Start() resets it. The
        // initializer makes None the state from the type's first moment, on every path — healthy load,
        // failed/partial init, hot reload. Main.Start() still calls ReleaseLock() as a redundant reset.
        private static LockType currentLock = LockType.None;
        private static bool _swappedFromQuest;
        private static bool _swappedDiggers;
        private static bool _swappedBeards;

        public static bool HasTitanLock() => currentLock == LockType.Titan;

        public static bool HasMoneyPitLock() => currentLock == LockType.MoneyPit;

        public static bool HasGoldLock() => currentLock == LockType.Gold;

        public static bool HasQuestLock() => currentLock == LockType.Quest;

        public static bool HasCookingLock() => currentLock == LockType.Cooking;

        public static bool CanSwap() => currentLock == LockType.None || HasQuestLock();

        private static void AcquireLock(LockType newLock)
        {
            _swappedFromQuest = currentLock == LockType.Quest;
            currentLock = newLock;
        }

        private static bool CanAcquireNewLock(LockType newLock)
        {
            return currentLock != newLock && CanSwap();
        }

        internal static void ReleaseLock()
        {
            currentLock = LockType.None;
        }

        // Yggdrasil's restoration, reachable without asking whether a harvest is still due.
        //
        // TryYggdrasilSwap() cannot serve as the release: its same-lock branch is the else of
        // NeedsHarvest(), so while fruit is still unharvested — which is exactly what a thrown
        // harvest leaves behind — the call takes the acquire branch, fails CanAcquireNewLock
        // against its own lock and returns false, restoring nothing. The gate that blocks the
        // release is the one the harvest would have cleared.
        //
        // Self-selecting, so the caller never repeats the lock test: it fires only while the
        // Yggdrasil lock is actually held, and no-ops once R4 has already transitioned it to
        // None or Quest. Every other lock type belongs to someone else and is left alone.
        internal static void RestoreYggdrasilSwap()
        {
            if (currentLock == LockType.Yggdrasil)
                RestoreConfiguration();
        }

        private static void SaveConfiguration()
        {
            if (!_swappedFromQuest)
                LoadoutManager.SaveCurrentLoadout();
            BeardManager.SaveBeards();
            DiggerManager.SaveDiggers();
        }

        private static void RestoreConfiguration()
        {
            var backToQuest = false;
            var originalGearRestored = false;

            try
            {
                try
                {
                    backToQuest = _swappedFromQuest && Settings.AutoQuest;

                    LoadoutManager.RestoreGear();
                    // The restored gear predates the lock — if the segment/objective moved meanwhile, it's
                    // stale. Let the gear refresh re-evaluate on the next tick with its bypass re-armed.
                    AdvisorApply.GearRestored();

                    originalGearRestored = true;
                }
                finally
                {
                    // The mode lock must never outlive this method: a throw above (or AutoQuest going off
                    // mid-nest) would otherwise strand it and CanSwap() would suppress every later swap.
                    // Quest is only handed back once the pre-Quest gear is actually back on.
                    _swappedFromQuest = false;

                    if (originalGearRestored && backToQuest)
                        AcquireLock(LockType.Quest);
                    else
                        ReleaseLock();
                }

                if (backToQuest && (Settings.QuestLoadout.Length > 0 || !string.IsNullOrEmpty(Settings.QuestObjective)))
                    LoadoutManager.ChangeGear(GearOptimizer.ResolveModeGear(Settings.QuestObjective, Settings.QuestObjectiveRespawn, Settings.QuestLoadout));
            }
            finally
            {
                // Beard/digger restores must run on every path, including a throw from RestoreGear/ChangeGear
                // above. If they were skipped while the lock has already transitioned to None, the temporary
                // swapped set stays equipped and the next SaveConfiguration() would snapshot it as the new
                // baseline — permanent loadout corruption.
                if (_swappedBeards)
                {
                    BeardManager.RestoreBeards();
                    _swappedBeards = false;
                }

                if (_swappedDiggers)
                {
                    DiggerManager.RestoreDiggers();
                    _swappedDiggers = false;
                }
            }
        }

        // The lock is held from AcquireLock onward, so every helper's snapshot and temporary swap runs
        // under it. A throw in there used to carry the lock out of the helper, and no caller can release
        // what it never learned was taken — worst of them was Yggdrasil, whose restore branch sits behind
        // !NeedsHarvest(): the harvest that would have cleared that gate is the one the throw prevented,
        // so the lock stayed for the session and RebirthAvailable() (BaseRebirth:157) never came back.
        //
        // The cleanup fault is reported and dropped rather than rethrown. RestoreConfiguration transitions
        // the lock inside its own finally (R4), so by the time anything left in it can throw the lock is
        // already safe — None, or Quest after a nested restore — and gear, beards and diggers reconcile on
        // a later advisor pass. The acquisition fault is the one worth keeping: it is why the swap failed
        // at all, so it stays authoritative and reaches the caller with its original stack.
        private static void CleanupFailedAcquisition(LockType failedLock)
        {
            try
            {
                RestoreConfiguration();
            }
            catch (Exception cleanupEx)
            {
                // Passed in, not read from currentLock: the transition above has already moved it, so the
                // lock can no longer name the acquisition that failed.
                try
                {
                    LogDebug($"Lock cleanup after failed {failedLock} acquisition:\n{cleanupEx}");
                }
                catch
                {
                    // Best-effort diagnostic. The original acquisition exception remains authoritative.
                }
            }
        }

        public static void TryTitanSwap()
        {
            if (CanAcquireNewLock(LockType.Titan) && ZoneHelpers.AnyTitansSpawningSoon())
            {
                AcquireLock(LockType.Titan);

                try
                {
                    SaveConfiguration();

                    if (ZoneHelpers.ShouldRunGoldLoadout())
                    {
                        Log("Switching to Gold Drop configuration for titans");
                        LoadoutManager.ChangeGear(GearOptimizer.ResolveGoldGear());
                    }
                    else if (ZoneHelpers.ShouldRunTitanLoadout())
                    {
                        Log("Switching to Titan configuration");
                        LoadoutManager.ChangeGear(GearOptimizer.ResolveTitanGear());
                    }

                    if (Settings.SwapTitanBeards)
                    {
                        BeardManager.EquipBeards(currentLock);
                        _swappedBeards = true;
                    }

                    if (Settings.SwapTitanDiggers)
                    {
                        DiggerManager.EquipDiggers(currentLock);
                        DiggerManager.RecapDiggers();
                        _swappedDiggers = true;
                    }
                }
                catch
                {
                    CleanupFailedAcquisition(LockType.Titan);
                    throw;
                }
            }
            else if (currentLock == LockType.Titan)
            {
                RestoreConfiguration();
            }
        }

        public static bool TryYggdrasilSwap(bool forced = false)
        {
            if (YggdrasilManager.NeedsHarvest(forced))
            {
                if (CanAcquireNewLock(LockType.Yggdrasil))
                {
                    AcquireLock(LockType.Yggdrasil);

                    try
                    {
                        SaveConfiguration();

                        if (Settings.SwapYggdrasilLoadouts && (forced || YggdrasilManager.NeedsSwap()))
                        {
                            Log("Switching to Yggdrasil configuration");
                            LoadoutManager.ChangeGear(GearOptimizer.ResolveModeGear(Settings.YggdrasilObjective, Settings.YggdrasilObjectiveRespawn, Settings.YggdrasilLoadout));
                        }
                        else
                        {
                            Log("Switching to Yggdrasil configuration without gear swap");
                        }

                        if (Settings.SwapYggdrasilBeards)
                        {
                            BeardManager.EquipBeards(currentLock);
                            _swappedBeards = true;
                        }

                        if (Settings.SwapYggdrasilDiggers)
                        {
                            DiggerManager.EquipDiggers(currentLock);
                            DiggerManager.RecapDiggers();
                            _swappedDiggers = true;
                        }

                        return true;
                    }
                    catch
                    {
                        CleanupFailedAcquisition(LockType.Yggdrasil);
                        throw;
                    }
                }
            }
            else if (currentLock == LockType.Yggdrasil)
            {
                RestoreConfiguration();
            }
            return false;
        }

        public static bool TryMoneyPitSwap(int[] loadout = null, int[] diggers = null, bool shockwave = false)
        {
            if (CanAcquireNewLock(LockType.MoneyPit))
            {
                AcquireLock(LockType.MoneyPit);

                try
                {
                    SaveConfiguration();

                    if (loadout?.Length > 0)
                        LoadoutManager.ChangeGear(loadout, shockwave);

                    if (diggers?.Length > 0 && Settings.SwapPitDiggers)
                    {
                        BeardManager.EquipBeards(currentLock);
                        _swappedBeards = true;

                        DiggerManager.EquipDiggers(diggers);
                        DiggerManager.RecapDiggers();
                        _swappedDiggers = true;
                    }

                    return true;
                }
                catch
                {
                    CleanupFailedAcquisition(LockType.MoneyPit);
                    throw;
                }
            }
            else if (currentLock == LockType.MoneyPit)
            {
                RestoreConfiguration();
            }
            return false;
        }

        public static bool TryGoldDropSwap()
        {
            if (CanAcquireNewLock(LockType.Gold))
            {
                AcquireLock(LockType.Gold);

                try
                {
                    SaveConfiguration();

                    Log("Switching to Gold configuration");
                    LoadoutManager.ChangeGear(GearOptimizer.ResolveGoldGear());

                    return true;
                }
                catch
                {
                    CleanupFailedAcquisition(LockType.Gold);
                    throw;
                }
            }
            else if (currentLock == LockType.Gold)
            {
                if (Settings.ManageGoldLoadouts)
                {
                    Log("Gold Loadout kill done. Turning off setting and swapping gear");
                    Settings.GoldSnipeComplete = true;
                }
                RestoreConfiguration();
            }
            return false;
        }

        public static bool TryQuestSwap()
        {
            if (CanAcquireNewLock(LockType.Quest))
            {
                AcquireLock(LockType.Quest);

                try
                {
                    SaveConfiguration();

                    if (Settings.ManageQuestLoadouts)
                    {
                        Log("Switching to Quest configuration");
                        LoadoutManager.ChangeGear(GearOptimizer.ResolveModeGear(Settings.QuestObjective, Settings.QuestObjectiveRespawn, Settings.QuestLoadout));
                    }

                    return true;
                }
                catch
                {
                    CleanupFailedAcquisition(LockType.Quest);
                    throw;
                }
            }
            else if (currentLock == LockType.Quest)
            {
                RestoreConfiguration();
            }
            return false;
        }

        public static bool TryCookingSwap()
        {
            if (CanAcquireNewLock(LockType.Cooking))
            {
                AcquireLock(LockType.Cooking);

                try
                {
                    SaveConfiguration();

                    Log("Switching to Cooking configuration");
                    LoadoutManager.ChangeGear(GearOptimizer.ResolveModeGear(Settings.CookingObjective, Settings.CookingObjectiveRespawn, Settings.CookingLoadout));

                    return true;
                }
                catch
                {
                    CleanupFailedAcquisition(LockType.Cooking);
                    throw;
                }
            }
            else if (currentLock == LockType.Cooking)
            {
                RestoreConfiguration();
            }
            return false;
        }

        public static string GetLockTypeName()
        {
            switch (currentLock)
            {
                case LockType.Cooking:
                    return "Cooking";
                case LockType.Gold:
                    return "Gold";
                case LockType.MoneyPit:
                    return "Money Pit";
                case LockType.None:
                    return "Default";
                case LockType.Quest:
                    return "Quest";
                case LockType.Titan:
                    return "Titan";
                case LockType.Yggdrasil:
                    return "Yggdrasil";
            }
            return "Unknown";
        }
    }
}
