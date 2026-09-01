using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // KEEP/TRASH verdicts for owned equipment. KEEP = an item earns a slot in at least one gear
    // objective's optimal loadout (the same optimizer the modes use), or appears in a configured
    // static loadout, or is currently worn, or is on the community guide's "Items to Keep" list for
    // the current chapter (GuideGear). TRASH = owned equipment that wins nothing anywhere at
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
            public string GuideChapter;  // chapter label the guide horizons were evaluated at ("" = unknown)
        }

        // Most recent verdict (BoostsPanel readout reuses it instead of re-running 30+ optimizations).
        public static Verdict Last;

        private static void LogDebugSafe(Exception e) => Main.LogDebug($"InventoryAdvisor guide chapter: {e.Message}");

        // One line when the guide's contribution CHANGES. Every other decision source in this codebase
        // has a debug line (SpendDbg, ZoneDbg, TitanGoldDbg) and a new KEEP source should not be the
        // exception: without it, "did the guide hold anything" is only answerable by opening a panel.
        private static string _lastGuideDbg;

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

            // Winners of every objective, both with and without the respawn pin. No USER pins here (the
            // new int[0]): pinned items occupy slots and would crowd genuine winners out of the keep set,
            // and the pins themselves are added unconditionally below.
            foreach (var obj in GearObjectives.Objectives)
            {
                try
                {
                    var seen = new HashSet<int>();
                    foreach (var id in GearOptimizer.OptimizeIds(obj, false, new int[0]) ?? new int[0]) { keep.Add(id); seen.Add(id); }
                    foreach (var id in GearOptimizer.OptimizeIds(obj, true, new int[0]) ?? new int[0]) { keep.Add(id); seen.Add(id); }
                    foreach (var id in seen)
                        v.Usage[id] = (v.Usage.TryGetValue(id, out var n) ? n : 0) + 1;
                }
                catch { }
            }

            // User-configured static loadouts and whatever is worn right now.
            var s = Main.Settings;
            if (s != null)
            {
                // PinnedGearIds is "always wear this" — trashing a pin is the one verdict that can never
                // be right, and it must not depend on a pin happening to win an objective.
                foreach (var arr in new[] { s.TitanLoadout, s.GoldDropLoadout, s.QuestLoadout, s.YggdrasilLoadout, s.CookingLoadout, s.PinnedGearIds })
                    if (arr != null)
                        foreach (var id in arr) keep.Add(id);
            }
            foreach (var id in LoadoutManager.CurrentGearIds()) keep.Add(id);

            // Community-guide chapter for the GuideGear horizons below. ProgressionAnalyzer is the
            // canonical titan-kill chapter engine (see its class note) and it LAGS boss progress,
            // which errs toward keeping. Unknown (0) keeps every guide entry active — over-keeping is
            // recoverable, over-trashing is not.
            int guideCh = 0;
            int guideHeld = 0, guideLapsed = 0;
            try
            {
                ProgressionAnalyzer.Progression prog = ProgressionAnalyzer.Detect();
                if (prog.Known) { guideCh = prog.Chapter; v.GuideChapter = prog.Label; }
            }
            catch (Exception e) { LogDebugSafe(e); }
            if (v.GuideChapter == null) v.GuideChapter = "";

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
                // Guide hold: the guide names this item on a chapter's "Items to Keep" list and its
                // horizon has not passed. Checked AFTER the optimizer sweep (an optimizer win needs no
                // tag) and BEFORE the chain/unmaxed fallbacks, because the guide reason is the more
                // useful label. This is what protects a unique-special item the optimizer undervalues
                // TODAY but a later chapter needs — the whole reason those lists exist.
                GuideGear.Entry ge;
                bool onGuide = GuideGear.TryGet(kv.Key, out ge);
                if (onGuide && GuideGear.KeepActive(ge, guideCh))
                {
                    guideHeld++;
                    v.Keep.Add(new KeyValuePair<int, string>(kv.Key, kv.Value + "  [guide: " + ge.Reason + "]"));
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
                // A guide item past its horizon that also wins nothing is trash like anything else —
                // but say why the protection lapsed, or the call looks like it contradicts the guide.
                if (onGuide) guideLapsed++;
                v.Trash.Add(onGuide
                    ? new KeyValuePair<int, string>(kv.Key, kv.Value + "  [guide horizon passed]")
                    : kv);
            }
            string guideDbg = $"[GuideGearDbg] chapter={(v.GuideChapter.Length > 0 ? v.GuideChapter : "unknown")}"
                            + $" held={guideHeld} lapsed={guideLapsed} keep={v.Keep.Count} trash={v.Trash.Count}";
            if (guideDbg != _lastGuideDbg)
            {
                _lastGuideDbg = guideDbg;
                Main.LogDebug(guideDbg);
            }

            Last = v;
            return v;
        }

        // Advisor-driven boost priority: equipped gear FIRST (since the 2026-07-28 change the priority list
        // is the only boost source — the old "equipped is boosted anyway" pass no longer exists), then
        // unequipped KEEP items ranked by objective usage, then chain climbers.
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
            List<int> list = new List<int>();

            // Equipped first, in slot order: it is what the character is actually using right now.
            foreach (int id in LoadoutManager.CurrentGearIds())
                if (id > 0 && !list.Contains(id) && NeedsBoosts(id))
                    list.Add(id);

            HashSet<int> equipped = new HashSet<int>(LoadoutManager.CurrentGearIds());
            foreach (KeyValuePair<int, string> kv in v.Keep
                .Where(kv => !equipped.Contains(kv.Key) && NeedsBoosts(kv.Key))
                .OrderByDescending(kv => v.Usage.TryGetValue(kv.Key, out int n) ? n : 0))
            {
                if (!list.Contains(kv.Key)) list.Add(kv.Key);
            }

            for (int i = 0; i < TransformManager.Chains.Length; i++)
            {
                try
                {
                    TransformManager.State s = TransformManager.Read(i);
                    if (s.OwnedTier >= 0 && s.NextId > 0 && s.Level < 100 && !list.Contains(s.OwnedId)
                        && NeedsBoosts(s.OwnedId))
                        list.Add(s.OwnedId);
                }
                catch { }
            }
            // The user's blacklist wins over everything above. Filtered HERE and not in the three passes
            // because this is the value AdvisorApply writes into Settings.PriorityBoosts every 10 minutes:
            // an item that GetBoostSlots is going to skip must not be put back into the list the panel
            // shows, or the panel disagrees with the behavior once every advisor pass.
            return list.Where(id => !InventoryManager.BoostBlacklisted(id)).ToArray();
        }
    }
}
