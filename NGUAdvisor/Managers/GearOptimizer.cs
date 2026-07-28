using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // Phase 2 of the native gear optimizer (route C3): the search. Finds the loadout maximizing an objective.
    //
    // NGU gear has NO set bonuses, so the objective is near-separable per slot; a coordinate-ascent over the
    // main slots plus greedy-fill + local-swap over accessories (the same heuristic the gear-optimizer uses
    // for accessories) reaches the optimum without the full Pareto machinery. The cube + nude base are fixed
    // and always included. Scoring uses GearScorer (validated against the website).
    public static class GearOptimizer
    {
        public class Result
        {
            public int MainWeapon, OffWeapon, Head, Chest, Legs, Boots;
            public readonly List<int> Accessories = new List<int>();
            public double Score;
            public IEnumerable<int> AllIds()
            {
                if (MainWeapon != 0) yield return MainWeapon;
                if (OffWeapon != 0) yield return OffWeapon;
                if (Head != 0) yield return Head;
                if (Chest != 0) yield return Chest;
                if (Legs != 0) yield return Legs;
                if (Boots != 0) yield return Boots;
                foreach (var a in Accessories) yield return a;
            }
        }

        // The REAL offhand contribution — the game's own InventoryController.weapon2Factor():
        // 0 while the second weapon slot is locked, else wish 28 + wish 45 progress capped at 1.
        // (Closes the last PLAN §4 gap: the hardcoded 100 over-valued the offhand.) Cached briefly —
        // scoring sweeps read this thousands of times per optimize pass.
        private static double _offhand = 100.0;
        private static DateTime _offhandAt = DateTime.MinValue;
        public static double OffhandPercent
        {
            get
            {
                if ((DateTime.UtcNow - _offhandAt).TotalSeconds > 30)
                {
                    _offhandAt = DateTime.UtcNow;
                    try { _offhand = Main.Character.inventoryController.weapon2Factor() * 100.0; }
                    catch { _offhand = 100.0; }
                }
                return _offhand;
            }
        }
        private static double Offhand => OffhandPercent;

        // Optimize for an objective and return the item IDs (for writing into a loadout / profile).
        // forceTopRespawn pins the single best Respawn item so the loadout always keeps some respawn.
        public static int[] OptimizeIds(GearObjectives.Objective obj, bool forceTopRespawn = false)
            => Optimize(obj, forceTopRespawn).AllIds().Where(x => x > 0).Distinct().ToArray();

        // Optimize for an objective by name (as stored in profiles/settings); null if unknown.
        public static GearObjectives.Objective FindObjective(string name)
            => GearObjectives.Objectives.FirstOrDefault(o =>
                string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

        // Resolve the gear a mode should equip: if objectiveName is set (and valid), optimize live for it
        // (route C3 3.2) so the mode's gear stays optimal; otherwise fall back to the static loadout IDs.
        // MUST be called on the main thread (reads live inventory). Never throws; falls back on any error.
        public static int[] ResolveModeGear(string objectiveName, bool forceRespawn, int[] fallback)
        {
            if (!string.IsNullOrEmpty(objectiveName))
            {
                var obj = FindObjective(objectiveName);
                if (obj == null)
                    Main.LogDebug($"Mode objective '{objectiveName}' not recognized; using static loadout.");
                else
                {
                    try
                    {
                        var ids = OptimizeIds(obj, forceRespawn);
                        if (ids.Length > 0)
                        {
                            Main.Log($"Mode gear optimized for '{obj.Name}'{(forceRespawn ? " (+top respawn)" : "")}: {ids.Length} items.");
                            return ids;
                        }
                    }
                    catch (Exception e) { Main.LogDebug($"Mode optimize '{objectiveName}' failed: {e.Message}; using static loadout."); }
                }
            }
            return fallback;
        }

        // Titan KILL gear. The user's TitanObjective (e.g. "Drop Chance") is a LOOT preference,
        // correct only while every targeted spawn auto-kills — on a REAL fight (spawning titan not
        // AK-able at its spawn version) it is the death loop (user-reported twice: empty loadout,
        // then drop gear on a live T6v2). Real fight -> force "Adventure" (Power + Toughness);
        // AK-trivial spawn -> honor the loot objective; nothing configured -> "Adventure".
        public static int[] ResolveTitanGear()
        {
            string obj = Main.Settings.TitanObjective;
            var fallback = Main.Settings.TitanLoadout;

            bool realFight = false;
            try
            {
                var targets = Main.Settings.TitanSwapTargets;
                for (int i = 0; i < ZoneHelpers.TitanZones.Length; i++)
                {
                    if (targets == null || i >= targets.Length || !targets[i]) continue;
                    if (!ZoneHelpers.TitanSpawningSoon(i)) continue;
                    if (!ZoneHelpers.AutokillAvailable(i)) { realFight = true; break; }
                }
            }
            catch { }

            if (realFight)
            {
                Main.Log("Titan fight is live (not AK) — kill set overrides the loot objective");
                obj = "Adventure";
            }
            else if (string.IsNullOrEmpty(obj) && (fallback == null || fallback.Length == 0))
                obj = "Adventure";
            return ResolveModeGear(obj, Main.Settings.TitanObjectiveRespawn, fallback);
        }

        // Gold gear resolution with a data-driven default: when the user configured NEITHER a gold
        // objective NOR a static gold loadout, optimize live for "Gold Drops" instead of doing nothing —
        // the optimizer knows the inventory better than a hand-picked list.
        public static int[] ResolveGoldGear()
        {
            string obj = Main.Settings.GoldObjective;
            var fallback = Main.Settings.GoldDropLoadout;
            if (string.IsNullOrEmpty(obj) && (fallback == null || fallback.Length == 0))
                obj = "Gold Drops";
            return ResolveModeGear(obj, Main.Settings.GoldObjectiveRespawn, fallback);
        }

        // Optimize and equip live. MUST be called on the main thread (equipping touches the game/UI).
        public static void OptimizeAndEquip(GearObjectives.Objective obj, bool forceTopRespawn = false)
        {
            if (obj == null) return;
            var ids = OptimizeIds(obj, forceTopRespawn);
            if (ids.Length > 0)
                LoadoutManager.ChangeGear(ids);
        }

        // Score the CURRENTLY-equipped loadout for an objective (same scoring the optimizer uses), so callers
        // can compare "how good is my gear now" vs Optimize().Score. Read-only; main thread. 0 on failure.
        public static double CurrentScore(GearObjectives.Objective obj)
        {
            try
            {
                var inv = Main.Character.inventory;
                var ic = Main.InventoryController;
                var list = new List<GearScorer.Item>(16);
                void Add(Equipment e) { if (e != null && e.id != 0) list.Add(GameGearAdapter.BuildItem(e, e.type == part.Weapon)); }
                Add(inv.weapon);
                if (ic.weapon2Unlocked()) Add(inv.weapon2);
                Add(inv.head); Add(inv.chest); Add(inv.legs); Add(inv.boots);
                if (inv.accs != null) foreach (var a in inv.accs) Add(a);
                list.Add(GameGearAdapter.BuildCubeItem());
                list.Add(GameGearAdapter.BuildBaseItem());
                return GearScorer.ScoreRaw(list, obj.Stats, obj.Exponents, Offhand);
            }
            catch (Exception e) { Main.LogDebug($"CurrentScore failed: {e.Message}"); return 0; }
        }

        public static Result Optimize(GearObjectives.Objective obj, bool forceTopRespawn = false)
        {
            var idToItem = new Dictionary<int, GearScorer.Item>();
            var pools = BuildPools(idToItem);
            var ic = Main.InventoryController;
            var cube = GameGearAdapter.BuildCubeItem();
            var baseItem = GameGearAdapter.BuildBaseItem();
            bool twoWeapons = ic.weapon2Unlocked();
            int accSlots = Math.Max(0, ic.accessorySpaces());

            List<KeyValuePair<int, GearScorer.Item>> Pool(part p) =>
                pools.TryGetValue(p, out var l) ? l : new List<KeyValuePair<int, GearScorer.Item>>();

            var weapons = Pool(part.Weapon);
            var heads = Pool(part.Head);
            var chests = Pool(part.Chest);
            var legs = Pool(part.Legs);
            var boots = Pool(part.Boots);
            var accPool = Pool(part.Accessory);

            var r = new Result();

            // "Top single Respawn": optionally pin the highest-Respawn candidate across all slots so at
            // least one equipped item always contributes Respawn; the remaining slots optimize around it.
            int pinId = 0;
            part pinPart = part.Head;
            bool Pinned(part p) => pinId != 0 && pinPart == p;

            // ---- inner-loop scorer -------------------------------------------------------------------
            // ScoreOf is THE hot path of this class: the accessory local-swap alone calls it
            // (iterations x slots x pool) times, and RunOptimize repeats the whole ascent up to 5 rounds.
            // The straightforward version built a List<Item> and a double[] per call and then did a
            // string-keyed Dictionary lookup for every (stat, item) pair — two allocations and dozens of
            // string hashes per single score.
            //
            // Instead each candidate gets a DENSE double[] of just this objective's stats, built once, and
            // a score is a walk of plain array adds into one reused buffer. This is an exact rewrite of
            // GearScorer.GetRawVals + ScoreVals, not an approximation:
            //   * a missing stat contributed 0 there (TryGetValue leaves val at 0 and the += is skipped),
            //     so a dense 0 is the same number;
            //   * NaN was skipped there, so NaN is folded to 0 at build time — also the same number;
            //   * the offhand rule was "the FIRST weapon in list order is mainhand, a later weapon is
            //     scaled by offhandPercent", reproduced literally below (including the case where
            //     MainWeapon is empty and OffWeapon is therefore the first weapon, and so NOT scaled);
            //   * cube and base carry IsWeapon=false and are pure addends, so they fold into a constant.
            string[] statNames = obj.Stats;
            int statCount = statNames.Length;
            double[] exponents = obj.Exponents;
            double offhandFactor = Offhand / 100.0;

            double[] VecOf(GearScorer.Item it)
            {
                var v = new double[statCount];
                if (it?.Stats == null) return v;
                for (int i = 0; i < statCount; i++)
                    if (it.Stats.TryGetValue(statNames[i], out double d) && !double.IsNaN(d))
                        v[i] = d;
                return v;
            }

            // Base + the two fixed items (cube, nude base) — identical for every loadout considered.
            var constVals = new double[statCount];
            for (int i = 0; i < statCount; i++)
                constVals[i] = GearScorer.BaseValue(statNames[i]);
            var cubeVec = VecOf(cube);
            var baseVec = VecOf(baseItem);
            for (int i = 0; i < statCount; i++)
                constVals[i] += cubeVec[i] + baseVec[i];

            var idToVec = new Dictionary<int, double[]>(idToItem.Count);
            foreach (var kv in idToItem)
                idToVec[kv.Key] = VecOf(kv.Value);

            var scratch = new double[statCount];

            double ScoreOf()
            {
                Array.Copy(constVals, scratch, statCount);

                void AddId(int id)
                {
                    if (id == 0 || !idToVec.TryGetValue(id, out var v)) return;
                    for (int i = 0; i < statCount; i++) scratch[i] += v[i];
                }

                // Weapons first, in list order, so the mainhand/offhand split matches GetRawVals.
                if (r.MainWeapon != 0)
                {
                    AddId(r.MainWeapon);
                    if (r.OffWeapon != 0 && idToVec.TryGetValue(r.OffWeapon, out var off))
                        for (int i = 0; i < statCount; i++) scratch[i] += off[i] * offhandFactor;
                }
                else
                {
                    // No mainhand: the offhand IS the first weapon and takes its full value.
                    AddId(r.OffWeapon);
                }

                AddId(r.Head); AddId(r.Chest); AddId(r.Legs); AddId(r.Boots);
                for (int a = 0; a < r.Accessories.Count; a++) AddId(r.Accessories[a]);

                double res = 1.0;
                for (int i = 0; i < statCount; i++)
                {
                    double v = scratch[i] / 100.0;
                    if (exponents != null && exponents.Length > i)
                        v = Math.Pow(v, exponents[i]);
                    res *= v;
                }
                return res;
            }

            // Re-pick the single best item for one slot, given everything else fixed.
            bool PickSlot(IEnumerable<KeyValuePair<int, GearScorer.Item>> pool, Func<int> get, Action<int> set)
            {
                int start = get(); int best = start; double bs = ScoreOf();
                foreach (var c in pool)
                {
                    set(c.Key); double s = ScoreOf();
                    if (s > bs) { bs = s; best = c.Key; }
                }
                set(best);
                return best != start;
            }

            void MainAscent()
            {
                for (int iter = 0; iter < 8; iter++)
                {
                    bool changed = false;
                    if (!Pinned(part.Weapon))
                        changed |= PickSlot(weapons.Where(w => w.Key != r.OffWeapon), () => r.MainWeapon, v => r.MainWeapon = v);
                    if (twoWeapons)
                        changed |= PickSlot(weapons.Where(w => w.Key != r.MainWeapon), () => r.OffWeapon, v => r.OffWeapon = v);
                    if (!Pinned(part.Head)) changed |= PickSlot(heads, () => r.Head, v => r.Head = v);
                    if (!Pinned(part.Chest)) changed |= PickSlot(chests, () => r.Chest, v => r.Chest = v);
                    if (!Pinned(part.Legs)) changed |= PickSlot(legs, () => r.Legs, v => r.Legs = v);
                    if (!Pinned(part.Boots)) changed |= PickSlot(boots, () => r.Boots, v => r.Boots = v);
                    if (!changed) break;
                }
            }

            // Membership mirror of r.Accessories. The uniqueness guard below is consulted once per
            // (candidate x slot x iteration), and List.Contains is a linear scan of every filled accessory
            // slot — with a full accessory bar that was the second-biggest cost in this loop after scoring.
            var accSet = new HashSet<int>();
            void SyncAccSet()
            {
                accSet.Clear();
                for (int k = 0; k < r.Accessories.Count; k++) accSet.Add(r.Accessories[k]);
            }

            void AccessoryOptimize()
            {
                if (accSlots <= 0 || accPool.Count == 0) return;
                // A pinned respawn accessory sits at index 0 and is never swapped out.
                int fixedCount = Pinned(part.Accessory) ? 1 : 0;
                SyncAccSet();
                // Greedy fill. Each accessory id is used at most once BY DESIGN: NGU only lets one copy of a
                // given accessory be equipped at a time, even if you own duplicates. So this uniqueness guard
                // (and the id-dedup in BuildPools) enforces a real game rule — it is NOT an optimizer limitation.
                while (r.Accessories.Count < accSlots)
                {
                    int best = 0; double bs = ScoreOf();
                    foreach (var c in accPool)
                    {
                        if (accSet.Contains(c.Key)) continue;   // one copy per accessory id (game rule)
                        r.Accessories.Add(c.Key); double s = ScoreOf(); r.Accessories.RemoveAt(r.Accessories.Count - 1);
                        if (s > bs) { bs = s; best = c.Key; }
                    }
                    if (best == 0) break; // nothing improves
                    r.Accessories.Add(best);
                    accSet.Add(best);
                }
                // local swap
                for (int iter = 0; iter < 50; iter++)
                {
                    bool improved = false;
                    for (int i = fixedCount; i < r.Accessories.Count; i++)
                    {
                        int cur = r.Accessories[i]; int best = cur; double bs = ScoreOf();
                        // Slot i is the one being re-picked, so it must not veto its own candidates:
                        // drop it from the membership set for the duration of the scan. (This mirrors the
                        // old List.Contains behaviour, where slot i already held the previous candidate
                        // rather than cur, so the guard only ever tested the OTHER slots.)
                        accSet.Remove(cur);
                        foreach (var c in accPool)
                        {
                            if (c.Key == cur || accSet.Contains(c.Key)) continue;
                            r.Accessories[i] = c.Key; double s = ScoreOf();
                            if (s > bs) { bs = s; best = c.Key; }
                        }
                        r.Accessories[i] = best;
                        accSet.Add(best);
                        if (best != cur) improved = true;
                    }
                    if (!improved) break;
                }
            }

            double RunOptimize()
            {
                // alternate until stable (slots interact only through the product objective)
                double prev = double.NegativeInfinity;
                for (int round = 0; round < 5; round++)
                {
                    MainAscent();
                    AccessoryOptimize();
                    double cur = ScoreOf();
                    if (cur <= prev * (1 + 1e-12)) break;
                    prev = cur;
                }
                return ScoreOf();
            }

            bool HasRespawn()
            {
                bool Has(int id) => id != 0 && idToItem.TryGetValue(id, out var it)
                    && it.Stats.TryGetValue(GearObjectives.Stat.Respawn, out var rv) && rv > 0;
                if (Has(r.MainWeapon) || Has(r.OffWeapon) || Has(r.Head) || Has(r.Chest) || Has(r.Legs) || Has(r.Boots)) return true;
                foreach (var a in r.Accessories) if (Has(a)) return true;
                return false;
            }

            // Pass 1: pure merit — no pin.
            r.Score = RunOptimize();

            // "Top single Respawn": only when the merit loadout carries NO respawn at all do we pin one
            // respawn item in — and we pick the candidate whose PINNED LOADOUT scores best overall
            // (tie-break: more respawn), not the one with the highest raw respawn. This prevents a
            // pure-respawn item (Stapler) being force-pinned alongside an item that already covers
            // respawn on merit (Ring of Greed), which double-equipped respawn.
            if (forceTopRespawn && !HasRespawn())
            {
                bool Eligible(part p, GearScorer.Item it, out double resp)
                    => it.Stats.TryGetValue(GearObjectives.Stat.Respawn, out resp) && resp > 0
                       && !(p == part.Accessory && accSlots <= 0);

                // PRE-FILTER to the candidates that can actually win, then optimize only those.
                //
                // The take rule below is "highest respawn wins outright; loadout score only breaks respawn
                // ties" (the user's Stapler-vs-Ring-of-Greed rule). That makes the winner, for ANY
                // enumeration order: the best-scoring candidate among those with the MAXIMUM respawn, ties
                // going to the first one seen. A candidate with less respawn can never win — yet the old
                // loop still ran a COMPLETE RunOptimize (main ascent + accessory fill + local swap, up to 5
                // rounds) for every respawn-bearing item in the inventory just to throw the result away.
                // One cheap pass over the pools finds the maximum first; usually only one or two candidates
                // reach it, so this turns N full optimizations into ~1.
                double maxResp = -1;
                foreach (var kv in pools)
                    foreach (var it in kv.Value)
                        if (Eligible(kv.Key, it.Value, out double resp) && resp > maxResp)
                            maxResp = resp;

                Result best = null;
                double bestScore = double.NegativeInfinity;
                foreach (var kv in pools)
                {
                    foreach (var it in kv.Value)
                    {
                        if (!Eligible(kv.Key, it.Value, out double resp) || resp != maxResp) continue;
                        part p = kv.Key;

                        r = new Result();
                        pinId = it.Key; pinPart = p;
                        switch (p)
                        {
                            case part.Weapon: r.MainWeapon = pinId; break;
                            case part.Head: r.Head = pinId; break;
                            case part.Chest: r.Chest = pinId; break;
                            case part.Legs: r.Legs = pinId; break;
                            case part.Boots: r.Boots = pinId; break;
                            case part.Accessory: r.Accessories.Add(pinId); break;
                        }
                        double s = RunOptimize();
                        // Same tie-break and same epsilon as before; respawn is now equal by construction.
                        if (best == null || s > bestScore * (1 + 1e-12)) { best = r; bestScore = s; }
                    }
                }
                if (best != null) { r = best; r.Score = bestScore; }
                pinId = 0;
            }

            return r;
        }

        // Build candidate pools by part from inventory + currently-equipped, deduped by item id.
        private static Dictionary<part, List<KeyValuePair<int, GearScorer.Item>>> BuildPools(Dictionary<int, GearScorer.Item> idToItem)
        {
            var inv = Main.Character.inventory;
            var ic = Main.InventoryController;
            var pools = new Dictionary<part, List<KeyValuePair<int, GearScorer.Item>>>();

            void Consider(Equipment e)
            {
                if (e == null || e.id == 0 || idToItem.ContainsKey(e.id)) return;
                var pt = e.type;
                if (pt != part.Head && pt != part.Chest && pt != part.Legs &&
                    pt != part.Boots && pt != part.Weapon && pt != part.Accessory) return;
                var item = GameGearAdapter.BuildItem(e, pt == part.Weapon);
                idToItem[e.id] = item;
                if (!pools.TryGetValue(pt, out var list))
                {
                    list = new List<KeyValuePair<int, GearScorer.Item>>();
                    pools[pt] = list;
                }
                list.Add(new KeyValuePair<int, GearScorer.Item>(e.id, item));
            }

            Consider(inv.weapon);
            if (ic.weapon2Unlocked()) Consider(inv.weapon2);
            Consider(inv.head); Consider(inv.chest); Consider(inv.legs); Consider(inv.boots);
            if (inv.accs != null) foreach (var a in inv.accs) Consider(a);
            if (inv.inventory != null) foreach (var e in inv.inventory) Consider(e);
            return pools;
        }
    }
}
