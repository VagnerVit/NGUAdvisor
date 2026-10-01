using System;
using System.Collections.Generic;
using System.Linq;
using static NGUAdvisor.Main;

namespace NGUAdvisor.Managers
{
    public static class DiggerManager
    {
        private static readonly Character _character = Main.Character;
        private static readonly AllGoldDiggerController _dc = _character.allDiggers;

        private static int[] _savedDiggers;
        private static int[] _tempDiggers;
        private static int[] _curDiggers;
        private static int _cheapestDigger;

        public static LockType CurrentLock { get; set; }
        private static readonly int[] TitanDiggers = { 11, 8, 3, 0 };
        private static readonly int[] YggDiggers = { 11, 8 };

        private static List<GoldDigger> Diggers => _character.diggers.diggers;

        private static List<int> ActiveDiggers => _character.diggers.activeDiggers;

        public static void SaveDiggers() => _savedDiggers = ActiveDiggers?.OrderFrom(_curDiggers).ToArray();

        public static void RestoreDiggers()
        {
            EquipDiggers(_savedDiggers);
            RecapDiggers();
        }

        public static void SaveTempDiggers() => _tempDiggers = ActiveDiggers?.OrderFrom(_curDiggers).ToArray();

        public static void RestoreTempDiggers()
        {
            EquipDiggers(_tempDiggers);
            RecapDiggers();
        }

        public static void EquipDiggers(LockType currentLock)
        {
            switch (currentLock)
            {
                case LockType.Titan:
                    EquipDiggers(TitanDiggers, true);
                    return;
                case LockType.Yggdrasil:
                    EquipDiggers(YggDiggers, true);
                    return;
            }
        }

        public static bool EquipDiggers(int[] diggers, bool ignoreCap = false)
        {
            if (!_character.buttons.diggers.interactable)
                return false;

            if (diggers?.Length > 0 == false)
            {
                _dc.clearAllActiveDiggers();
                _curDiggers = null;
                return true;
            }

            // No gold income means no digger can run — bail BEFORE clearing, or a retry loop strips
            // the active set every pass (post-rebirth "diggers never turn on" report: the old code
            // cleared, failed to activate anything at 0 GPS, and repeated every 10s).
            if (_character.grossGoldPerSecond() <= 0.0)
                return false;

            // Only ask for what can actually run: leveled diggers, at most one per slot. A set that
            // names locked/unleveled diggers (advisor or profile) must not fail forever over them.
            int[] unlocked = diggers.Where(d => d >= 0 && d < Diggers.Count && Diggers[d].maxLevel > 0)
                                    .Distinct()
                                    .ToArray();
            int slots = _dc.maxDiggerSlots();
            int[] usable = unlocked.Take(slots).ToArray();
            // Name WHICH shortfall it is: "locked/unleveled" sends the user to EXP, "over slot count"
            // to an AP/perk slot purchase, and the old single message covered both (user-reported a
            // set of six owned diggers reading as "0/2" with one active slot).
            if (usable.Length < diggers.Length)
            {
                int lockedCount = diggers.Distinct().Count() - unlocked.Length;
                int slotCapped = unlocked.Length - usable.Length;
                string why = lockedCount > 0 && slotCapped > 0
                    ? $"{lockedCount} locked/unleveled, {slotCapped} over slot count (slots={slots})"
                    : lockedCount > 0
                        ? $"{lockedCount} locked/unleveled"
                        : $"{slotCapped} over slot count (slots={slots}) — the diggers are owned, the SLOTS are the cap";
                Main.LogDebug($"EquipDiggers: using {usable.Length}/{diggers.Length} of requested set — {why}");
            }
            if (usable.Length == 0)
                return false;

            _dc.clearAllActiveDiggers();

            var gps = 0.0;
            if (!ignoreCap)
                gps = _character.grossGoldPerSecond() * (100.0 - Settings.DiggerCap) / 100.0;

            var allEquipped = true;

            foreach (var digger in usable)
            {
                Diggers[digger].curLevel = 1;
                if (_character.goldPerSecond() - _dc.drain(digger, true) >= gps)
                    _dc.activateDigger(digger);

                allEquipped &= Diggers[digger].active;
            }

            _curDiggers = diggers.ToArray();

            UpdateCheapestDigger();

            _dc.refreshMenu();
            return allEquipped;
        }

