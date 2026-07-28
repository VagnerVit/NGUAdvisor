using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static NGUAdvisor.Main;

namespace NGUAdvisor.Managers
{
    public static class YggdrasilManager
    {
        private static readonly Character _character = Main.Character;
        private static readonly AllYggdrasil _yc = _character.yggdrasilController;
        private static readonly FruitController _fc = _yc.fruits[0];

        private static List<Fruit> Fruits => _character.yggdrasil.fruits;

        public static bool AnyHarvestable()
        {
            for (var i = 0; i < Fruits.Count; i++)
            {
                if (_fc.harvestTier(i) > 0)
                    return true;
            }

            return false;
        }

        private static float EquipYggdrasilYield()
        {
            float result = 1f;
            foreach (var id in Settings.YggdrasilLoadout)
            {
                ih item = LoadoutManager.FindItemSlot(id);
                if (item == null)
                    continue;

                if (item.equipment.spec1Type == specType.Yggdrasil)
                    result += item.equipment.spec1Cur / 1e7f;
                if (item.equipment.spec2Type == specType.Yggdrasil)
                    result += item.equipment.spec2Cur / 1e7f;
                if (item.equipment.spec3Type == specType.Yggdrasil)
                    result += item.equipment.spec3Cur / 1e7f;
            }
            return result;
        }

        private static long MacguffinFruit2Bonus(int tier, bool firstHarvest = true)
        {
            var fruit = Fruits[13];
            int tierFactor = _fc.tierFactor(tier);
            bool usePoop = fruit.usePoop && (!_character.settings.poopOnlyMaxTier || tier == (int)fruit.maxTier);
            float poopModifier = usePoop ? _character.allArbitrary.poopModifier() : 1f;
            float harvestBonus = firstHarvest ? _character.adventureController.itopod.totalHarvestBonus(13) : 1f;
            float equipBonus = EquipYggdrasilYield();
            var result = (long)Mathf.Ceil(tierFactor * 0.1f * poopModifier * equipBonus * _character.yggdrasilYieldBonus() * harvestBonus);
            if (result >= int.MaxValue)
                result = int.MaxValue;
            if (result < 0)
                result = 0L;
            return result;
        }

        private static bool MacguffinFruit2Ready()
        {
            var fruit = Fruits[13];
            if (!fruit.eatFruit)
                return false;

            long maxTier = fruit.maxTier;
            if (maxTier < 1)
                return false;

            int harvestTier = fruit.harvestTier();
            if (harvestTier < 1)
                return false;

            if (fruit.usePoop && !_character.settings.poopOnlyMaxTier)
                return false;

            var maxBonus = (double)MacguffinFruit2Bonus((int)maxTier) / maxTier;
            var bonus = (double)MacguffinFruit2Bonus(harvestTier);
            bonus += (maxTier - harvestTier) * (double)MacguffinFruit2Bonus(1, false);
            bonus /= maxTier;
            if (bonus <= maxBonus)
                return false;

            return true;
        }

        private static bool QPFruitReady()
        {
            var fruit = Fruits[14];
            if (!fruit.eatFruit)
                return false;

            long maxTier = fruit.maxTier;
            if (maxTier < 1)
                return false;

            int harvestTier = fruit.harvestTier();
            if (harvestTier < 1)
                return false;

            if (harvestTier < Settings.YggSwapThreshold && Settings.SwapYggdrasilLoadouts && Settings.YggdrasilLoadout.Length > 0)
                return false;

            if (fruit.usePoop)
                return false;

            if (_character.adventureController.itopod.totalHarvestBonus(14) > 1f)
                return false;

            return true;
        }

        public static bool NeedsHarvest(bool forced = false)
        {
            if (forced)
                return AnyHarvestable();
            return _yc.anyFruitMaxxed() || MacguffinFruit2Ready() || QPFruitReady();
        }

        public static bool NeedsSwap()
        {
            int thresh = Math.Max(1, Settings.YggSwapThreshold);
            for (var i = 0; i < Fruits.Count; i++)
            {
                if (_fc.harvestTier(i) >= thresh && _fc.fruitMaxxed(i))
                    return true;
            }

            return false;
        }

        public static void ManageYggHarvest()
        {
            if (LockManager.TryYggdrasilSwap())
                HarvestAll();
        }

