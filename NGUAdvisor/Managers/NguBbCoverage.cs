using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // Does another gear set still keep every funded NGU lane at BB? The pure half of NguBbGear.
    // Gear enters each game total as (1 + Σ gear bonuses) — decomp Character.totalEnergyPower,
    // totalCapEnergy, totalNGUSpeedBonus and their magic twins — and a lane's BB is inversely
    // proportional to power × NGU speed (NGUBP.EnergyBb), so a set's effect is three factors per pool.
    public static class NguBbCoverage
    {
        // The factor a game total moves by when the worn set is swapped for another. Fractions are the
        // scorer's (1 + Σ item %/100), so their difference is exactly the change in the game's bonus sum.
        public static double SwapFactor(double gameBonus, double wornFraction, double setFraction)
            => (1 + gameBonus + setFraction - wornFraction) / (1 + gameBonus);

        // A set that gives up none of power, cap or NGU speed against the plain set costs the pool nothing.
        public static bool NoWeaker(IReadOnlyList<double> setFractions, IReadOnlyList<double> plainFractions)
            => setFractions.Zip(plainFractions, (s, p) => s >= p).All(x => x);

        // A lower cap comes straight out of the lanes' pool; a higher one is not credited, because the
        // percentage tokens ahead of the lanes (CAPTM:5, CAPWAN:30) take their share of it first.
        public static double CapChange(double cap, double capFactor) => cap * Math.Min(0, capFactor - 1);

        public static bool Covers(double available, IEnumerable<double> laneBbs, double headroom)
            => available >= laneBbs.Sum() * headroom;
    }
}
