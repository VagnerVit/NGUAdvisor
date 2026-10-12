using System;

namespace NGUAdvisor.Managers
{
    // What an hour of an NGU lane is worth, as the game computes it. Bonus curves are decomp
    // AllNGUController.<x>BonusNormal/Evil/Sadistic: linear up to a softcap, then a power of the level.
    // Respawn has its own capped curve (NGUAdvisors.RespawnRatio); Number's timeMulti factor is left
    // out, which leaves a ratio between two levels at the same run time unchanged only approximately.
    public static class NguGain
    {
        public const double TicksPerHour = 50.0 * 3600.0;

        private struct Curve
        {
            public readonly double Softcap, Exponent, Coefficient;
            public Curve(double softcap, double exponent, double coefficient)
            {
                Softcap = softcap;
                Exponent = exponent;
                Coefficient = coefficient;
            }
        }

        private static readonly Curve Linear = new Curve(double.MaxValue, 1.0, 1.0);

        // [id][track: normal, evil, sadistic]
        private static readonly Curve[][] EnergyCurves =
        {
            new[] { Linear, Linear, Linear },                                                                 // Augments
            new[] { Linear, new Curve(1000, 0.25, 177.9), new Curve(1000, 0.15, 354.81) },                     // Wandoos
            new[] { Linear, Linear, Linear },                                                                 // Respawn (own curve)
            new[] { Linear, Linear, new Curve(1000, 0.5, 31.63) },                                             // Gold
            new[] { new Curve(1000, 0.5, 31.7), new Curve(1000, 0.25, 177.9), new Curve(1000, 0.2, 251.19) },  // Adventure α
            new[] { Linear, Linear, Linear },                                                                 // Power α
            new[] { new Curve(1000, 0.5, 31.7), new Curve(1000, 0.3, 125.9), new Curve(1000, 0.2, 251.2) },    // Drop Chance
            new[] { new Curve(1000, 0.3, 125.9), new Curve(1000, 0.3, 125.9), new Curve(1000, 0.1, 501.19) },  // Magic NGU
            new[] { new Curve(1000, 0.3, 125.9), new Curve(1000, 0.2, 251.2), new Curve(1000, 0.1, 501.21) },  // PP
        };

        private static readonly Curve[][] MagicCurves =
        {
            new[] { new Curve(400, 0.33, 55.4), new Curve(400, 0.1, 219.72), new Curve(400, 0.08, 247.69) },     // Yggdrasil
            new[] { new Curve(2000, 0.4, 95.66), new Curve(2000, 0.2, 437.35), new Curve(2000, 0.15, 639.56) },  // EXP
            new[] { Linear, Linear, Linear },                                                                   // Power β
            new[] { new Curve(1000, 0.5, 31.7), new Curve(1000, 0.3, 125.9), new Curve(1000, 0.2, 251.2) },      // Number
            new[] { new Curve(1000, 0.8, 3.981), new Curve(1000, 0.8, 3.981), new Curve(1000, 0.8, 3.981) },     // Time Machine
            new[] { new Curve(1000, 0.3, 125.9), new Curve(1000, 0.2, 251.2), new Curve(1000, 0.15, 354.82) },   // Energy NGU
            new[] { new Curve(1000, 0.4, 63.13), new Curve(1000, 0.25, 177.83), new Curve(1000, 0.12, 436.53) }, // Adventure β
        };

        // track: 0 normal, 1 evil, 2 sadistic. factor: the game's <track><Energy|Magic>BoostFactor[id].
        public static double Bonus(bool magic, int id, int track, double level, double factor)
        {
            Curve[][] table = magic ? MagicCurves : EnergyCurves;
            if (id < 0 || id >= table.Length) return 1.0 + level * factor;
            Curve curve = table[id][Math.Max(0, Math.Min(2, track))];
            if (level <= curve.Softcap) return 1.0 + level * factor;
            return 1.0 + Math.Pow(level, curve.Exponent) * curve.Coefficient * factor;
        }

        public static double Ratio(bool magic, int id, int track, double level, double gained, double factor)
            => Bonus(magic, id, track, level + gained, factor) / Bonus(magic, id, track, level, factor);

        // Levels one hour adds at a rate quoted at the current level. The game caps an NGU at one level
        // per tick and divides progress by (level+1), so the quoted rate holds only for the first level:
        // with K = progress per tick at level 0, levels come one per tick until level+1 reaches K, then
        // (level+1)/K ticks each, i.e. (L+1)^2 grows by 2K per tick.
        public static double LevelsInHour(double level, double levelsPerHourNow)
        {
            if (levelsPerHourNow <= 0) return 0;
            double k = levelsPerHourNow / TicksPerHour * (level + 1.0);
            double ticks = TicksPerHour;
            double at = level;
            double cappedLevels = Math.Min(ticks, Math.Max(0.0, k - (at + 1.0)));
            at += cappedLevels;
            ticks -= cappedLevels;
            if (ticks > 0)
                at = Math.Sqrt((at + 1.0) * (at + 1.0) + 2.0 * k * ticks) - 1.0;
            return at - level;
        }
    }
}