        // Owns the Yggdrasil lock from the moment TryYggdrasilSwap() hands it over: every caller —
        // the automatic pass, PreRebirth, and both manual buttons — does all of its lock-held work
        // right here. A throw used to walk out with the lock still on, and nothing could take it
        // back: the unharvested fruit keeps NeedsHarvest() true, which is precisely the state in
        // which TryYggdrasilSwap() refuses to restore. The lock stayed for the session, CanSwap()
        // stayed false, and RebirthAvailable() (BaseRebirth:157) never returned — the run could not
        // end. So the restoration is reached from both exits now, and the harvest fault survives it.
        public static void HarvestAll(bool tierOver1 = false)
        {
            try
            {
                ReadTooltipLog(false);
                var macguffinFruit = Fruits[10];
                if (tierOver1)
                {
                    if (macguffinFruit.harvestTier() > 0 && Settings.FavoredMacguffin >= 0)
                    {
                        InventoryManager.ManageFavoredMacguffin(false, true);
                        _fc.consumeFruit(10);
                    }
                    InventoryManager.RestoreMacguffins();
                    _yc.consumeAll(true);
                }
                else
                {
                    if (macguffinFruit.harvestTier() > 0 && macguffinFruit.harvestTier() >= macguffinFruit.maxTier && Settings.FavoredMacguffin >= 0)
                    {
                        InventoryManager.ManageFavoredMacguffin(false, true);
                        _fc.consumeFruit(10);
                    }
                    InventoryManager.RestoreMacguffins();
                    _yc.consumeAll();
                    if (MacguffinFruit2Ready())
                        _fc.consumeFruit(13);
                    if (QPFruitReady())
                        _fc.consumeFruit(14);
                }
                LockManager.RestoreYggdrasilSwap();
                ReadTooltipLog(true);
            }
            catch
            {
                CleanupFailedYggdrasilHarvest();
                throw;
            }
        }

        // The cleanup faults are reported and dropped, never rethrown. RestoreConfiguration transitions
        // the lock inside its own finally (R4), so by the time anything left in it can throw the lock
        // is already safe, and the harvest fault is the one worth keeping — it is why the harvest
        // failed at all. A throw at or after the normal restoration finds no Yggdrasil lock and the
        // guarded call simply no-ops, so there is no second release to get wrong.
        //
        // MacGuffins go back FIRST, matching the successful path (RestoreMacguffins before the eventual
        // Yggdrasil restore) and running while the harvest inventory context is still up, before gear
        // restoration shuffles daycare/inventory slots the MacGuffin restore may need. RestoreMacguffins
        // is self-guarding on _savedMacguffins: it no-ops unless a favored-fruit swap is actually
        // outstanding, so a harvest that never touched MacGuffins passes through it untouched. The two
        // cleanups are independently guarded — one failing must not skip the other, and neither may
        // replace the primary harvest exception that HarvestAll's catch rethrows.
        private static void CleanupFailedYggdrasilHarvest()
        {
            try
            {
                InventoryManager.RestoreMacguffins();
            }
            catch (Exception cleanupEx)
            {
                try
                {
                    LogDebug($"MacGuffin cleanup after failed harvest:\n{cleanupEx}");
                }
                catch
                {
                    // Best-effort diagnostic. Do not block Yggdrasil cleanup.
                }
            }

            try
            {
                LockManager.RestoreYggdrasilSwap();
            }
            catch (Exception cleanupEx)
            {
                try
                {
                    LogDebug($"Yggdrasil cleanup after failed harvest:\n{cleanupEx}");
                }
                catch
                {
                    // Best-effort diagnostic. The original harvest exception remains authoritative.
                }
            }
        }

        public static void ReadTooltipLog(bool doLog)
        {
            var bLog = Main.Character.tooltip.log;
            var log = bLog.GetFieldValue<TooltipLog, List<string>>("Eventlog");
            // Add something to the end of our logs to mark them as complete
            for (var i = 0; i < log.Count; i++)
            {
                if (log[i].EndsWith("<b></b>"))
                    continue;
                if (doLog)
                {
                    var sb = new StringBuilder(log[i]);
                    sb.Replace("<b>", "");
                    sb.Replace("</b>", "");
                    LogPitSpin(sb.ToString());
                }
                log[i] += "<b></b>";
            }
        }

        public static void CheckFruits()
        {
            if (!Settings.ActivateFruits)
                return;
            int curPage = _yc.curPage;
            for (var i = 0; i < Fruits.Count; i++)
            {
                var fruit = Fruits[i];
                // Skip inactive fruits
                if (fruit.maxTier == 0L)
                    continue;

                // Skip fruits that are permed
                if (fruit.permCostPaid)
                    continue;

                if (fruit.activated)
                    continue;

                if (_yc.usesEnergy[i] &&
                    _character.curEnergy >= _yc.activationCost[i])
                {
                    Log($"Removing energy for fruit {i}");
                    _character.removeMostEnergy();
                    var slot = ChangePage(i);
                    _yc.fruits[slot].activate(i);
                    continue;
                }

                if (!_yc.usesEnergy[i] &&
                    _character.magic.curMagic >= _yc.activationCost[i])
                {
                    Log($"Removing magic for fruit {i}");
                    _character.removeMostMagic();
                    var slot = ChangePage(i);
                    _yc.fruits[slot].activate(i);
                }
            }
            _yc.changePage(curPage);
        }

        private static int ChangePage(int slot)
        {
            var page = slot / 9;
            _yc.changePage(page);
            return slot - (page * 9);
        }
    }
}
