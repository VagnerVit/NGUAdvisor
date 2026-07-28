using System;
using System.Globalization;

namespace NGUAdvisor.Managers
{
    // Single canonical large-number abbreviator, consolidating the previously-duplicated per-panel Fmt()
    // ladders (review finding #31: 6 formatters, inconsistent "Q" vs "Qa" suffixes, precision, and negative
    // handling). Uses the "Qa" quadrillion suffix and a consistent ~3-significant-figure mantissa, handles
    // negatives and NaN/Infinity, and falls back to scientific notation past the ladder.
    //
    // Named NumberFormatter (not NumberFormat) to avoid colliding with the game's global Assembly-CSharp
    // NumberFormat type, which the UI panels also reference. Unity-free and pure so it is unit-testable.
    // NOTE: GoldPanel intentionally formats via the game's own Character.display() FIRST (to honor the
    // player's in-game number-display setting) and only falls back to this ladder — that special case is
    // preserved at GoldPanel's call site, not here.
    public static class NumberFormatter
    {
        private static readonly string[] Suffixes =
            { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "De" };

        // Abbreviate a value to ~3 significant figures with an idle-game suffix ladder (K..De). Signed,
        // NaN/Infinity-safe (-> "0"), and falls back to scientific notation beyond the ladder (>= 1e36).
        public static string Abbrev(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v == 0.0) return "0";
            string sign = v < 0 ? "-" : "";
            double a = Math.Abs(v);
            int i = 0;
            while (a >= 1000 && i < Suffixes.Length - 1) { a /= 1000; i++; }
            string mant = Mantissa(a);
            // Rounding to display precision can push the mantissa to "1000" (e.g. the double nearest 1e33 lands
            // at ~999.99 No, which rounds to "1000No"); roll up one tier so it reads "1De" instead.
            if (mant == "1000")
            {
                if (i < Suffixes.Length - 1) { a /= 1000; i++; mant = Mantissa(a); }
                else return sign + Math.Abs(v).ToString("0.##e+0", CultureInfo.InvariantCulture);   // past the top suffix -> scientific
            }
            if (a >= 1000)   // exhausted the ladder and still huge (>= ~1e36) -> scientific on the true value
                return sign + Math.Abs(v).ToString("0.##e+0", CultureInfo.InvariantCulture);
            return sign + mant + Suffixes[i];
        }

        // ~3 significant figures for a mantissa in [0, 1000), trailing zeros trimmed (so 5 -> "5", not "5.00").
        // InvariantCulture on every path: on a comma-decimal locale (cs-CZ, de-DE) the default culture
        // rendered "1,5K", which broke the abbreviation tests and displayed the wrong separator in-game.
        private static string Mantissa(double m) =>
            m >= 100 ? m.ToString("0", CultureInfo.InvariantCulture)
            : m >= 10 ? m.ToString("0.#", CultureInfo.InvariantCulture)
            : m.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
