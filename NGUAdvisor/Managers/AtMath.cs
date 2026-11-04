using System;

namespace NGUAdvisor.Managers
{
    // THE canonical Advanced Training level math for this codebase.
    //
    // Game truth, decompiled from AdvancedTrainingController:
    //   getDivisor()      = baseTime * (level + 1)
    //   progressPerTick() = (energy/50) * sqrt(totalEnergyPower()) * totalAdvancedTrainingSpeedBonus()
    //                       / getDivisor()
    // A level lands when barProgress >= 1 and the game ticks 50/s. With
    // M = (energy/50) * sqrt(epow) * atSpeedBonus the CONTINUOUS level speed is dL/dt = r/(L+1) for a
    // constant r = 50*M/baseTime (LevelAt / SecondsToLevel). The game's bar is quantised, so the
    // projections (SecondsToTarget, LevelAtCapped) count whole ticks per level instead.
    //
    // This replaces three Apps Script functions in the community AT Calculator (atcalc, bb, bbtrue)
    // whose bodies are in no export of that sheet. They were derived here, then checked against the
    // sheet's own displayed numbers (see AtMathTests).
    //
    // NOTE: AtHourPlanner's projection now runs through LevelAtCapped below — it used to carry a private
    // uncapped copy of the closed form, which over-projected blitz-boosting slots by up to ~330% across
    // its own 2-4h window. Its 1 + 0.1*L^0.4 multiplier is still a private copy; folding that one in is
    // the remaining follow-up.
    //
    // Unity-free on purpose: linked into tests/NGUAdvisor.Tests, which builds without an NGU install.
    public static class AtMath
    {
        // L(t) = sqrt((L0+1)^2 + 2rt) - 1
        public static double LevelAt(double l0, double r, double t)
        {
            if (r <= 0 || t <= 0) return l0;
            return Math.Sqrt((l0 + 1.0) * (l0 + 1.0) + 2.0 * r * t) - 1.0;
        }

        // The inverse — this is the sheet's `atcalc`. Null when there is no answer, never a fabricated
        // zero or infinity: a rendered number reads as a prediction.
        public static double? SecondsToLevel(double l0, double l1, double r)
        {
            if (double.IsNaN(r) || double.IsInfinity(r) || r <= 0) return null;
            if (double.IsNaN(l0) || double.IsNaN(l1)) return null;
            if (l1 <= l0) return null;
            return ((l1 + 1.0) * (l1 + 1.0) - (l0 + 1.0) * (l0 + 1.0)) / (2.0 * r);
        }

        // THE answer to "how long until this slot reaches that level".
        //
        // Game truth, from updateAdvancedTraining:
        //   barProgress[id] += progressPerTick();
        //   if (barProgress[id] >= 1f) { barProgress[id] = 0f; if (canLevel()) level++; }
        // The bar is RESET, not decremented, so the overflow is DISCARDED. Within one level the
        // progress per tick is constant, so reaching level L+1 takes max(1, ceil(1/p(L))) ticks with
        // p(L) = c/(L+1), c = ppt*(level+1) (= M/baseTime, constant in L). That one formula covers
        // the one-level-per-tick region (p >= 1) and the slower one above the ceiling alike; the
        // continuous closed form (LevelAt / SecondsToLevel) is only its limit and under-quotes.
        public static double? SecondsToTarget(double level, double target, double ppt, double tickSeconds)
        {
            if (double.IsNaN(ppt) || double.IsInfinity(ppt) || ppt <= 0) return null;
            if (double.IsNaN(level) || double.IsNaN(target)) return null;
            if (tickSeconds <= 0) return null;
            if (target <= level) return null;

            double ticks = TicksToReach(level, target, ppt * (level + 1.0));
            if (double.IsNaN(ticks) || double.IsInfinity(ticks)) return null;
            return ticks * tickSeconds;
        }

        // The FORWARD twin of SecondsToTarget: where does the level land after t seconds, with the same
        // per-level tick count. The result carries the fractional progress into the next level, so
        // it round-trips with SecondsToTarget exactly at whole levels.
        public static double LevelAtCapped(double l0, double ppt, double t, double tickSeconds)
        {
            if (t <= 0) return l0;
            if (ppt <= 0 || tickSeconds <= 0) return l0;
            if (double.IsNaN(ppt) || double.IsInfinity(ppt)) return l0;

            return l0 + LevelsInTicks(l0, ppt * (l0 + 1.0), t / tickSeconds);
        }

        // Levels the game's discard-the-overflow bar gains from `level` in `ticks` ticks, whole levels
        // plus the fraction of the next one. c = progressPerTick * (level+1).
        public static double LevelsInTicks(double level, double c, double ticks)
        {
            if (c <= 0 || ticks <= 0) return 0;
            long first = (long)Math.Floor(level) + 1;       // k = L+1 of the first level gained

            double lo = 0, hi = Math.Floor(ticks);          // every level costs at least one tick
            while (lo < hi)
            {
                double mid = Math.Floor((lo + hi + 1.0) / 2.0);
                if (SumTicks(first, first + (long)mid - 1, c) <= ticks) lo = mid; else hi = mid - 1.0;
            }
            double left = ticks - SumTicks(first, first + (long)lo - 1, c);
            double nextCost = Math.Max(1.0, Math.Ceiling((first + lo) / c));
            return lo + Math.Min(left / nextCost, 1.0);
        }

