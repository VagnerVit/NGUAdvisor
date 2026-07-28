using System;

namespace NGUAdvisor.Managers
{
    /// <summary>
    /// ITOPOD floor-difficulty normalizers used to convert a character's attack power
    /// into an equivalent one-shottable ITOPOD floor. Sourced from NGU Idle's ITOPOD
    /// mob-HP scaling (floor HP grows ~5% per floor). Values are duplicated across the
    /// game's own attack-type divisors; centralize here so a game patch / Evil retrack
    /// is a one-line update. NOTE: not yet read from a live-game field — if a live
    /// source is found later, wire these to it.
    /// </summary>
    public static class ItopodConstants
    {
        // Divisor applied to (attack * <attackType>Power) for most attack types
        // (idle, regular, ultimate, strong, base). Matches the game's floor-HP base.
        public const double FloorHpNormalizer = 771.375;

        // Divisor used specifically for the piercing-attack branch.
        public const double PiercingHpNormalizer = 769.25;

        // Per-floor HP growth base: floor = log_base(normalizedAttack).
        public const double FloorGrowthBase = 1.05;

        // Float aliases for the Unity float-math call sites in ITOPODManager.
        // Exact: 771.375 = 771 + 3/8 and 769.25 = 769 + 1/4 are representable in float.
        public const float FloorHpNormalizerF = (float)FloorHpNormalizer;
        public const float PiercingHpNormalizerF = (float)PiercingHpNormalizer;
    }
}
