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

            // Greedy allocation in PRIORITY order (matches the game's own auto-level: each digger sized
            // against the gold actually AVAILABLE, not an even gps/count share). The old even split
            // collapsed every digger to level 1 on Evil, where per-level drains dwarf gross/count (user-
            // caught: 6-9 diggers all stuck at level 1 with 9e21 gross). Reset to the level-1 baseline, then
            // level high-priority diggers first against (gps - everyone else's current drain); each digger's
            // resulting drain <= its budget, so the running total can never exceed gps.
            var ordered = ActiveDiggers?.OrderFrom(priorityOrder).ToArray() ?? new int[0];
            foreach (var d in ordered)
                Diggers[d].curLevel = 1;
            foreach (var d in ordered)
                SetLevelMaxAffordable(d, gps - (_character.totalGPSDrain() - _dc.drain(d, 0, true)));

            UpgradeCheapestDigger();
            _dc.refreshMenu();

            LogRecap(ordered, gps);
        }

        // Post-recap diagnostic (validation aid). Dumps the greedy PRIORITY ORDER and the resulting
        // running level + drain per active digger, so the ordering can be confirmed live from inject.log.
        // Debug-channel and throttled — the advisor recaps every ~30s and this must not spam.
        private static DateTime _lastRecapDbg = DateTime.MinValue;

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
                Main.LogDebug($"[DiggerDbg] src={src} gross={_character.grossGoldPerSecond():0.##e0} budget={budget:0.##e0} "
                            + $"order=[{string.Join(" ", ordered.Select(d => d.ToString()).ToArray())}] -> {string.Join(", ", parts)}");
            }
            catch { }
        }

        private static void SetLevelMaxAffordable(int id, double cap)
        {
            if (id < 0 || id >= Diggers.Count)
                return;
            var curLevel = Diggers[id].curLevel;
            Diggers[id].curLevel = 0L;
            if (cap < _dc.drain(id, 1, true))
                Diggers[id].curLevel = curLevel;
            else
            {
                var num1 = (long)Math.Floor(Math.Log(cap / _dc.baseGPSDrain[id], _dc.gpsGrowthRate[id]) + 1L);
                if (num1 < curLevel)
                    num1 = curLevel;
                if (num1 > Diggers[id].maxLevel)
                    num1 = Diggers[id].maxLevel;
                Diggers[id].curLevel = num1;
                // Levels only — membership belongs to EquipDiggers / ReconcileAdvisorDiggers. The two
                // activateDigger arms that lived here were unreachable (RecapDiggers only ever calls this
                // for already-active diggers, whose level is >= 1, so num1 >= 1), and reachable or not they
                // toggled ActiveDiggers from inside RecapDiggers' foreach over that same live list — an
                // enumerator-invalidating InvalidOperationException waiting to happen.
                if (_character.grossGoldPerSecond() < _dc.totalGPSDrain())
                    Diggers[id].curLevel = curLevel;
            }
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
