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

        private const int ValueMode = 4;

        // Kickass (0), Faster Wishes (1) and Money Pit (4) come first in the guide (ch.6); they are the
        // one-shot unlocks the value score cannot price.
        private static readonly int[] GuideFirstWishes = { 0, 1, 4 };

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

            if (Settings.WishMode == ValueMode)
            {
                AllocateByValue(remainingEnergy, remainingMagic, remainingRes3);
                return;
            }

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

        private static void AllocateByValue(long remainingEnergy, long remainingMagic, long remainingRes3)
        {
            int slots = MaxSlots() - _wc.numAllocatedWishes();
            if (slots <= 0)
                return;

            energy = Math.Max(0L, remainingEnergy / slots + Math.Sign(remainingEnergy % slots));
            magic = Math.Max(0L, remainingMagic / slots + Math.Sign(remainingMagic % slots));
            res3 = Math.Max(0L, remainingRes3 / slots + Math.Sign(remainingRes3 % slots));
            if (energy <= 0L && magic <= 0L && res3 <= 0L)
                return;

            List<int> ordered = ValueOrder(GetValidWishes(), out Dictionary<int, double> weights);
            if (ordered.Count > slots)
                ordered = ordered.GetRange(0, slots);
            if (ordered.Count == 0)
                return;

            ordered = ordered.OrderByDescending(id => weights[id]).ToList();
            double[] fractions = HackMath.WishSplitFractions(ordered.Select(id => weights[id]).ToArray());
            double remainingFraction = 1.0;
            for (int i = 0; i < ordered.Count; i++)
            {
                double share = remainingFraction > 0.0 ? Math.Min(1.0, fractions[i] / remainingFraction) : 1.0 / (ordered.Count - i);
                remainingFraction -= fractions[i];

                energy = Math.Min(remainingEnergy, (long)Math.Ceiling(remainingEnergy * share));
                magic = Math.Min(remainingMagic, (long)Math.Ceiling(remainingMagic * share));
                res3 = Math.Min(remainingRes3, (long)Math.Ceiling(remainingRes3 * share));

                AllocateToWish(ordered[i]);
                Wish wish = Wishes[ordered[i]];
                remainingEnergy -= wish.energy;
                remainingMagic -= wish.magic;
                remainingRes3 -= wish.res3;
            }
        }

        private static bool HasValueScore(int id) =>
            HackMath.IsProductWish(id) && (!HackMath.IsCardWish(id) || _character.cards.cardsOn);

        // Value rate = relative gain of the next level x levels per tick, per wish. Wish-speed wishes are
        // priced as their gain times what they would speed up, so they need the others' sum first.
        private static Dictionary<int, double> ValueRates(List<int> ids, bool unclamped)
        {
            Dictionary<int, double> rates = new Dictionary<int, double>();
            double othersSum = 0.0;
            foreach (int id in ids)
            {
                if (!HasValueScore(id) || HackMath.IsWishSpeedWish(id))
                    continue;
                double ppt = unclamped ? BaseProgressPerTick(id) : ProgressPerTick(id, out _);
                double rate = HackMath.WishGainPerLevel(id, _wc.properties[id].effectPerLevel, Wishes[id].level) * ppt;
                rates[id] = rate;
                othersSum += rate;
            }
            foreach (int id in ids)
            {
                if (!HasValueScore(id) || !HackMath.IsWishSpeedWish(id))
                    continue;
                double ppt = unclamped ? BaseProgressPerTick(id) : ProgressPerTick(id, out _);
                double gain = HackMath.WishGainPerLevel(id, _wc.properties[id].effectPerLevel, Wishes[id].level);
                rates[id] = HackMath.WishSpeedValueRate(gain, othersSum, ppt);
            }
            return rates;
        }

        // Best first. weights holds each wish's unclamped value rate for the pool split; wishes without a
        // score take the largest weight present so the cheapest-first fallback is not starved.
        private static List<int> ValueOrder(List<int> wishIds, out Dictionary<int, double> weights)
        {
            List<int> live = wishIds.Where(id => ProgressPerTick(id, out _) > 0).ToList();
            weights = new Dictionary<int, double>();
            if (live.Count == 0)
                return live;

            bool hasPriorities = Settings.WishPriorities.Length > 0;
            if (!Settings.WeakPriorities && live.Any(id => Settings.WishPriorities.Contains(id)))
                live = live.Where(id => Settings.WishPriorities.Contains(id)).ToList();

            Dictionary<int, double> rates = ValueRates(live, false);
            Dictionary<int, double> baseRates = ValueRates(live, true);

            List<int> ordered = live
                .OrderByDescending(id => !hasPriorities && Array.IndexOf(GuideFirstWishes, id) >= 0)
                .ThenByDescending(id => rates.TryGetValue(id, out double r) && r > 0.0)
                .ThenByDescending(id => rates.TryGetValue(id, out double r) ? r : 0.0)
                .ThenBy(id => _wc.wishSpeedDivider(id))
                .ThenBy(id =>
                {
                    int i = Array.IndexOf(Settings.WishPriorities, id);
                    return i == -1 ? int.MaxValue : i;
                })
                .ThenByDescending(id => Wishes[id].progress)
                .ToList();

            double maxWeight = baseRates.Values.DefaultIfEmpty(0.0).Max();
            foreach (int id in ordered)
                weights[id] = baseRates.TryGetValue(id, out double w) && w > 0.0 ? w : maxWeight;
            return ordered;
        }

        private static List<int> GetValidWishes()
        {
            bool diffCheck(int id) => _wc.properties[id].difficultyRequirement <= _character.settings.rebirthDifficulty;
            bool levelCheck(int id) => Wishes[id].level < _wc.properties[id].maxLevel;
            bool unlockedCheck(int id) => !_wc.wishLocked(id);
            var validWishes = Enumerable.Range(0, _character.wishes.wishSize()).Where(id => diffCheck(id) && levelCheck(id) && unlockedCheck(id));
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