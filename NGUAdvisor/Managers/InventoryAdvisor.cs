using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // KEEP/TRASH verdicts for owned equipment. KEEP = an item earns a slot in at least one gear
    // objective's optimal loadout (the same optimizer the modes use), or appears in a configured
    // static loadout, or is currently worn. TRASH = owned equipment that wins nothing anywhere at
    // max level. Verdicts are per item ID: duplicate copies of a KEEP item are merge fodder, not
    // trash — the UI carries that caveat.
    public static class InventoryAdvisor
    {
        public class Verdict
        {
            public List<KeyValuePair<int, string>> Keep = new List<KeyValuePair<int, string>>();
            public List<KeyValuePair<int, string>> Trash = new List<KeyValuePair<int, string>>();
            // id -> how many objective-optimal loadouts include it (drives the auto boost priority).
            public Dictionary<int, int> Usage = new Dictionary<int, int>();
        }

        // Most recent verdict (BoostsPanel readout reuses it instead of re-running 30+ optimizations).
        public static Verdict Last;

        public static Verdict Compute()
        {
            var v = new Verdict();
            var c = Main.Character;
            if (c == null) return v;

            // Everything owned that occupies a gear slot.
            var owned = new Dictionary<int, string>();
            void Consider(Equipment e)
            {
                if (e == null || e.id == 0 || owned.ContainsKey(e.id)) return;
                var pt = e.type;
                if (pt != part.Head && pt != part.Chest && pt != part.Legs &&
                    pt != part.Boots && pt != part.Weapon && pt != part.Accessory) return;
                owned[e.id] = Main.ItemName(e.id);
            }
            var inv = c.inventory;
            Consider(inv.weapon);
            try { if (Main.InventoryController.weapon2Unlocked()) Consider(inv.weapon2); } catch { }
            Consider(inv.head); Consider(inv.chest); Consider(inv.legs); Consider(inv.boots);
            if (inv.accs != null) foreach (var a in inv.accs) Consider(a);
            if (inv.inventory != null) foreach (var e in inv.inventory) Consider(e);

            var keep = new HashSet<int>();

            // Winners of every objective, both with and without the respawn pin.
            foreach (var obj in GearObjectives.Objectives)
            {
                try
                {
                    var seen = new HashSet<int>();
                    foreach (var id in GearOptimizer.OptimizeIds(obj, false) ?? new int[0]) { keep.Add(id); seen.Add(id); }
                    foreach (var id in GearOptimizer.OptimizeIds(obj, true) ?? new int[0]) { keep.Add(id); seen.Add(id); }
                    foreach (var id in seen)
                        v.Usage[id] = (v.Usage.TryGetValue(id, out var n) ? n : 0) + 1;
                }
                catch { }
            }

            // User-configured static loadouts and whatever is worn right now.
            var s = Main.Settings;
            if (s != null)
            {
                foreach (var arr in new[] { s.TitanLoadout, s.GoldDropLoadout, s.QuestLoadout, s.YggdrasilLoadout, s.CookingLoadout })
                    if (arr != null)
                        foreach (var id in arr) keep.Add(id);
            }
            foreach (var id in LoadoutManager.CurrentGearIds()) keep.Add(id);

            // Never-maxed items and transform-chain tiers are excluded from TRASH (user rule):
            // an unmaxed item still owes its permanent item-list max bonus (farm it to 100 first),
            // and chain tiers are consolidation/climb fodder, never trash.
            var il = c.inventory.itemList;
            foreach (var kv in owned.OrderBy(x => x.Value))
            {
                if (keep.Contains(kv.Key))
                {
                    v.Keep.Add(kv);
                    continue;
                }
                if (TransformManager.ChainItem(kv.Key))
                {
                    v.Keep.Add(new KeyValuePair<int, string>(kv.Key, kv.Value + "  [chain]"));
                    continue;
                }
                bool unmaxed = false;
                try { unmaxed = kv.Key < il.itemMaxxed.Count && !il.itemMaxxed[kv.Key]; } catch { }
                if (unmaxed)
                {
                    v.Keep.Add(new KeyValuePair<int, string>(kv.Key, kv.Value + "  [max first]"));
                    continue;
                }
                v.Trash.Add(kv);
            }
            Last = v;
            return v;
        }

        // Advisor-driven boost priority: unequipped KEEP items ranked by objective usage, then chain
        // climbers (highest owned tier still below max). Equipped gear is boosted first by the
        // existing InventoryManager pass regardless of this list.
        // Fully-boosted items have nothing left to receive — they neither rank nor display.
        private static bool NeedsBoosts(int id)
        {
            try
            {
                var f = LoadoutManager.FindItemSlot(id);
                return f != null && f.equipment.GetNeededBoosts().Total() > 0;
            }
            catch { return true; }
        }

        public static int[] AutoBoostPriority(Verdict v)
        {
            var equipped = new HashSet<int>(LoadoutManager.CurrentGearIds());
            var list = v.Keep
                .Where(kv => !equipped.Contains(kv.Key) && NeedsBoosts(kv.Key))
                .OrderByDescending(kv => v.Usage.TryGetValue(kv.Key, out var n) ? n : 0)
                .Select(kv => kv.Key)
                .ToList();
            for (int i = 0; i < TransformManager.Chains.Length; i++)
            {
                try
                {
                    var s = TransformManager.Read(i);
                    if (s.OwnedTier >= 0 && s.NextId > 0 && s.Level < 100 && !list.Contains(s.OwnedId)
                        && !equipped.Contains(s.OwnedId) && NeedsBoosts(s.OwnedId))
                        list.Add(s.OwnedId);
                }
                catch { }
            }
            return list.ToArray();
        }
    }
}
