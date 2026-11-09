using System;

namespace NGUAdvisor.Managers
{
    // What a beard is worth, from the game's own formulas (decomp AllBeardsController.temp*/perm*Bonus,
    // timeFactor, addedTrimmings). Each beard feeds a different stat, so values are comparable only within
    // one beard: a ×1.05 on Number and a ×1.05 on drop chance are not the same gain.
    public static class BeardValue
    {
        public const int Stats = 0, Drops = 1, Number = 2, Ngu = 3, Wandoos = 4, Adventure = 5, Golden = 6;
        public const int Count = 7;

        private const double Softcap = 1000;

        private struct Curve
        {
            public readonly double Linear, Exponent, Coefficient;
            public Curve(double linear, double exponent, double coefficient)
            {
                Linear = linear;
                Exponent = exponent;
                Coefficient = coefficient;
            }

            // Linear to the softcap, then level^Exponent × Coefficient × Linear. Exponent 0 = no softcap.
            public double At(double level)
            {
                if (level <= 0) return 1.0;
                if (Exponent <= 0 || level <= Softcap) return 1.0 + level * Linear;
                return 1.0 + Math.Pow(level, Exponent) * Coefficient * Linear;
            }
        }

        private static readonly Curve[] TempCurves =
        {
            new Curve(0.05, 0, 0),
            new Curve(0.0005, 0.3, 125.9),
            new Curve(0.01, 0.5, 31.7),
            new Curve(0.0001, 0.3, 125.9),
            new Curve(0.001, 0.5, 31.7),
            new Curve(0.001, 0.3, 125.9),
            new Curve(0.002, 0.5, 31.7),
        };

        private static readonly Curve[] PermCurves =
        {
            new Curve(0.01, 0, 0),
            new Curve(0.0005, 0.33, 102.4),
            new Curve(0.001, 0.5, 31.7),
            new Curve(0.0002, 0.3, 125.9),
            new Curve(0.002, 0.5, 31.7),
            new Curve(0.0005, 0.5, 31.7),
            new Curve(0.005, 0.5, 31.7),
        };

        public static double Temp(int id, double level) => id < 0 || id >= Count ? 1.0 : TempCurves[id].At(level);

        public static double Perm(int id, double permLevel) => id < 0 || id >= Count ? 1.0 : PermCurves[id].At(permLevel);

        // AllBeardsController.timeFactor: nothing before an hour, then run/3h scaled by perk 21, capped at 8.
        public static double TimeFactor(double runSeconds, int perk21Level)
        {
            if (runSeconds < 3600.0) return 0.0;
            double f = runSeconds / 10800.0 * 24.0 / (24 - perk21Level);
            return Math.Min(f, 8.0);
        }

        // AllBeardsController.addedTrimmings: perm levels a rebirth converts from the live level.
        public static double Trimmings(double level, double timeFactor)
        {
            if (level <= 0 || timeFactor <= 0) return 0.0;
            return Math.Min(level, Math.Floor(Math.Sqrt(level) * timeFactor));
        }

        // The lasting gain a run banks: the perm bonus after this run's trimmings over the perm bonus now.
        public static double LastingGain(int id, double levelAtRebirth, double permLevel, double timeFactor)
            => Perm(id, permLevel + Trimmings(levelAtRebirth, timeFactor)) / Perm(id, permLevel);
    }
}
