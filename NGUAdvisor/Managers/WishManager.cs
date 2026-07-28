using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static NGUAdvisor.Main;

namespace NGUAdvisor.Managers
{
    public static class WishManager
    {
        private static readonly Character _character = Main.Character;
        private static readonly WishesController _wc = _character.wishesController;

        private static long energy;
        private static long magic;
        private static long res3;

        private static List<Wish> Wishes => _character.wishes.wishes;

        private static int MaxSlots()
        {
            var slots = _wc.curWishSlots();
            if (slots > Settings.WishLimit)
                slots = Settings.WishLimit;
            var validWishes = GetValidWishes().Count;
            if (slots > validWishes)
                slots = validWishes;
            return slots;
        }

        private static bool Allocated(Wish wish) => wish.energy > 0 || wish.magic > 0 || wish.res3 > 0;

        public static void UpdateWishMenu()
        {
            var filteredWishes = _wc.curValidUpgradesList;
            var pods = _wc.pods;

            if (pods.Count <= 0 || filteredWishes.Count <= 0 || Wishes.Count <= 0)
                return;

            int wishToSelect = _wc.curSelectedWish;

            int firstWishOnCurrentPage = pods[0].id;
            int wishPageIndex = 0;

            if (filteredWishes.Contains(firstWishOnCurrentPage))
                wishPageIndex = filteredWishes.IndexOf(firstWishOnCurrentPage);

            int pageNumber = wishPageIndex / pods.Count;

            if (!filteredWishes.Contains(wishToSelect) && !Allocated(Wishes[wishToSelect]))
                wishToSelect = filteredWishes.FirstOrDefault(x => Allocated(Wishes[x]));

            if (wishToSelect == 0)
                wishToSelect = filteredWishes[0];

            _wc.updateMenu();

            if (pageNumber > 0)
                _wc.changePage(pageNumber);

            if (wishToSelect != _wc.curSelectedWish)
                _wc.selectNewWish(wishToSelect);
        }

        public static void Allocate(bool overCap = false)
        {
            _wc.removeAllResources();

            long remainingEnergy = overCap ? _character.idleEnergy : (long)Math.Ceiling(_character.idleEnergy * Settings.WishEnergy / 100.0);
            if (remainingEnergy > _character.idleEnergy)
                remainingEnergy = _character.idleEnergy;

            long remainingMagic = overCap ? _character.magic.idleMagic : (long)Math.Ceiling(_character.magic.idleMagic * Settings.WishMagic / 100.0);
            if (remainingMagic > _character.magic.idleMagic)
                remainingMagic = _character.magic.idleMagic;

            long remainingRes3 = overCap ? _character.res3.idleRes3 : (long)Math.Ceiling(_character.res3.idleRes3 * Settings.WishR3 / 100.0);
            if (remainingRes3 > _character.res3.idleRes3)
                remainingRes3 = _character.res3.idleRes3;

            var validWishes = GetValidWishes();
            for (var slots = MaxSlots() - _wc.numAllocatedWishes(); slots > 0; slots--)
            {
                if (validWishes.Count <= 0)
                    return;

                energy = Math.Max(0L, remainingEnergy / slots + Math.Sign(remainingEnergy % slots));
                magic = Math.Max(0L, remainingMagic / slots + Math.Sign(remainingMagic % slots));
                res3 = Math.Max(0L, remainingRes3 / slots + Math.Sign(remainingRes3 % slots));
                if (energy <= 0L && magic <= 0L && res3 <= 0L)
                    return;

                int wishId = BestWishId(validWishes);
                if (wishId < 0)
                    continue;

                validWishes.Remove(wishId);

                AllocateToWish(wishId);
                var wish = Wishes[wishId];
                remainingEnergy -= wish.energy;
                remainingMagic -= wish.magic;
                remainingRes3 -= wish.res3;
            }
        }

