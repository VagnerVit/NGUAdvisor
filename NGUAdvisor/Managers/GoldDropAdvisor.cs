using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // Is a gold kill worth its gear swap?
    //
    // The Time Machine's output rides on `machine.realBaseGold`, which the game keeps as the HIGHEST gold
    // drop of the current rebirth (decomp TimeMachineController.setbaseGold: assigns only when the new
    // drop EXCEEDS it, and only past boss 29). So a snipe that cannot beat that number buys nothing at
    // all, while the swap itself costs Power/Toughness for as long as the gold set is held — which is how
    // a run ends up flipping to gold gear and re-fighting its best zone forever after a titan already
    // banked a drop no zone enemy can match.
    //
    // Drop math (decomp LootDrop.goldDrop): drop = baseGold * Random.Range(4f, 5f) * totalGoldbonus().
    // The per-zone `baseGold` constants live in GoldDropTables (game-independent, unit-tested).
    public static class GoldDropAdvisor
    {
        // The BEST roll of Random.Range(4f, 5f), deliberately. The first version planned on the worst roll
        // (4.0) and that was a methodological error: the number being compared against — realBaseGold — is
        // a REALIZED drop, so it already contains someone's 4–5 roll. Predicting at 4.0 and comparing to a
        // bank realized at ~4.5 makes an identical kill look 11 % worse than itself, so a snipe could never
        // beat its own previous drop (observed: T3 predicted 14.5M against a 22.5M bank it had produced
        // itself). The costs are asymmetric too — over-predicting wastes one gear swap, under-predicting
        // forfeits the run's gold production — so the optimistic end is the correct side to err on.
        private const double RollMax = 5.0;

        // Kept for the ZONE snipe only (see BeatsBank callers): re-arming a snipe means fighting a zone in
        // loot gear for a while, so a tiny predicted gain is not worth it. Titan gold does NOT use this —
        // an auto-kill happens either way, so its gear swap is free.
        public const double RebankMargin = 1.25;

        private static double _gearFactor = 1.0;
        private static DateTime _gearFactorAt = DateTime.MinValue;

        // What swapping to the gold set does to the drop. `totalGoldbonus()` multiplies exactly one
        // gear-dependent factor — (1 + bonuses[GoldDropAmount] + bonuses[GoldDrop2] + cubeGoldBonus())
        // (decomp Character.totalGoldbonus) — and a single-stat "Gold Drops" score IS that factor
        // (rawTotal/100, cube included), so the ratio of the optimizer's best score to the worn one is
        // the multiplier the swap will apply. Cached: Optimize walks the whole inventory.
        public static double GoldGearFactor()
        {
            if ((DateTime.UtcNow - _gearFactorAt).TotalSeconds < 120)
                return _gearFactor;
            _gearFactorAt = DateTime.UtcNow;

            try
            {
                GearObjectives.Objective objective = GearOptimizer.FindObjective("Gold Drops");
                if (objective == null)
                    return _gearFactor;
                double worn = GearOptimizer.CurrentScore(objective);
                if (worn <= 0)
                    return _gearFactor;
                double best = GearOptimizer.Optimize(objective).Score;
                _gearFactor = best > worn ? best / worn : 1.0;
            }
            catch (Exception e)
            {
                // Optimistic on failure: 1.0 UNDER-states the drop, which can only leave a snipe armed.
                _gearFactor = 1.0;
                Main.LogDebug($"Gold gear factor: {e.Message}");
            }
            return _gearFactor;
        }

        // The number to beat: the highest drop of this rebirth, which is what the TM converts.
        public static double Banked()
        {
            try { return Main.Character.machine.realBaseGold; }
            catch { return 0; }
        }

        public static double PredictedDrop(int zone, bool bossOnly)
        {
            double baseGold = GoldDropTables.BaseGold(zone, bossOnly);
            if (baseGold <= 0)
                return 0;
            try { return baseGold * RollMax * Main.Character.totalGoldbonus() * GoldGearFactor(); }
            catch { return 0; }
        }

        // Would a gold kill in this zone raise the Time Machine at all? `margin` is the improvement
        // demanded (1.0 = any gain). An unknown zone returns true — no data must never block a snipe.
        public static bool BeatsBank(int zone, bool bossOnly, double margin, out double predicted, out double banked)
        {
            predicted = PredictedDrop(zone, bossOnly);
            banked = Banked();
            if (predicted <= 0)
                return true;
            return predicted > banked * margin;
        }

        public static bool ZoneSnipeBeatsBank(int zone, out double predicted, out double banked)
            => BeatsBank(zone, true, 1.0, out predicted, out banked);

        // Is this titan worth wearing the gold set for? The bank does NOT enter the answer, and that is the
        // whole point (user-reported: an auto-killed titan went down in loot gear because its predicted
        // drop sat under the bank). The kill happens with or without us; the gold set only changes what it
        // drops, and LockManager re-tests the autokill right after the swap, so a swap that would cost the
        // kill is undone. With no fight to lose there is nothing to weigh against — the only titans worth
        // skipping are the ones that drop no gold at all (BEAST, THE TRAITOR).
        //
        // `predicted` is still reported so the log can say what the swap is expected to be worth.
        public static bool TitanKillWorthGoldGear(int titanIndex, out double predicted, out double banked)
        {
            predicted = 0;
            banked = Banked();
            if (titanIndex < 0 || titanIndex >= ZoneHelpers.TitanZones.Length)
                return false;
            if (GoldSwapDenied(titanIndex))
                return false;
            int zone = ZoneHelpers.TitanZones[titanIndex];
            if (GoldDropTables.BaseGold(zone, true) <= 0)
                return false;
            predicted = PredictedDrop(zone, true);
            return true;
        }

        // Titans whose gold swap turned out to cost the autokill (checked against live stats right after
        // the swap). Retried after the cooldown because stats grow, but never in a tight loop: a titan
        // that needs its kill set is a REAL fight in loot gear otherwise — the death loop
        // ResolveTitanGear guards against on the titan side.
        private const double DenyMinutes = 30.0;
        private static readonly Dictionary<int, DateTime> _goldSwapDenied = new Dictionary<int, DateTime>();

        public static void DenyGoldSwap(int titanIndex)
        {
            _goldSwapDenied[titanIndex] = DateTime.UtcNow;
        }

        public static bool GoldSwapDenied(int titanIndex)
        {
            DateTime at;
            if (!_goldSwapDenied.TryGetValue(titanIndex, out at))
                return false;
            if ((DateTime.UtcNow - at).TotalMinutes < DenyMinutes)
                return true;
            _goldSwapDenied.Remove(titanIndex);
            return false;
        }

        // Why the zone snipe is latched, for the Gold pipeline's snipe stage: a snipe closed by an actual
        // kill and one skipped for lack of payoff both read as GoldSnipeComplete, and "COMPLETE" for the
        // second is a lie. Numbers only — the panel formats them; it must NOT re-predict, because
        // PredictedDrop reaches the optimizer and the panel runs on the WinForms thread.
        public static bool SnipeSkipped { get; private set; }
        public static double SnipeSkipPredicted { get; private set; }
        public static double SnipeSkipBanked { get; private set; }

        public static void NoteSnipeSkipped(double predicted, double banked)
        {
            SnipeSkipped = true;
            SnipeSkipPredicted = predicted;
            SnipeSkipBanked = banked;
        }

        // Called from the GoldSnipeComplete setter on every re-arm: whatever the reason, the skip note is
        // stale the moment the snipe is armed again.
        public static void ClearSnipeSkip()
        {
            SnipeSkipped = false;
        }

        // Rebirth wipes realBaseGold and re-grows the stats every deny was measured against.
        public static void ResetRun()
        {
            _goldSwapDenied.Clear();
            _gearFactorAt = DateTime.MinValue;
            ClearSnipeSkip();
        }
    }
}