        // The advisor's converge-in-place path, distinct from the clear-and-rebuild EquipDiggers that
        // the lock-owned swaps and restore still use. The recommendation is reconciled member by member:
        // a digger already active for the right reason keeps its slot AND its level, only obsolete
        // members are dropped, and only missing members are attempted. A member that cannot afford
        // activation this pass is simply left missing — no clear, no level reset, no churn — and retried
        // whole on a later pass. Returns true only once every requested digger is actually active; the
        // caller runs RecapDiggers (levels + gold-spending upgrades) only on that complete set.
        //
        // This does NOT touch _savedDiggers/_tempDiggers/_curDiggers/_swappedDiggers: those belong to the
        // temporary-swap/restore machinery, which is deliberately left on the old clear-and-rebuild path.
        internal static bool ReconcileAdvisorDiggers(int[] requestedDiggers, out bool membershipChanged)
        {
            membershipChanged = false;

            if (!_character.buttons.diggers.interactable)
                return false;

            // Membership must be judged at the LEVEL-1 baseline. RecapDiggers (called right after by
            // ApplyDiggers) resets every active digger to level 1 and redistributes the whole DiggerCap
            // budget by priority — so a kept member's inflated level must NOT make the set read as "full"
            // and freeze out a cheap, higher-value newcomer. The affordability gate below reads the live
            // net (goldPerSecond), which recap leaves sitting at ~the reserve, so net − anyPositiveDrain
            // was ALWAYS below the reserve and no new member could ever join once the active set first
            // saturated the budget (user-caught: the cheap Stats digger, base 1e12, permanently locked out
            // of an open slot). Resetting to level 1 here restores true headroom; recap re-levels at once.
            foreach (var id in ActiveDiggers)
                if (id >= 0 && id < Diggers.Count)
                    Diggers[id].curLevel = 1;

            // Defensive normalization with the executor's own rules (valid, leveled, distinct, capped).
            // The planner already applies these, but the boundary must stay safe for a future caller.
            var target = requestedDiggers?
                .Where(d => d >= 0 && d < Diggers.Count && Diggers[d].maxLevel > 0)
                .Distinct()
                .Take(_dc.maxDiggerSlots())
                .ToArray() ?? new int[0];

            // Drop obsolete members off a SNAPSHOT — activateDigger mutates ActiveDiggers, so the live
            // list must never be the thing being enumerated while toggling. Read the toggle result:
            // membership only changed if the digger actually went inactive.
            foreach (var id in ActiveDiggers.ToArray())
            {
                if (Array.IndexOf(target, id) < 0)
                {
                    _dc.activateDigger(id);
                    if (!Diggers[id].active)
                        membershipChanged = true;
                }
            }

            // An empty request is complete once nothing is active — done AFTER removal so obsolete
            // members are actually dropped, and never falling through to activation.
            if (target.Length == 0)
            {
                if (membershipChanged)
                    _dc.refreshMenu();
                return ActiveDiggers.Count == 0;
            }

            // Attempt only the missing members, in recommendation order, through the EXACT production
            // affordability precheck. No income means nothing can run — leave the current set untouched.
            var gross = _character.grossGoldPerSecond();
            if (gross > 0.0)
            {
                var gps = gross * (100.0 - Settings.DiggerCap) / 100.0;
                foreach (var digger in target)
                {
                    if (Diggers[digger].active)
                        continue;
                    if (ActiveDiggers.Count >= _dc.maxDiggerSlots())
                        break;

                    Diggers[digger].curLevel = 1;
                    if (_character.goldPerSecond() - _dc.drain(digger, true) >= gps)
                    {
                        _dc.activateDigger(digger);
                        // The game runs its own gross-GPS gate and can still refuse — read the result
                        // rather than assume, and never clear/rebuild on a refusal.
                        if (Diggers[digger].active)
                            membershipChanged = true;
                    }
                }
            }

            if (membershipChanged)
                _dc.refreshMenu();

            // Complete only when the live active set EXACTLY matches the target — same count AND
            // membership that ApplyDiggers' early-return checks, so a lingering extra active member
            // can never read as complete and trigger a spurious success log or a next-tick re-run.
            var activeAfter = ActiveDiggers.ToArray();
            return activeAfter.Length == target.Length && target.All(activeAfter.Contains);
        }