        private static List<int> GetValidWishes()
        {
            bool diffCheck(int id) => _wc.properties[id].difficultyRequirement <= _character.settings.rebirthDifficulty;
            bool levelCheck(int id) => Wishes[id].level < _wc.properties[id].maxLevel;
            var validWishes = Enumerable.Range(0, _character.wishes.wishSize()).Where(id => diffCheck(id) && levelCheck(id));
            validWishes = validWishes.Except(Settings.WishBlacklist);
            return validWishes.ToList();
        }

        private static int BestWishId(List<int> wishIds)
        {
            var maxima = wishIds.Where(id => ProgressPerTick(id, out _) > 0);
            if (!maxima.Any())
                return -1;
            if (!Settings.WeakPriorities && Settings.WishMode > 0)
                maxima = maxima.AllMaxBy(id => Settings.WishPriorities.Contains(id));
            switch (Settings.WishMode)
            {
                case 1: // Cheapest
                case 3 when _wc.numAllocatedWishes() == 0 && MaxSlots() > 1: // Balanced, first slot
                    maxima = maxima.AllMinBy(id => _wc.wishSpeedDivider(id));
                    break;
                case 2: // Fastest
                    maxima = maxima.AllMaxBy(id => ProgressPerTick(id, out _) / (1f - Wishes[id].progress));
                    break;
                case 3: // Balanced
                    if (_wc.numAllocatedWishes() == MaxSlots() - 1) // Last slot
                        maxima = maxima.AllMaxBy(id => BaseProgressPerTick(id) <= _wc.minimumWishTime() * 1.1f);
                    maxima = maxima.AllMaxBy(id => ProgressPerTick(id, out _)).AllMaxBy(id => _wc.wishSpeedDivider(id));
                    break;
            }
            maxima = maxima.AllMinBy(id =>
            {
                var i = Array.IndexOf(Settings.WishPriorities, id);
                return i == -1 ? int.MaxValue : i;
            });
            if (Settings.WishMode > 0)
                maxima = maxima.AllMaxBy(id => Wishes[id].progress);
            return maxima.First();
        }

        private static float BaseProgressPerTick(int id)
        {
            float energyFactor = Mathf.Pow(_character.totalEnergyPower() * energy, _wc.energyBias(id));
            float magicFactor = Mathf.Pow(_character.totalMagicPower() * magic, _wc.magicBias(id));
            float res3Factor = Mathf.Pow(_character.totalRes3Power() * res3, _wc.res3Bias(id));

            return energyFactor * magicFactor * res3Factor * _wc.totalWishSpeedBonuses() / _wc.wishSpeedDivider(id);
        }

        private static float ProgressPerTick(int id, out float ppt)
        {
            if (_wc.invalidID(id))
            {
                ppt = 0f;
                return 0f;
            }

            ppt = BaseProgressPerTick(id);
            if (ppt < 1E-8f)
                return 0f;

            if (ppt > _wc.minimumWishTime())
                return _wc.minimumWishTime();

            // 499 tick offset
            float progress = Wishes[id].progress + ppt * 499f;
            if (progress > 1f)
                progress = 1f;

            if (ppt / progress <= 2.9802322E-8f) // Math.Pow(2, -25)
                return 0f;

            return ppt;
        }

        private static void AllocateToWish(int id)
        {
            if (_wc.invalidID(id))
                return;

            var ppt = ProgressPerTick(id, out var baseppt);
            if (ppt <= 0f)
                return;

            double multi = Math.Pow((double)baseppt / ppt, 1.0 / 3.0 / _wc.energyBias(id));

            _character.input.energyMagicInput = (long)Math.Ceiling(energy * 1.000002 / multi);
            _wc.addEnergy(id);

            _character.input.energyMagicInput = (long)Math.Ceiling(magic * 1.000002 / multi);
            _wc.addMagic(id);

            _character.input.energyMagicInput = (long)Math.Ceiling(res3 * 1.000002 / multi);
            _wc.addRes3(id);
        }
    }
}