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

        // "Must have in every inventory" is ONE list, not one per breakpoint -- so it is a global
        // setting and every entry point honors it, including titan and gold gear resolution.
        public static IReadOnlyList<int> ActivePins()
        {
            try { return Main.Settings?.PinnedGearIds ?? new int[0]; }
            catch { return new int[0]; }
        }

        // Optimize for an objective and return the item IDs (for writing into a loadout / profile).
        // forceTopRespawn pins the single best Respawn item so the loadout always keeps some respawn.
        // pinnedIds: null = the global pins (this path EQUIPS); new int[0] = none (VALUATION -- see below).
        public static int[] OptimizeIds(GearObjectives.Objective obj, bool forceTopRespawn = false,
                                        IReadOnlyList<int> pinnedIds = null)
            => Optimize(obj, forceTopRespawn, pinnedIds).AllIds().Where(x => x > 0).Distinct().ToArray();

        // Same, for a priority chain plus pinned item ids.
        public static int[] OptimizeIds(IReadOnlyList<GearPriority> chain, IReadOnlyList<int> pinnedIds,
                                        bool forceTopRespawn = false)
            => Optimize(chain, pinnedIds, forceTopRespawn).AllIds().Where(x => x > 0).Distinct().ToArray();

        // Optimize for an objective by name (as stored in profiles/settings); null if unknown.
        public static GearObjectives.Objective FindObjective(string name)
            => GearObjectives.Objectives.FirstOrDefault(o =>
                string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

        // Resolve the gear a mode should equip: if objectiveName is set (and valid), optimize live for it
        // (route C3 3.2) so the mode's gear stays optimal; otherwise fall back to the static loadout IDs.
        // pinnedIds is null unless a caller needs to override the global pins (e.g. suppress them for a
        // real titan fight) -- null falls through to the global setting inside Optimize/OptimizeIds.
        // MUST be called on the main thread (reads live inventory). Never throws; falls back on any error.
        public static int[] ResolveModeGear(string objectiveName, bool forceRespawn, int[] fallback,
                                            IReadOnlyList<int> pinnedIds = null)
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
                        var chain = new[] { new GearPriority { Objective = obj, MaxAccessorySlots = GearChain.Unlimited } };
                        var ids = OptimizeIds(chain, pinnedIds, forceRespawn);
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
                // Pins must not override a real fight either -- the same death loop this override
                // exists for (loot/pinned gear equipped into a live titan) applies to pinned items too.
                return ResolveModeGear(obj, Main.Settings.TitanObjectiveRespawn, fallback, new int[0]);
            }
            if (string.IsNullOrEmpty(obj) && (fallback == null || fallback.Length == 0))
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

        // AdvisorApply compares CurrentScore against Optimize().Score behind a re-equip bar. A chain has
        // no single score, so BOTH sides measure priority 0's objective — otherwise the bar would be
        // comparing two different quantities. "Priority 0" must mean the same entry on both sides, so it
        // is resolved by LeadObjective, exactly as Optimize's own `steps` list does.
        public static double CurrentScore(IReadOnlyList<GearPriority> chain)
        {
            var lead = LeadObjective(chain);
            return lead == null ? 0 : CurrentScore(lead);
        }

        // The objective Result.Score is measured on: the first usable entry of the chain, matching the
        // filter Optimize applies when it builds its step list.
        private static GearObjectives.Objective LeadObjective(IReadOnlyList<GearPriority> chain)
            => chain == null
                ? null
                : chain.Take(GearChain.MaxPriorities)
                       .Where(p => p != null && p.Objective != null)
                       .Select(p => p.Objective)
                       .FirstOrDefault();

        // Per-objective scoring state. Previously these were closures over Optimize's single objective;
        // a priority chain needs one of these per priority over the SAME candidate pools.
        //
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
        //
        // NOT RE-ENTRANT. _scratch is a single buffer reused by every ScoreOf call on this instance, so
        // ScoreOf must never run while another ScoreOf-driven loop is mid-flight ON THE SAME INSTANCE.
        // One context per priority is fine (and is what the chain does); nesting two scoring loops that
        // share a context silently corrupts both scores and no test would catch it.
        private class ScoreContext
        {
            private readonly string[] _statNames;
            private readonly double[] _exponents;
            private readonly double[] _caps;
            private readonly double[] _constVals;
            private readonly Dictionary<int, double[]> _idToVec;
            private readonly double[] _scratch;
            private readonly double _offhandFactor;

            public ScoreContext(GearObjectives.Objective obj,
                                Dictionary<int, GearScorer.Item> idToItem,
                                GearScorer.Item cube, GearScorer.Item baseItem, double offhandFactor)
            {
                _statNames = obj.Stats;
                _exponents = obj.Exponents;
                _offhandFactor = offhandFactor;
                var statCount = _statNames.Length;

                // Base + the two fixed items (cube, nude base) — identical for every loadout considered.
                // Caps are read once per objective, from the same rule GetRawVals uses — the scoring
                // semantics of the two paths must stay identical (see the equivalence comment above).
                _caps = new double[statCount];
                for (var i = 0; i < statCount; i++)
                    _caps[i] = GearScorer.CapValue(_statNames[i]);

                _constVals = new double[statCount];
                for (var i = 0; i < statCount; i++)
                    _constVals[i] = GearScorer.BaseValue(_statNames[i]);
                var cubeVec = VecOf(cube);
                var baseVec = VecOf(baseItem);
                for (var i = 0; i < statCount; i++)
                    _constVals[i] += cubeVec[i] + baseVec[i];

                _idToVec = new Dictionary<int, double[]>(idToItem.Count);
                foreach (var kv in idToItem)
                    _idToVec[kv.Key] = VecOf(kv.Value);

                _scratch = new double[statCount];
            }

            private double[] VecOf(GearScorer.Item it)
            {
                var v = new double[_statNames.Length];
                if (it?.Stats == null) return v;
                for (var i = 0; i < _statNames.Length; i++)
                    if (it.Stats.TryGetValue(_statNames[i], out double d) && !double.IsNaN(d))
                        v[i] = d;
                return v;
            }

            public double ScoreOf(Result r)
            {
                var statCount = _statNames.Length;
                Array.Copy(_constVals, _scratch, statCount);

                void AddId(int id)
                {
                    if (id == 0 || !_idToVec.TryGetValue(id, out var v)) return;
                    for (var i = 0; i < statCount; i++) _scratch[i] += v[i];
                }

                // Weapons first, in list order, so the mainhand/offhand split matches GetRawVals.
                if (r.MainWeapon != 0)
                {
                    AddId(r.MainWeapon);
                    if (r.OffWeapon != 0 && _idToVec.TryGetValue(r.OffWeapon, out var off))
                        for (var i = 0; i < statCount; i++) _scratch[i] += off[i] * _offhandFactor;
                }
                else
                {
                    // No mainhand: the offhand IS the first weapon and takes its full value.
                    AddId(r.OffWeapon);
                }

                AddId(r.Head); AddId(r.Chest); AddId(r.Legs); AddId(r.Boots);
                for (var a = 0; a < r.Accessories.Count; a++) AddId(r.Accessories[a]);

                double res = 1.0;
                for (var i = 0; i < statCount; i++)
                {
                    // Clamp before the exponent, exactly as GetRawVals clamps before ScoreVals.
                    var total = _scratch[i];
                    if (total > _caps[i]) total = _caps[i];
                    var v = total / 100.0;
                    if (_exponents != null && _exponents.Length > i)
                        v = Math.Pow(v, _exponents[i]);
                    res *= v;
                }
                return res;
            }
        }

        // Single-objective optimize: a one-element unlimited chain. Kept so every existing caller is
        // unaffected by the CHAIN layer.
        //
        // Pins are a different matter and the caller must decide: pinnedIds defaults to null, i.e. the
        // global pin list, because the callers that reach this overload to EQUIP must honour it. Callers
        // that only VALUE a loadout (score ratios, keep/trash verdicts, the GearOptimizerDiagnostic
        // regression baseline) pass new int[0] -- a pin is a user constraint on what gets worn, and
        // letting it into a valuation makes the number a function of the user's pin list.
        public static Result Optimize(GearObjectives.Objective obj, bool forceTopRespawn = false,
                                      IReadOnlyList<int> pinnedIds = null)
            => Optimize(new[] { new GearPriority { Objective = obj, MaxAccessorySlots = GearChain.Unlimited } },
                        pinnedIds, forceTopRespawn);

        // Chain-aware optimize: an ordered list of objectives, each claiming at most its budget of the
        // accessory slots still free, on top of a list of pinned "always wear this" item ids.
        //
        // Ported from the reference driver -- external/gear-optimizer/src/sagas/optimize.worker.js:29:
        //     base_layout = optimizer.construct_base(state.locked, state.equip);       // pins
        //     for (idx...) base_layout = optimizer.compute_optimal(base_layout, idx);  // one priority
        // Each priority takes at most maxslots of the slots still FREE (Optimizer.js:135 count_accslots)
        // and the slots it fills are frozen for every later priority. That sequencing -- not the search
        // inside a single priority -- is what produces mixed accessory sets.
        public static Result Optimize(IReadOnlyList<GearPriority> chain, IReadOnlyList<int> pinnedIds,
                                      bool forceTopRespawn = false)
        {
            // null means "caller didn't specify" -> fall back to the global pinned-items setting.
            // Callers that need NO pins (e.g. a live titan fight) must pass an empty list, not null.
            pinnedIds = pinnedIds ?? ActivePins();

            var idToItem = new Dictionary<int, GearScorer.Item>();
            var pools = BuildPools(idToItem);
            var ic = Main.InventoryController;
            var cube = GameGearAdapter.BuildCubeItem();
            var baseItem = GameGearAdapter.BuildBaseItem();
            bool twoWeapons = ic.weapon2Unlocked();
            int accSlots = Math.Max(0, ic.accessorySpaces());

            // Read ONCE for the whole call. OffhandPercent is a 30s TTL cache over the live
            // weapon2Factor(); letting it refresh mid-chain would score different priorities -- or
            // different forceTopRespawn trials -- under different offhand factors and make their scores
            // incomparable.
            double offhandFactor = Offhand / 100.0;

            List<KeyValuePair<int, GearScorer.Item>> Pool(part p) =>
                pools.TryGetValue(p, out var l) ? l : new List<KeyValuePair<int, GearScorer.Item>>();

            var weapons = Pool(part.Weapon);
            var heads = Pool(part.Head);
            var chests = Pool(part.Chest);
            var legs = Pool(part.Legs);
            var boots = Pool(part.Boots);
            var accPool = Pool(part.Accessory);

            // Which slot a pinned id belongs to. Pools are shared by every priority and every respawn
            // trial, so this is built once with them.
            var idToPart = new Dictionary<int, part>();
            foreach (var kv in pools)
                foreach (var it in kv.Value)
                    idToPart[it.Key] = kv.Key;

            var steps = (chain ?? new GearPriority[0]).Take(GearChain.MaxPriorities)
                                                      .Where(p => p != null && p.Objective != null)
                                                      .ToList();

            var r = new Result();
            if (steps.Count == 0) return r;

            // Slots frozen by a pin for the WHOLE run. (Main slots are additionally frozen after
            // priority 0 -- that freeze is expressed by simply not running MainAscent again.)
            var pinnedMain = new HashSet<part>();
            bool mainWeaponPinned = false, offWeaponPinned = false;
            int pinnedAccCount = 0;

            // Re-pick the single best item for one slot, given everything else fixed.
            bool PickSlot(ScoreContext c0, IEnumerable<KeyValuePair<int, GearScorer.Item>> pool, Func<int> get, Action<int> set)
            {
                int start = get(); int best = start; double bs = c0.ScoreOf(r);
                foreach (var c in pool)
                {
                    set(c.Key); double s = c0.ScoreOf(r);
                    if (s > bs) { bs = s; best = c.Key; }
                }
                set(best);
                return best != start;
            }

            void MainAscent(ScoreContext c0)
            {
                for (int iter = 0; iter < 8; iter++)
                {
                    bool changed = false;
                    if (!mainWeaponPinned)
                        changed |= PickSlot(c0, weapons.Where(w => w.Key != r.OffWeapon), () => r.MainWeapon, v => r.MainWeapon = v);
                    if (twoWeapons && !offWeaponPinned)
                        changed |= PickSlot(c0, weapons.Where(w => w.Key != r.MainWeapon), () => r.OffWeapon, v => r.OffWeapon = v);
                    if (!pinnedMain.Contains(part.Head)) changed |= PickSlot(c0, heads, () => r.Head, v => r.Head = v);
                    if (!pinnedMain.Contains(part.Chest)) changed |= PickSlot(c0, chests, () => r.Chest, v => r.Chest = v);
                    if (!pinnedMain.Contains(part.Legs)) changed |= PickSlot(c0, legs, () => r.Legs, v => r.Legs = v);
                    if (!pinnedMain.Contains(part.Boots)) changed |= PickSlot(c0, boots, () => r.Boots, v => r.Boots = v);
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

            // cap      = the highest accessory count this priority may leave behind (its own budget on
            //            top of everything already frozen).
            // firstFree= the first accessory index this priority owns; everything below it is frozen by
            //            a pin or by an earlier priority and is never re-picked. Frozen accessories DO
            //            still score through the normal add path, which is what makes the greedy fill
            //            "best marginal accessory given what is already worn".
            void AccessoryOptimize(ScoreContext c0, int cap, int firstFree)
            {
                if (cap <= 0 || accPool.Count == 0) return;
                SyncAccSet();
                // Greedy fill. Each accessory id is used at most once BY DESIGN: NGU only lets one copy of a
                // given accessory be equipped at a time, even if you own duplicates. So this uniqueness guard
                // (and the id-dedup in BuildPools) enforces a real game rule — it is NOT an optimizer limitation.
                while (r.Accessories.Count < cap)
                {
                    int best = 0; double bs = c0.ScoreOf(r);
                    foreach (var c in accPool)
                    {
                        if (accSet.Contains(c.Key)) continue;   // one copy per accessory id (game rule)
                        r.Accessories.Add(c.Key); double s = c0.ScoreOf(r); r.Accessories.RemoveAt(r.Accessories.Count - 1);
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
                    for (int i = firstFree; i < r.Accessories.Count; i++)
                    {
                        int cur = r.Accessories[i]; int best = cur; double bs = c0.ScoreOf(r);
                        // Slot i is the one being re-picked, so it must not veto its own candidates:
                        // drop it from the membership set for the duration of the scan. (This mirrors the
                        // old List.Contains behaviour, where slot i already held the previous candidate
                        // rather than cur, so the guard only ever tested the OTHER slots.)
                        accSet.Remove(cur);
                        foreach (var c in accPool)
                        {
                            if (c.Key == cur || accSet.Contains(c.Key)) continue;
                            r.Accessories[i] = c.Key; double s = c0.ScoreOf(r);
                            if (s > bs) { bs = s; best = c.Key; }
                        }
                        r.Accessories[i] = best;
                        accSet.Add(best);
                        if (best != cur) improved = true;
                    }
                    if (!improved) break;
                }
            }

            void RunOptimize(ScoreContext c0, int cap, int firstFree)
            {
                // alternate until stable (slots interact only through the product objective)
                double prev = double.NegativeInfinity;
                for (int round = 0; round < 5; round++)
                {
                    MainAscent(c0);
                    AccessoryOptimize(c0, cap, firstFree);
                    double cur = c0.ScoreOf(r);
                    if (cur <= prev * (1 + 1e-12)) break;
                    prev = cur;
                }
            }

            // Pins ("always wear this"). Reference: construct_base(state.locked, state.equip).
            // Reported once per Optimize call, not once per chain run.
            var skippedPins = new HashSet<int>();
            var droppedPins = new HashSet<int>();
            var duplicatePins = new HashSet<int>();

            int IdInSlot(part slot)
            {
                switch (slot)
                {
                    case part.Head: return r.Head;
                    case part.Chest: return r.Chest;
                    case part.Legs: return r.Legs;
                    case part.Boots: return r.Boots;
                    default: return 0;
                }
            }

            void PlacePins(int extraPin)
            {
                pinnedMain.Clear();
                mainWeaponPinned = false;
                offWeaponPinned = false;
                pinnedAccCount = 0;

                void Place(int id, bool report)
                {
                    // The pin list outlives the item: an id the player no longer owns is simply not in
                    // the pools. Skip it and say so rather than throw.
                    if (id == 0 || !idToPart.TryGetValue(id, out var slot))
                    {
                        if (report) skippedPins.Add(id);
                        return;
                    }
                    if (slot == part.Accessory)
                    {
                        // One copy per id is a game rule, not an optimizer limitation (see the greedy
                        // fill); and truncating silently would read to the user as "the optimizer
                        // ignored my pin", so both causes are reported, separately.
                        if (r.Accessories.Contains(id)) { if (report) duplicatePins.Add(id); return; }
                        if (pinnedAccCount >= accSlots) { if (report) droppedPins.Add(id); return; }
                        r.Accessories.Add(id);
                        pinnedAccCount++;
                        return;
                    }
                    if (slot == part.Weapon)
                    {
                        // A weapon pinned twice must NOT land in both hands: ScoreOf would then add it at
                        // full value AND at the offhand factor, inflating every score in the run.
                        if (id == r.MainWeapon || id == r.OffWeapon) { if (report) duplicatePins.Add(id); return; }
                        if (!mainWeaponPinned) { r.MainWeapon = id; mainWeaponPinned = true; }
                        else if (twoWeapons && !offWeaponPinned) { r.OffWeapon = id; offWeaponPinned = true; }
                        else if (report) droppedPins.Add(id);
                        return;
                    }
                    if (pinnedMain.Contains(slot))
                    {
                        if (report) { if (IdInSlot(slot) == id) duplicatePins.Add(id); else droppedPins.Add(id); }
                        return;
                    }
                    switch (slot)
                    {
                        case part.Head: r.Head = id; break;
                        case part.Chest: r.Chest = id; break;
                        case part.Legs: r.Legs = id; break;
                        case part.Boots: r.Boots = id; break;
                    }
                    pinnedMain.Add(slot);
                }

                if (pinnedIds != null)
                    foreach (var id in pinnedIds) Place(id, true);
                // The forceTopRespawn candidate rides in as one more pin; it is never "the user's pin",
                // so it is not reported.
                if (extraPin != 0) Place(extraPin, false);
            }

            // One full pass of the chain from scratch, optionally with one extra pinned id.
            // Returns (and stores) priority 0's objective score.
            double RunChain(int extraPin)
            {
                r = new Result();
                PlacePins(extraPin);

                var frozenAccCount = pinnedAccCount;
                ScoreContext lead = null;

                for (var k = 0; k < steps.Count; k++)
                {
                    // ONE ScoreContext PER PRIORITY. ScoreContext is not re-entrant (see its comment):
                    // priorities must never share one, and no priority's scoring loop ever runs inside
                    // another's.
                    var ctx = new ScoreContext(steps[k].Objective, idToItem, cube, baseItem, offhandFactor);
                    if (k == 0) lead = ctx;

                    // The budget is recomputed here from the slots ACTUALLY filled so far, not from a
                    // plan drawn up before the chain ran -- Optimizer.js:268 calls count_accslots(:136)
                    // inside compute_optimal, against the current base_layout, for exactly this reason:
                    //     accslots = this.accslots - base_layout.counts['accessory'];
                    //     accslots = this.maxslots < accslots ? this.maxslots : accslots;
                    // i.e. the cap applies to the slots still FREE at this moment in the chain.
                    // A priority routinely fills fewer slots than it asked for (under a Respawn objective
                    // an accessory with no Respawn scores dead equal, so the greedy fill stops at once);
                    // charging it for slots it never took would strand them empty for the whole run.
                    var take = Math.Min(Math.Max(0, steps[k].MaxAccessorySlots),
                                        Math.Max(0, accSlots - frozenAccCount));
                    var cap = frozenAccCount + take;

                    // Priority 0 owns the main slots and runs the full alternation; every later priority
                    // is accessory-only, which is exactly "freeze the main slots after priority 0".
                    if (k == 0) RunOptimize(ctx, cap, frozenAccCount);
                    else AccessoryOptimize(ctx, cap, frozenAccCount);

                    frozenAccCount = r.Accessories.Count;
                }

                // A chain has no single score, so Result.Score is priority 0's -- the same quantity
                // CurrentScore(chain) reports, so AdvisorApply's re-equip bar compares like with like.
                r.Score = lead.ScoreOf(r);
                return r.Score;
            }

            bool HasRespawn()
            {
                bool Has(int id) => id != 0 && idToItem.TryGetValue(id, out var it)
                    && it.Stats.TryGetValue(GearObjectives.Stat.Respawn, out var rv) && rv > 0;
                if (Has(r.MainWeapon) || Has(r.OffWeapon) || Has(r.Head) || Has(r.Chest) || Has(r.Legs) || Has(r.Boots)) return true;
                foreach (var a in r.Accessories) if (Has(a)) return true;
                return false;
            }

            // Pass 1: pure merit — no forced respawn pin (user pins still apply).
            RunChain(0);

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

                        // The candidate joins the pin list and the WHOLE chain re-runs around it.
                        double s = RunChain(it.Key);
                        // Same tie-break and same epsilon as before; respawn is now equal by construction.
                        if (best == null || s > bestScore * (1 + 1e-12)) { best = r; bestScore = s; }
                    }
                }
                if (best != null) { r = best; r.Score = bestScore; }
            }

            if (skippedPins.Count > 0)
                Main.LogDebug("Gear pins not in inventory, skipped: " +
                              string.Join(", ", skippedPins.Select(id => $"{Main.ItemName(id)} ({id})").ToArray()));
            if (droppedPins.Count > 0)
                Main.LogDebug("Gear pins dropped, no free slot: " +
                              string.Join(", ", droppedPins.Select(id => $"{Main.ItemName(id)} ({id})").ToArray()));
            if (duplicatePins.Count > 0)
                Main.LogDebug("Gear pins dropped, already pinned (one copy per item): " +
                              string.Join(", ", duplicatePins.Select(id => $"{Main.ItemName(id)} ({id})").ToArray()));

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