        // Lock/restore/quick/profile callers: EquipDiggers just wrote _curDiggers, so it IS the live
        // priority order — level against it.
        public static void RecapDiggers(bool ignoreCap = false) => RecapDiggers(_curDiggers, ignoreCap);

        // Advisor path overload. ReconcileAdvisorDiggers deliberately does NOT update _curDiggers, so the
        // parameterless overload would level the greedy budget in a STALE (last lock swap) or null order,
        // silently discarding the recommendation's ranking — Adventure-leads / Stats-on-push / DC-on-titan
        // never reached the leveler, so during a tight-budget push (Evil) the cubic Stats digger got
        // leveled last on leftover budget instead of first. ApplyDiggers passes CurrentDiggerSet() here so
        // the greedy allocation honors the priorities the selector actually computed.
        public static void RecapDiggers(int[] priorityOrder, bool ignoreCap = false)
        {
            if (!_character.buttons.diggers.interactable)
                return;

            var gps = _character.grossGoldPerSecond();
            if (gps == 0.0)
                return;

            if (!ignoreCap)
                gps *= Settings.DiggerCap / 100.0;

            var ordered = ActiveDiggers?.OrderFrom(priorityOrder).ToArray() ?? new int[0];
            LevelWithinBudget(ordered, gps);

            UpgradeCheapestDigger();
            _dc.refreshMenu();

            LogRecap(ordered, gps);
        }

        // BUY THE CHEAPEST NEXT LEVEL, over and over, until nothing else fits in the budget.
        //
        // A digger's drain is `base * growth^(level-1)` with growth around 1.5-1.75, so the last level of
        // an expensive digger costs more than DOZENS of levels further down. The old allocation gave each
        // digger, in priority order, everything it could carry before moving on — and on a live set that
        // meant the lead digger ate 98.9 % of the budget and the tail ran on the crumbs. One measured
        // example, straight out of two consecutive [DiggerDbg] lines: Adventure going L85 -> L86 cost
        // Drop Chance TWELVE levels (L78 -> L63). Buying by marginal cost instead put the same budget
        // into 260 levels where priority order bought 230 (user-caught 2026-09-20).
        //
        // WHAT THIS DELIBERATELY GIVES UP. Priority no longer decides who gets leveled — it decides who
        // gets a SLOT (CurrentDiggerSet / ReconcileAdvisorDiggers still rank membership) and it breaks
        // ties here. An expensive digger the advisor considers important now gets fewer levels than the
        // old allocation gave it. That is the trade the user asked for, and it is only defensible because
        // levels are the one currency every digger shares: the bonuses themselves (adventure stats vs.
        // drop chance vs. NGU speed) have no exchange rate, and inventing one would be a made-up constant
        // driving real decisions.
        //
        // The budget is NOT spent to the last coin: the loop stops at the first level that does not fit,
        // and that leftover is a genuine result (the cheapest remaining level costs more than what is
        // left), not a rounding slack to be squeezed out.
        private static void LevelWithinBudget(int[] ordered, double budget)
        {
            if (ordered.Length == 0)
                return;

            foreach (int d in ordered)
                Diggers[d].curLevel = 1;

            double spent = 0.0;
            foreach (int d in ordered)
                spent += _dc.drain(d, 0, true);

            // Every digger sits at level 1 and each pass adds exactly one level, so the total number of
            // levels the set can hold is the hard bound on the passes this can take.
            int maxPasses = 0;
            foreach (int d in ordered)
                maxPasses += (int)Math.Max(0, Diggers[d].maxLevel);

            for (int pass = 0; pass < maxPasses; pass++)
            {
                int cheapest = -1;
                double cheapestCost = 0.0;
                foreach (int d in ordered)
                {
                    if (Diggers[d].curLevel >= Diggers[d].maxLevel)
                        continue;
                    // Ordered walk with a STRICT comparison: the first digger at a given cost wins, so
                    // priority order is what breaks a tie.
                    double cost = _dc.drain(d, 1, true) - _dc.drain(d, 0, true);
                    if (cheapest < 0 || cost < cheapestCost)
                    {
                        cheapest = d;
                        cheapestCost = cost;
                    }
                }

                if (cheapest < 0 || spent + cheapestCost > budget)
                    break;

                Diggers[cheapest].curLevel++;
                spent += cheapestCost;
            }

            // The running total is computed from the same drain() the game bills us with, but it is a sum
            // of doubles over hundreds of passes — so the LIVE figure decides. Anything given back comes
            // off the most expensive level in the set, which is the mirror of how it was handed out.
            while (_character.grossGoldPerSecond() < _dc.totalGPSDrain())
            {
                int dearest = -1;
                double dearestCost = 0.0;
                foreach (int d in ordered)
                {
                    if (Diggers[d].curLevel <= 1)
                        continue;
                    double cost = _dc.drain(d, 0, true) - _dc.drain(d, -1, true);
                    if (dearest < 0 || cost > dearestCost)
                    {
                        dearest = d;
                        dearestCost = cost;
                    }
                }
                if (dearest < 0)
                    break;
                Diggers[dearest].curLevel--;
            }
        }

