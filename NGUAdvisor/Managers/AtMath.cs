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
    // M = (energy/50) * sqrt(epow) * atSpeedBonus the level speed is dL/dt = r/(L+1) for a CONSTANT
    // r = 50*M/baseTime, which is what makes the closed forms below exact rather than a simulation.
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

        // THE answer to "how long until this slot reaches that level" — three branches, because the
        // closed form above is only half the story.
        //
        // Game truth, from updateAdvancedTraining:
        //   barProgress[id] += progressPerTick();
        //   if (barProgress[id] >= 1f) { barProgress[id] = 0f; if (canLevel()) level++; }
        // The bar is RESET, not decremented, so the overflow is DISCARDED: a slot with
        // progressPerTick >= 1 gains exactly one level per tick however much energy it holds. Feeding
        // the closed form across that region promises a time the game physically cannot deliver — which
        // is what this function exists to stop (the source spreadsheet branched here too).
        //
        // r = progressPerTick * (level+1) / tickSeconds is CONSTANT (it is 50*M/baseTime), and
        // progressPerTick falls as the level rises, so a slot below the ceiling stays below it until it
        // crosses — the two regimes are one prefix and one suffix, never interleaved.
        public static double? SecondsToTarget(double level, double target, double ppt, double tickSeconds)
        {
            if (double.IsNaN(ppt) || double.IsInfinity(ppt) || ppt <= 0) return null;
            if (double.IsNaN(level) || double.IsNaN(target)) return null;
            if (tickSeconds <= 0) return null;
            if (target <= level) return null;

            double ceiling = BbCeiling(ppt * (level + 1.0), 1.0);
            double r = ppt * (level + 1.0) / tickSeconds;

            if (target <= ceiling)                      // never leaves the one-level-per-tick region
                return tickSeconds * (target - level);
            if (level >= ceiling)                       // already past it: pure closed form
                return SecondsToLevel(level, target, r);

            double? closed = SecondsToLevel(ceiling, target, r);
            if (!closed.HasValue) return null;
            return tickSeconds * (ceiling - level) + closed.Value;
        }

        // The FORWARD twin of SecondsToTarget: where does the level land after t seconds, honouring the
        // same one-level-per-tick cap? Same three regimes, same reasoning (barProgress is RESET, so the
        // overflow is discarded and no amount of energy buys more than one level per tick).
        //
        // Equivalence property this is relied upon for: when ppt <= 1 the ceiling sits at or below the
        // current level, so the first branch returns the plain uncapped closed form — bit-for-bit the
        // arithmetic callers had before. Only blitz-boosting slots (ppt > 1) see a different number, and
        // for those the capped answer is strictly LOWER.
        public static double LevelAtCapped(double l0, double ppt, double t, double tickSeconds)
        {
            if (t <= 0) return l0;
            if (ppt <= 0 || tickSeconds <= 0) return l0;

            double ceiling = BbCeiling(ppt * (l0 + 1.0), 1.0);
            double r = ppt * (l0 + 1.0) / tickSeconds;

            if (ceiling <= l0)                    // not blitz-boosting: the cap never binds
                return LevelAt(l0, r, t);

            double tCap = (ceiling - l0) * tickSeconds;
            if (t <= tCap)                        // still inside the one-level-per-tick region
                return l0 + t / tickSeconds;
            return LevelAt(ceiling, r, t - tCap); // capped prefix, then the closed form beyond it
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