        // Ticks to climb from `level` to `target`.
        public static double TicksToReach(double level, double target, double c)
        {
            if (c <= 0 || target <= level) return 0;
            long first = (long)Math.Floor(level) + 1;
            return SumTicks(first, (long)Math.Ceiling(target), c);
        }

        // Levels per tick of a bar whose per-tick progress is constant (Wandoos): 1 / ceil(1/p), 1 once
        // p >= 1.
        public static double LevelsPerTick(double progressPerTick)
        {
            if (double.IsNaN(progressPerTick) || progressPerTick <= 0) return 0;
            if (progressPerTick >= 1.0) return 1.0;
            return 1.0 / Math.Ceiling(1.0 / progressPerTick);
        }

        private const long ExactSumCap = 4096;

        // Sum over k = first..last of max(1, ceil(k/c)). Exact while the span of levels or of tick-count
        // groups stays under the cap; beyond both the tick counts exceed the cap, where ceil(x) = x + 0.5
        // on average is within 1e-3 relative.
        private static double SumTicks(long first, long last, double c)
        {
            if (last < first) return 0;
            double sum = 0;
            double onesEnd = Math.Floor(c);                       // k <= c: one tick each
            if (onesEnd >= first)
            {
                long end = onesEnd >= last ? last : (long)onesEnd;
                sum += end - first + 1;
                first = end + 1;
                if (first > last) return sum;
            }

            long span = last - first + 1;
            if (span <= ExactSumCap)
            {
                for (long k = first; k <= last; k++) sum += Math.Ceiling(k / c);
                return sum;
            }

            double nFirst = Math.Ceiling(first / c);
            double nLast = Math.Ceiling(last / c);
            if (nLast - nFirst < ExactSumCap)
            {
                for (double n = nFirst; n <= nLast; n++)
                {
                    double lo = Math.Max(first, Math.Floor((n - 1.0) * c) + 1.0);
                    double hi = Math.Min(last, Math.Floor(n * c));
                    if (hi >= lo) sum += n * (hi - lo + 1.0);
                }
                return sum;
            }

            return sum + ((double)last * (last + 1.0) - (first - 1.0) * (double)first) / (2.0 * c) + 0.5 * span;
        }

        // The AT slot's contribution to attack/defense: 1 + 0.1 * L^0.4 (slot 1 = attack, 0 = defense).
        public static double StatMultiplier(double level)
            => level <= 0 ? 1.0 : 1.0 + 0.1 * Math.Pow(level, 0.4);

        // The exact inverse of StatMultiplier: 1 + 0.1*L^0.4 = m  =>  L = ((m - 1)/0.1)^2.5.
        //
        // "Which AT level buys this much more stat" — the question a goal threshold is: the requirement's
        // multiplier ratio times the slot's current multiplier is the multiplier to invert.
        //
        // Null is the same "no answer" contract the rest of this file honours: m <= 1 means there is
        // nothing to reach (already met), and NaN/infinity means the caller had no usable number. Neither
        // may come back as a level — a rendered level reads as a target the user should feed towards.
        public static double? LevelForMultiplier(double multiplier)
        {
            if (double.IsNaN(multiplier) || double.IsInfinity(multiplier)) return null;
            if (multiplier <= 1.0) return null;
            return Math.Pow((multiplier - 1.0) / 0.1, 2.5);
        }

        // Below this the bootup factor is too small to divide out reliably (WandoosAdvisor uses the same floor).
        public const double MinWandoosBoot = 0.02;

        // The Wandoos speed a Wandoos-AT stop is solved against: fully booted, without the Wandoos digger
        // (it does not run in the marathon) and without this AT slot's own (1 + f·L). Decomp
        // Character.totalWandoos*Speed multiplies all three in; bootupSpeedFactor reaches 1.1 only once
        // booted with wandoosComplete, so the full-boot factor is passed in, not assumed to be 1.
        public static double? WandoosBaseSpeed(double liveSpeed, double atFactor, double boot, double fullBoot, double digger)
        {
            if (double.IsNaN(liveSpeed) || double.IsInfinity(liveSpeed) || liveSpeed <= 0) return null;
            if (double.IsNaN(boot) || boot < MinWandoosBoot) return null;
            if (atFactor <= 0 || fullBoot <= 0 || digger <= 0) return null;
            return liveSpeed / atFactor / boot * fullBoot / digger;
        }

        // The Wandoos-AT level at which the dump's one-level-per-tick allocation (decomp capAmount* =
        // baseTime / speed) costs `share` of the cap. Speed carries the slot as (1 + f·L), so solve for L.
        // 0 = already met at level 0.
        public static long? WandoosStopLevel(double baseTime, double cap, double baseSpeed, double levelFactor, double share)
        {
            if (baseTime <= 0 || cap <= 0 || baseSpeed <= 0 || levelFactor <= 0 || share <= 0) return null;
            double needFactor = baseTime / (share * cap * baseSpeed);
            if (double.IsNaN(needFactor) || double.IsInfinity(needFactor)) return null;
            double levels = (needFactor - 1.0) / levelFactor;
            if (levels >= long.MaxValue) return null;
            return levels <= 0 ? 0 : (long)Math.Ceiling(levels);
        }

        // Highest level still blitz-boosting: progressPerTick >= 1 <=> L <= M/baseTime - 1.
        // The community sheet's equivalent cell omits the -1 (it solves for L+1), so it reads one
        // higher. The decomp is the authority here; do not "correct" this toward the spreadsheet.
        public static double BbCeiling(double m, double baseTime)
        {
            if (baseTime <= 0 || m <= 0) return 0;
            return m / baseTime - 1.0;
        }
    }
}