        // Post-recap diagnostic (validation aid). Dumps the set in PRIORITY order (which now decides
        // membership and ties, not who gets leveled first) plus the resulting level + drain per digger,
        // and the SPENT total — the one number that shows whether the budget was actually usable, since
        // buying by marginal cost deliberately leaves the remainder unspent when the cheapest remaining
        // level costs more than what is left.
        // Debug-channel, throttled to 60 s and logged only when the decision (src, order, levels) changes —
        // the printed gross/budget/drain magnitudes drift every pass and are not part of the key.
        private static DateTime _lastRecapDbg = DateTime.MinValue;
        private static string _lastRecapKey;

        private static void LogRecap(int[] ordered, double budget)
        {
            if ((DateTime.UtcNow - _lastRecapDbg).TotalSeconds < 60)
                return;
            _lastRecapDbg = DateTime.UtcNow;
            try
            {
                var parts = ordered
                    .Select(d => $"{d}:L{Diggers[d].curLevel}/{Diggers[d].maxLevel}(drain {_dc.drain(d, 0, true):0.##e0})")
                    .ToArray();
                // src= names WHO chose this order. AdvisorDiggers routes through
                // OptimizationAdvisor and the profile's digger List never reaches the game, so a
                // user editing that List sees nothing change and no line says why (cost a session's
                // debugging: the profile was edited, re-applied, and order= stayed put). Same
                // disclosure ZoneDbg makes about Target ITOPOD overriding SnipeZone.
                string src = Main.Settings.AdvisorDiggers
                    ? "advisor (OptimizationAdvisor — profile digger List is NOT consulted)"
                    : "profile";
                double spent = 0.0;
                long levels = 0;
                foreach (int d in ordered)
                {
                    spent += _dc.drain(d, 0, true);
                    levels += Diggers[d].curLevel;
                }
                string key = src + "|" + string.Join(" ", ordered.Select(d => $"{d}:{Diggers[d].curLevel}/{Diggers[d].maxLevel}").ToArray());
                if (key == _lastRecapKey)
                    return;
                _lastRecapKey = key;
                Main.LogDebug($"[DiggerDbg] src={src} gross={_character.grossGoldPerSecond():0.##e0} budget={budget:0.##e0} "
                            + $"spent={spent:0.##e0} levels={levels} "
                            + $"order=[{string.Join(" ", ordered.Select(d => d.ToString()).ToArray())}] -> {string.Join(", ", parts)}");
            }
            catch { }
        }

        public static void UpdateCheapestDigger()
        {
            if (!Settings.UpgradeDiggers)
                return;
            _cheapestDigger = -1;
            for (var i = 0; i < Diggers.Count; i++)
            {
                if (_cheapestDigger == -1)
                    _cheapestDigger = i;
                if (_dc.upgradeCost(i) < _dc.upgradeCost(_cheapestDigger))
                    _cheapestDigger = i;
            }
        }

        public static void UpgradeCheapestDigger()
        {
            if (!Settings.UpgradeDiggers)
                return;
            if (_cheapestDigger == -1)
                return;
            if (!_character.buttons.diggers.interactable)
                return;
            if (_dc.upgradeCost(_cheapestDigger) + Settings.MoneyPitThreshold > _character.realGold)
                return;

            Log("Upgrading Digger " + _cheapestDigger);
            _dc.upgradeMaxLevel(_cheapestDigger);

            UpdateCheapestDigger();
            UpgradeCheapestDigger();
        }
    }
}
