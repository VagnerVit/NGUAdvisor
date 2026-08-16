using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // Transform-chain intelligence. Chain tables extracted verbatim from the game's decompiled
    // InventoryController.checkItemTransform: an item at level >= 100 transforms into the next tier
    // when a boost processes it. Per-chain user toggles (Settings arrays, indexed by chain):
    // AUTO-CLIMB (allow the transform), KEEP MAX LVL (hold a maxed copy for its stats),
    // FILTER LOWER (write the game's loot-filter list for tiers below the highest owned).
    //
    // Freeze semantics (v2 — per COPY, not per ID; the v1 per-ID freeze left spare copies of a held
    // item unmerged forever, user-reported as 3x Sir Looty): the game's own mergeAll refuses to
    // merge any at-100 equipment (both sides must be < 100) and inventory merges never consume
    // equipped items — so merging is natively safe and freezing only needs to stop BOOSTS on at-100
    // copies (the transform trigger). Modes per held chain:
    //   Auto-climb OFF          -> HoldAll: every at-100 copy is boost-frozen; sub-100 spares merge.
    //   Keep-max ON + climb ON  -> KeepOne: ONE at-100 copy stays frozen (equipped preferred, else
    //                              lowest inventory slot); further at-100 copies boost + transform,
    //                              so the chain climbs while the kept copy keeps its stats.
    public static class TransformManager
    {
        public class Chain
        {
            public string Name;
            public int[] Tiers;
            public bool SadisticGate;   // last hop requires sadistic difficulty (id 195 -> 506)
        }

        public static readonly Chain[] Chains =
        {
            new Chain { Name = "Pendant", Tiers = new[] { 53, 76, 94, 142, 170, 229, 295, 388, 430, 504, 480 } },
            new Chain { Name = "Looty", Tiers = new[] { 67, 128, 169, 230, 296, 389, 431, 505, 485 } },
            new Chain { Name = "Chain #120", Tiers = new[] { 120, 121 } },
            new Chain { Name = "Chain #154", Tiers = new[] { 154, 159 } },
            new Chain { Name = "Chain #195", Tiers = new[] { 195, 506 }, SadisticGate = true },
        };

        public class State
        {
            public int ChainIndex = -1;
            public int OwnedTier = -1;   // index into Tiers of the highest owned item, -1 = none owned
            public int OwnedId;
            public long Level;
            public int NextId = -1;      // -1 = top of chain (or gated)
        }

        // Highest owned tier per chain, from a live scan of everything in gear slots + inventory.
        public static State Read(int chainIndex)
        {
            var s = new State { ChainIndex = chainIndex };
            var chain = Chains[chainIndex];
            var owned = OwnedLevels();
            for (int i = chain.Tiers.Length - 1; i >= 0; i--)
            {
                if (!owned.TryGetValue(chain.Tiers[i], out var level)) continue;
                s.OwnedTier = i;
                s.OwnedId = chain.Tiers[i];
                s.Level = level;
                if (i < chain.Tiers.Length - 1)
                {
                    bool gated = chain.SadisticGate && i == chain.Tiers.Length - 2
                        && Main.Character.settings.rebirthDifficulty < difficulty.sadistic;
                    s.NextId = gated ? -1 : chain.Tiers[i + 1];
                }
                break;
            }
            return s;
        }

        private static Dictionary<int, long> OwnedLevels()
        {
            var owned = new Dictionary<int, long>();
            var c = Main.Character;
            if (c == null) return owned;
            void Consider(Equipment e)
            {
                if (e == null || e.id == 0) return;
                if (!owned.TryGetValue(e.id, out var lv) || e.level > lv)
                    owned[e.id] = e.level;
            }
            var inv = c.inventory;
            Consider(inv.weapon);
            try { if (Main.InventoryController.weapon2Unlocked()) Consider(inv.weapon2); } catch { }
            Consider(inv.head); Consider(inv.chest); Consider(inv.legs); Consider(inv.boots);
            if (inv.accs != null) foreach (var a in inv.accs) Consider(a);
            if (inv.inventory != null) foreach (var e in inv.inventory) Consider(e);
            return owned;
        }

        private static bool Flag(int[] arr, int idx) => arr != null && idx < arr.Length && arr[idx] != 0;

        private enum HoldMode { HoldAll, KeepOne }

        private static HashSet<int> _chainIds;

        // Any tier of any transform chain (MergeInventory's locked-group exception).
        public static bool ChainItem(int id)
        {
            if (_chainIds == null)
            {
                var set = new HashSet<int>();
                foreach (var ch in Chains)
                    foreach (var t in ch.Tiers)
                        set.Add(t);
                _chainIds = set;
            }
            return _chainIds.Contains(id);
        }

        // Merge permission for chain items comes from the CLIMB toggle, not the boost blacklist
        // (users classically boost-blacklist chain items — that must not stop consolidation).
        // Climb ON -> merge freely (a merge reaching 100 transforms in the game's own merge path:
        // that IS the climb). Climb OFF -> no merges (a merge crossing 100 would transform and
        // violate the hold). null -> not a chain item, normal rules apply.
        public static bool? MergeAllowed(int id)
        {
            for (int i = 0; i < Chains.Length; i++)
                if (Array.IndexOf(Chains[i].Tiers, id) >= 0)
                    return Flag(Main.Settings?.TransformAutoClimb, i);
            return null;
        }

        // Held-chain ids -> mode, plus the designated kept inventory slot per KeepOne id
        // (int.MinValue = an equipped at-100 copy is the kept one). Cached briefly.
        private static Dictionary<int, HoldMode> _held = new Dictionary<int, HoldMode>();
        private static Dictionary<int, int> _keptSlot = new Dictionary<int, int>();
        private static DateTime _frozenAt = DateTime.MinValue;

        private static void RefreshHeld()
        {
            if ((DateTime.UtcNow - _frozenAt).TotalSeconds <= 5) return;
            _frozenAt = DateTime.UtcNow;

            var held = new Dictionary<int, HoldMode>();
            var kept = new Dictionary<int, int>();
            var st = Main.Settings;
            var c = Main.Character;
            if (st != null && c != null)
            {
                for (int i = 0; i < Chains.Length; i++)
                {
                    bool climb = Flag(st.TransformAutoClimb, i);
                    bool keep = Flag(st.TransformKeepMax, i);
                    if (climb && !keep) continue;   // fully free chain
                    var mode = climb ? HoldMode.KeepOne : HoldMode.HoldAll;
                    var tiers = Chains[i].Tiers;

                    // KeepOne holds the HIGHEST OWNED tier only (user-reported: an at-100 Forest
                    // Pendant sat in the inventory reading TRANSFORMABLE forever while the chain was
                    // already three tiers further along). Keeping a maxed copy is about the stats of
                    // the tier you actually wear — a maxed copy of an obsolete tier is worth nothing
                    // and freezing it strands 100 levels of merges at the bottom of the chain.
                    // HoldAll (climb OFF) still freezes every tier: there the user wants no transform.
                    int firstTier = 0, lastTier = tiers.Length - 2;   // top tier can't transform anyway
                    if (mode == HoldMode.KeepOne)
                    {
                        int owned = Read(i).OwnedTier;
                        if (owned < 0 || owned > lastTier) continue;
                        firstTier = lastTier = owned;
                    }

                    for (int t = firstTier; t <= lastTier; t++)
                    {
                        bool gated = Chains[i].SadisticGate && t == tiers.Length - 2
                            && c.settings.rebirthDifficulty < difficulty.sadistic;
                        if (gated) continue;
                        held[tiers[t]] = mode;
                    }
                }

                // Designate the kept copy for KeepOne ids: an equipped at-100 copy wins; otherwise
                // the lowest inventory slot holding one.
                try
                {
                    var inv = c.inventory;
                    bool EquippedMax(int id)
                    {
                        bool Hit(Equipment e) => e != null && e.id == id && e.level >= 100;
                        if (Hit(inv.weapon) || Hit(inv.head) || Hit(inv.chest) || Hit(inv.legs) || Hit(inv.boots)) return true;
                        try { if (Main.InventoryController.weapon2Unlocked() && Hit(inv.weapon2)) return true; } catch { }
                        if (inv.accs != null) foreach (var a in inv.accs) if (Hit(a)) return true;
                        return false;
                    }
                    foreach (var id in held.Keys.ToList())
                    {
                        if (held[id] != HoldMode.KeepOne) continue;
                        if (EquippedMax(id)) { kept[id] = int.MinValue; continue; }
                        for (int slot = 0; slot < inv.inventory.Count; slot++)
                        {
                            var e = inv.inventory[slot];
                            if (e != null && e.id == id && e.level >= 100) { kept[id] = slot; break; }
                        }
                    }
                }
                catch { }
            }
            _held = held;
            _keptSlot = kept;
        }

        // Per-COPY freeze. The boost path (InventoryManager.GetBoostSlots) and the merge path
        // (InventoryManager.MergeBlocked) both call this directly now, not through IsBlacklisted.
        // Only at-100 copies of held chains freeze; sub-100 spares merge and boost freely.
        public static bool Frozen(ih x)
        {
            try
            {
                if (x == null) return false;
                RefreshHeld();
                return FrozenCore(x.id, x.level, x.slot);
            }
            catch { return false; }
        }

        private static bool FrozenCore(int id, long level, int slot)
        {
            if (!_held.TryGetValue(id, out var mode)) return false;
            if (level < 100) return false;
            if (mode == HoldMode.HoldAll) return true;

            // KeepOne: equipped kept copy -> every at-100 INVENTORY copy may transform; an
            // inventory kept copy -> only that exact slot is protected.
            if (!_keptSlot.TryGetValue(id, out var keptSlot)) return true;   // no designation yet: hold
            bool equippedSlot = slot < 0 || slot >= 10000;
            if (keptSlot == int.MinValue) return equippedSlot;
            return !equippedSlot && slot == keptSlot;
        }

        // NOTE: there is deliberately NO Frozen(int) overload. One existed and a call site passed
        // x.id into it instead of the ih — silently disabling the per-copy freeze (audit catch).
        // Freezing is per COPY; anything that only has an id has no business asking.

        // Periodic pass (AdvisorApply): write the game's loot filter for tiers below the highest owned
        // on chains with FILTER enabled. itemFiltered is the same list the game's filter UI edits.
        private static string _lastFilterLog;

        // R11: the outer whole-Tick catch was removed so AdvisorApply's RunStep("Transforms", ...) owns
        // the bounded fault report. Nested probes keep their own narrow catches.
        public static void Tick()
        {
            var st = Main.Settings;
            var c = Main.Character;
            if (st == null || c == null) return;

            ActiveClimb(c, st);
            ApplyBoostTransform(c, st);

            var filtered = new List<int>();
            for (int i = 0; i < Chains.Length; i++)
            {
                if (!Flag(st.TransformFilter, i)) continue;
                var s = Read(i);
                if (s.OwnedTier <= 0) continue;
                for (int t = 0; t < s.OwnedTier; t++)
                {
                    int id = Chains[i].Tiers[t];
                    var fl = c.inventory.itemList.itemFiltered;
                    if (id < fl.Count && !fl[id])
                    {
                        fl[id] = true;
                        filtered.Add(id);
                    }
                }
            }
            if (filtered.Count > 0)
            {
                var key = string.Join(",", filtered.Select(x => x.ToString()).ToArray());
                if (key != _lastFilterLog)
                {
                    _lastFilterLog = key;
                    Main.Log($"Transform chains: filtered lower tiers {key} from loot");
                }
            }
        }

        // Boost auto-transform. The game rerolls every dropped BOOST into the chosen type
        // (ItemNameDesc.autoTransform, called from all four loot paths when settings.autoTransform is
        // 1..3), which is why this belongs to the advisor at all: the right type is the one whose
        // sinks still have room, and that changes as gear fills up.
        //
        // Settings.BoostTransformMode: 0 = Advisor (BoostSinks.BestType), 1..3 = the game's own
        // Power/Toughness/Special, 4 = None. The game's four toggles are hidden until the 100-level
        // challenge is fully completed (InventoryController.updateTransformToggles) -- before that the
        // setting does nothing and writing it would be invisible, so we stay out.
        private static DateTime _lastBoostTransform = DateTime.MinValue;

        // The advisor's own answer, and the ONLY one — InventoryManager.ManageBoostConversion used to
        // decide this too and the two overwrote each other every pass.
        //
        // A padlocked, unfinished boost outranks the value math: the padlock is the user saying "finish
        // this one", and drops of its type are what finish it. That rule is carried over verbatim from
        // ManageBoostConversion (ids 1-13 Power, 14-26 Toughness, 27-39 Special; a level-100 copy is done
        // and no longer counts). Everything else is BoostSinks: where a boost can still land.
        // The type drops are ACTUALLY being rerolled into: the user's forced choice when there is one,
        // the advisor's answer otherwise. Callers that ask "which boost id am I farming" want this,
        // not the raw setting.
        public static int EffectiveBoostType(Character c)
        {
            int mode = Main.Settings?.BoostTransformMode ?? 0;
            if (mode == 0) return AdvisedType(c);
            return mode == 4 ? BoostSinks.TypeNone : mode;
        }

        public static int AdvisedType(Character c)
        {
            int locked = LockedBoostType(c);
            return locked != BoostSinks.TypeNone ? locked : BoostSinks.BestType(BoostSinks.Current());
        }

        // Why the advisor picked what it picked. Throttled to once a minute: the pick is re-evaluated
        // every 5s, but the inputs move slowly and this line exists to answer "why T when my gear
        // clearly wants S", not to narrate.
        private static DateTime _lastTypeDbg = DateTime.MinValue;

        private static void LogTypeDbg(Character c, SavedSettings st, int want)
        {
            if ((DateTime.UtcNow - _lastTypeDbg).TotalSeconds < 60) return;
            _lastTypeDbg = DateTime.UtcNow;
            try
            {
                var sinks = BoostSinks.Current();
                var gear = BoostSinks.GearScores(sinks);
                var score = BoostSinks.TypeScores(sinks);
                int locked = LockedBoostType(c);
                Main.LogDebug($"[BoostTypeDbg] mode={st.BoostTransformMode} pick={BoostSinks.TypeName(want)}"
                    + $" locked={BoostSinks.TypeName(locked)}"
                    + $" gearScores P={gear[0]:0.##} T={gear[1]:0.##} S={gear[2]:0.##}"
                    + $" withCube P={score[0]:0.##} T={score[1]:0.##} S={score[2]:0.##}"
                    + $" gearHeadroom P={sinks.PowerGearHeadroom:0.##} T={sinks.ToughnessGearHeadroom:0.##} S={sinks.SpecialGearHeadroom:0.##}"
                    + $" cube usable={sinks.CubeUsable} P={sinks.CubePowerRaw:0.##}/{sinks.CubePowerSoftcap:0.##}"
                    + $" T={sinks.CubeToughnessRaw:0.##}/{sinks.CubeToughnessSoftcap:0.##}");
            }
            catch (Exception e) { Main.LogDebug($"[BoostTypeDbg] failed: {e.Message}"); }
        }

        private static int LockedBoostType(Character c)
        {
            int minId = int.MaxValue;
            var inv = c.inventory.inventory;
            for (int i = 0; i < inv.Count; i++)
            {
                var e = inv[i];
                if (e == null || e.id < 1 || e.id > 39) continue;
                if (e.removable || e.level >= 100) continue;   // not padlocked, or already finished
                if (e.id < minId) minId = e.id;
            }
            if (minId == int.MaxValue) return BoostSinks.TypeNone;
            return minId <= 13 ? BoostSinks.TypePower
                : minId <= 26 ? BoostSinks.TypeToughness
                : BoostSinks.TypeSpecial;
        }

        private static void ApplyBoostTransform(Character c, SavedSettings st)
        {
            if ((DateTime.UtcNow - _lastBoostTransform).TotalSeconds < 5) return;
            _lastBoostTransform = DateTime.UtcNow;

            if (c.challenges.levelChallenge10k.curCompletions < c.allChallenges.level100Challenge.maxCompletions)
                return;

            int want = st.BoostTransformMode == 0
                ? AdvisedType(c)
                : st.BoostTransformMode == 4 ? BoostSinks.TypeNone : st.BoostTransformMode;

            LogTypeDbg(c, st, want);

            int had = c.settings.autoTransform;
            if (had == want) return;
            c.settings.autoTransform = want;
            // Unity's overloaded == is the only check that sees a destroyed object; ?. does not.
            if (Main.InventoryController != null)
                Main.InventoryController.updateTransformToggles();
            // The PREVIOUS value is in the message on purpose: a write that keeps repeating means
            // something else is putting it back, and only "was X" says what that something wants.
            Main.Log($"Boost auto-transform: {BoostSinks.TypeName(had)} -> {BoostSinks.TypeName(want)}"
                + (st.BoostTransformMode == 0 ? " (advisor: most boost value delivered)" : ""));
        }

        // Active climb (user-reported: 3x unlocked Sir Looty at level 100 sat forever). The game only
        // transforms a chain item when a BOOST pass processes it, and the advisor never boosts
        // unlocked inventory items — so unlocked at-100 spares had no path forward. Now the manager
        // performs the transform itself, exactly the game's own pattern: checkItemTransform ->
        // deleteItem -> itemInfo.makeLoot(nextId, slot). One transform per pass; kept copies
        // (per-copy freeze) and gated hops are skipped.
        private static DateTime _lastClimb = DateTime.MinValue;

        private static void ActiveClimb(Character c, SavedSettings st)
        {
            if ((DateTime.UtcNow - _lastClimb).TotalSeconds < 10) return;
            _lastClimb = DateTime.UtcNow;
            RefreshHeld();
            var ic = Main.InventoryController;
            if (ic == null) return;

            for (int i = 0; i < Chains.Length; i++)
            {
                if (!Flag(st.TransformAutoClimb, i)) continue;
                var tiers = Chains[i].Tiers;
                for (int slot = 0; slot < c.inventory.inventory.Count; slot++)
                {
                    var e = c.inventory.inventory[slot];
                    if (e == null || e.level < 100 || e.id == 0) continue;
                    // The inventory padlock is a HARD veto, and it outranks the KeepOne slot heuristic.
                    // A transform CONSUMES the item (deleteItem + makeLoot of the next tier at level 1),
                    // and both of the game's own transform paths refuse to consume a protected copy:
                    // ItemController.consumeItem() requires `removable`, and mergeable() returns false
                    // for any at-100 copy that is not removable. This was the one path in the whole
                    // system that ignored it — and because MaxItem() biases merges toward the locked
                    // copy (Extensions.cs, locked => level + 101) so it survives, the locked copy is
                    // exactly the one that reaches 100 first. Net effect, user-reported: the padlocked
                    // maxed Ascended Forest Pendant was consumed to mint a level-1 Ascended x2 while a
                    // lower unlocked copy was "kept".
                    if (!e.removable) continue;
                    int tierIdx = Array.IndexOf(tiers, e.id);
                    if (tierIdx < 0 || tierIdx >= tiers.Length - 1) continue;
                    bool gated = Chains[i].SadisticGate && tierIdx == tiers.Length - 2
                        && c.settings.rebirthDifficulty < difficulty.sadistic;
                    if (gated) continue;
                    if (FrozenCore(e.id, e.level, slot)) continue;   // the kept copy stays

                    int next = ic.checkItemTransform(e);
                    if (next <= 0) continue;
                    int origId = e.id;
                    string from = Main.ItemNameNice(origId);
                    c.inventory.deleteItem(slot);
                    try
                    {
                        ic.itemInfo.makeLoot(next, slot);
                    }
                    catch (Exception ex)
                    {
                        // makeLoot threw after the item was already deleted — best-effort restore the
                        // original at-100 item into the same slot so the climb failure is not destructive.
                        Main.Log($"Transform climb FAILED for {from}: {ex.Message}; restoring original item");
                        try { ic.itemInfo.makeLoot(origId, slot); }
                        catch (Exception ex2) { Main.Log($"Transform climb restore ALSO failed for {from}: {ex2.Message}"); }
                        return;
                    }
                    Main.Log($"Transform climb: {from} → {Main.ItemNameNice(next)}");
                    return;   // one per pass — let inventory state settle
                }
            }
        }
    }
}
