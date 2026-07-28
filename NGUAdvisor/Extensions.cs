using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NGUAdvisor.Managers;
using UnityEngine;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    public static class EnumerableExtensions
    {
        // Orders elements in first collection by their order in second collection
        public static IEnumerable<TSource> OrderFrom<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second)
        {
            if (second == null)
                return first;
            return second.Concat(first).Intersect(first);
        }

        // Returns all maximum values in a generic sequence according to a specified key selector function
        public static IEnumerable<TSource> AllMaxBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> selector) where TKey : IComparable
        {
            var array = source.ToArray();
            if (array.Length == 0) return array;
            var max = selector(array[0]);
            var count = 1;
            for (var i = 1; i < array.Length; i++)
            {
                var item = array[i];
                var value = selector(item);
                var comp = value.CompareTo(max);
                if (comp >= 0)
                {
                    if (comp > 0)
                    {
                        count = 0;
                        max = value;
                    }
                    array[count++] = item;
                }
            }
            Array.Resize(ref array, count);
            return array;
        }

        // Returns all minimum values in a generic sequence according to a specified key selector function
        public static IEnumerable<TSource> AllMinBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> selector) where TKey : IComparable
        {
            var array = source.ToArray();
            if (array.Length == 0) return array;
            var min = selector(array[0]);
            var count = 1;
            for (var i = 1; i < array.Length; i++)
            {
                var item = array[i];
                var value = selector(item);
                var comp = value.CompareTo(min);
                if (comp <= 0)
                {
                    if (comp < 0)
                    {
                        count = 0;
                        min = value;
                    }
                    array[count++] = item;
                }
            }
            Array.Resize(ref array, count);
            return array;
        }
    }

    public static class ReflectionExtensions
    {
        private const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<(Type, string), FieldInfo> fieldCache = new Dictionary<(Type, string), FieldInfo>();
        private static readonly Dictionary<(Type, string, string), MethodInfo> methodCache = new Dictionary<(Type, string, string), MethodInfo>();

        private static FieldInfo GetFieldInfo<TObj>(string name)
        {
            var type = typeof(TObj);
            var key = (type, name);
            if (!fieldCache.TryGetValue(key, out var result))
            {
                result = type.GetField(name, flags);
                fieldCache.Add(key, result);
            }

            return result;
        }

        private static MethodInfo GetMethodInfo<TObj>(string name, Type[] paramTypes)
        {
            var type = typeof(TObj);
            // Key on parameter type names; a Type[] compares by reference, so param'd calls would never cache-hit and would leak entries
            var paramKey = paramTypes.Length == 0 ? string.Empty : string.Join(",", paramTypes.Select(t => t.FullName));
            var key = (type, name, paramKey);
            if (!methodCache.TryGetValue(key, out var result))
            {
                result = type.GetMethod(name, flags, null, paramTypes, null);
                methodCache.Add(key, result);
            }

            return result;
        }

        public static TRes GetFieldValue<TObj, TRes>(this TObj obj, string name) => (TRes)GetFieldInfo<TObj>(name).GetValue(obj);

        public static void SetFieldValue<TObj>(this TObj obj, string name, object value) => GetFieldInfo<TObj>(name).SetValue(obj, value);

        public static void CallMethod<TObj>(this TObj obj, string name, object[] parameters = null)
        {
            Type[] paramTypes = parameters?.Select(x => x.GetType()).ToArray() ?? Type.EmptyTypes;
            GetMethodInfo<TObj>(name, paramTypes).Invoke(obj, parameters);
        }

        public static TRes CallMethod<TObj, TRes>(this TObj obj, string name, object[] parameters = null)
        {
            Type[] paramTypes = parameters?.Select(x => x.GetType()).ToArray() ?? Type.EmptyTypes;
            return (TRes)GetMethodInfo<TObj>(name, paramTypes).Invoke(obj, parameters);
        }
    }

    public static class InventoryExtensions
    {
        public static ih MaxItem(this IEnumerable<ih> items)
        {
            return items.
                AllMaxBy(x => x.locked ? x.level + 101 : x.level).
                AllMinBy(x => x.equipment.GetNeededBoosts().Total()).
                First();
        }

        public static ih GetInventoryHelper(this Equipment equip, int slot)
        {
            return new ih
            {
                level = equip.level,
                equipment = equip,
                id = equip.id,
                locked = !equip.removable,
                name = Main.InventoryController.itemInfo.itemName[equip.id],
                slot = slot
            };
        }

        public static IEnumerable<ih> GetConvertedInventory(this Inventory inv)
        {
            return inv.inventory.Select((x, i) =>
            {
                var c = x.GetInventoryHelper(i);
                return c;
            }).Where(x => x.id != 0);
        }

        public static bool HasBoosts(this Inventory inv) => inv.inventory.Exists(x => x.id < 40 && x.id > 0);

        public static IEnumerable<ih> GetConvertedEquips(this Inventory inv)
        {
            var list = new List<ih>
            {
                inv.head.GetInventoryHelper(-1), inv.chest.GetInventoryHelper(-2), inv.legs.GetInventoryHelper(-3),
                inv.boots.GetInventoryHelper(-4), inv.weapon.GetInventoryHelper(-5)
            };

            if (Main.InventoryController.weapon2Unlocked())
                list.Add(inv.weapon2.GetInventoryHelper(-6));

            list.AddRange(inv.accs.Select((t, i) => t.GetInventoryHelper(i + 10000)));

            list.RemoveAll(x => x.id == 0);
            return list;
        }

        public static BoostsNeeded GetNeededBoosts(this Equipment equip)
        {
            float CalcCap(float cap, int level) => Mathf.Floor(cap * (1f + level / 100f));

            var n = new BoostsNeeded();

            if (equip.capAttack != 0.0)
                n.power += Math.Max(CalcCap(equip.capAttack, equip.level) - equip.curAttack, 0f);

            if (equip.capDefense != 0.0)
                n.toughness += Math.Max(CalcCap(equip.capDefense, equip.level) - equip.curDefense, 0f);

            if (equip.spec1Type != specType.None)
                n.special += Math.Max(CalcCap(equip.spec1Cap, equip.level) - equip.spec1Cur, 0f);

            if (equip.spec2Type != specType.None)
                n.special += Math.Max(CalcCap(equip.spec2Cap, equip.level) - equip.spec2Cur, 0f);

            if (equip.spec3Type != specType.None)
                n.special += Math.Max(CalcCap(equip.spec3Cap, equip.level) - equip.spec3Cur, 0f);

            return n;
        }
    }

    public static class AugmentsExtensions
    {
        private static readonly Character _character = Main.Character;

        public static float AugTimeLeftEnergy(this AugmentController aug, long energy)
        {
            return (float)((1.0 - aug.AugProgress()) / aug.getAugProgressPerTick(energy) / 50.0);
        }

        public static float AugTimeLeftEnergyMax(this AugmentController aug, long energy)
        {
            return (float)(1.0 / aug.getAugProgressPerTick(energy) / 50.0);
        }

        public static float AugProgress(this AugmentController aug) => _character.augments.augs[aug.id].augProgress;

        public static float UpgradeTimeLeftEnergy(this AugmentController aug, long energy)
        {
            return (float)((1.0 - aug.UpgradeProgress()) / GetUpgradeProgressPerTick(aug, energy) / 50.0);
        }

        public static float UpgradeTimeLeftEnergyMax(this AugmentController aug, long energy)
        {
            return (float)(1.0 / GetUpgradeProgressPerTick(aug, energy) / 50.0);
        }

        public static float UpgradeProgress(this AugmentController aug) => _character.augments.augs[aug.id].upgradeProgress;

        public static float GetUpgradeProgressPerTick(this AugmentController aug, long amount)
        {
            var num = (double)amount * _character.totalEnergyPower() / (_character.augments.augs[aug.id].upgradeLevel + 1L);

            if (_character.settings.rebirthDifficulty == difficulty.normal)
                num /= 50000.0 * _character.augmentsController.normalUpgradeSpeedDividers[aug.id];
            else if (_character.settings.rebirthDifficulty == difficulty.evil)
                num /= 50000.0 * _character.augmentsController.evilUpgradeSpeedDividers[aug.id];
            else if (_character.settings.rebirthDifficulty == difficulty.sadistic)
                num /= _character.augmentsController.sadisticUpgradeSpeedDividers[aug.id];

            num *= (1f + _character.inventoryController.bonuses[specType.Augs]);
            num *= _character.inventory.macguffinBonuses[12];
            num *= _character.hacksController.totalAugSpeedBonus();
            num *= _character.adventureController.itopod.totalAugSpeedBonus();
            num *= _character.cardsController.getBonus(cardBonus.augSpeed);
            num *= 1f + _character.allChallenges.noAugsChallenge.evilCompletions() * 0.05f;

            if (_character.allChallenges.noAugsChallenge.completions() >= 1)
                num *= 1.1000000238418579;

            if (_character.allChallenges.noAugsChallenge.evilCompletions() >= _character.allChallenges.noAugsChallenge.maxCompletions)
                num *= 1.25;

            if (_character.settings.rebirthDifficulty >= difficulty.sadistic)
                num /= aug.sadisticDivider();

            if (num >= 3.4028234663852886E+38)
                num = 3.4028234663852886E+38;

            if (num <= 9.9999997171806854E-10)
                num = 0.0;

            return (float)num;
        }
    }

    public static class MiscExtensions
    {
        // Function from https:// www.dotnetperls.com/pretty-date
        public static string GetPrettyDate(this DateTime d)
        {
            // 1.
            // Get time span elapsed since the date.
            var s = DateTime.Now.Subtract(d);

            // 2.
            // Get total number of days elapsed.
            var dayDiff = (int)s.TotalDays;

            // 3.
            // Get total number of seconds elapsed.
            var secDiff = (int)s.TotalSeconds;

            // 4.
            // Don't allow out of range values.
            if (dayDiff < 0 || dayDiff >= 31)
                return null;

            // 5.
            // Handle same-day times.
            if (dayDiff == 0)
            {
                // A.
                // Less than one minute ago.
                if (secDiff < 60)
                    return "just now";
                // B.
                // Less than 2 minutes ago.
                if (secDiff < 120)
                    return "1 minute ago";
                // C.
                // Less than one hour ago.
                if (secDiff < 3600)
                    return $"{Math.Floor((double)secDiff / 60)} minutes ago";
                // D.
                // Less than 2 hours ago.
                if (secDiff < 7200)
                    return "1 hour ago";
                // E.
                // Less than one day ago.
                if (secDiff < 86400)
                    return $"{Math.Floor((double)secDiff / 3600)} hours ago";
            }
            // 6.
            // Handle previous days.
            if (dayDiff == 1)
                return "yesterday";
            if (dayDiff < 7)
                return $"{dayDiff} days ago";
            if (dayDiff < 31)
                return $"{Math.Ceiling((double)dayDiff / 7)} weeks ago";
            return null;
        }
    }
}
