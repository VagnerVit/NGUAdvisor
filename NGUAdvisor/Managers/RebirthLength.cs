using System;
using System.Globalization;

namespace NGUAdvisor.Managers
{
    // What one more hour of the current run is worth to the Number multiplier, from the decomp
    // (Rebirth.calculateTimeMulti, AllBeardsController.timeFactor). Advisory only: it changes no rebirth.
    // nextAttackMulti is linear in timeMulti and in the previous run's timeMulti (oldTimeMulti), so a run
    // of T hours multiplies the Number term by M(T) and rebirthing every T hours compounds M(T)^(1/T) per hour.
    public static class RebirthLength
    {
        private const double HourSeconds = 3600.0;
        private const double BeardFactorCap = 8.0;
        private const double BeardFactorWindowSeconds = 10800.0;

        // Same breakpoints and divisors as Rebirth.calculateTimeMulti; past an hour it is linear in 48 h.
        public static double TimeMulti(double seconds)
        {
            if (seconds < 60.0) return seconds / 34359738368.0 / HourSeconds;
            if (seconds < 120.0) return seconds / 33554432.0 / HourSeconds;
            if (seconds < 180.0) return seconds / 518144.0 / HourSeconds;
            if (seconds < 240.0) return seconds / 16192.0 / HourSeconds;
            if (seconds < 300.0) return seconds / 2048.0 / HourSeconds;
            if (seconds < 420.0) return seconds / 512.0 / HourSeconds;
            if (seconds < 600.0) return seconds / 128.0 / HourSeconds;
            if (seconds < 720.0) return seconds / 32.0 / HourSeconds;
            if (seconds < 900.0) return seconds / 8.0 / HourSeconds;
            if (seconds < 1800.0) return seconds / 4.0 / HourSeconds;
            if (seconds < HourSeconds) return seconds / 2.0 / HourSeconds;
            return 1.0 + seconds / 172800.0;
        }

        // AllBeardsController.timeFactor: 0 below an hour, capped at 8; perk 21 shortens the ramp.
        public static double BeardTimeFactor(double seconds, int itopodPerk21Level)
        {
            if (seconds < HourSeconds) return 0.0;
            double factor = seconds / BeardFactorWindowSeconds * 24.0 / (24 - itopodPerk21Level);
            return Math.Min(factor, BeardFactorCap);
        }

        // 24 h with perk 21 at 0.
        public static double BeardCapSeconds(int itopodPerk21Level)
            => BeardFactorCap * BeardFactorWindowSeconds * (24 - itopodPerk21Level) / 24.0;

        // Relative Number gain of running one more hour.
        public static double MarginalHourGain(double seconds)
        {
            double now = TimeMulti(seconds);
            return now <= 0.0 ? 0.0 : TimeMulti(seconds + HourSeconds) / now - 1.0;
        }

        // Compounded gain per hour of rebirthing every `seconds`; 0 before the first hour, where the
        // multiplier is below 1 and the notion stops meaning anything.
        public static double AverageHourGain(double seconds)
        {
            if (seconds < HourSeconds) return 0.0;
            return Math.Pow(TimeMulti(seconds), HourSeconds / seconds) - 1.0;
        }

        public static string Describe(double seconds, int itopodPerk21Level)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            double hours = seconds / HourSeconds;
            double capHours = BeardCapSeconds(itopodPerk21Level) / HourSeconds;
            string beards = seconds >= BeardCapSeconds(itopodPerk21Level)
                ? "beard trimmings are capped"
                : $"beard trimmings stop growing at {capHours.ToString("0.#", inv)} h";
            if (seconds < HourSeconds)
                return $"run is {hours.ToString("0.0", inv)} h — Number time factor ramps up until the first hour; {beards}";
            return $"+{(MarginalHourGain(seconds) * 100.0).ToString("0.00", inv)}% Number for the next hour vs "
                 + $"{(AverageHourGain(seconds) * 100.0).ToString("0.00", inv)}%/h over the run so far ({hours.ToString("0.0", inv)} h); {beards}";
        }
    }
}
