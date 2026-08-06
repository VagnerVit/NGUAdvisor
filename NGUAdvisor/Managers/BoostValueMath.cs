using System;

namespace NGUAdvisor.Managers
{
    // Pure boost-value math: what a boost DROP is actually worth, in adventure-stat points.
    //
    // Unity-free on purpose so tests/NGUAdvisor.Tests can link it. Every game rule encoded here is
    // named at its call site:
    //
    //  - The 13-value ladder and the id layout (id = tier for Power, +13 Toughness, +26 Special)
    //    are verified against ItemNameDesc.itemName[1..39] ("Power Boost 1" .. "Special boost 10K").
    //  - Equipment.boostEquip: an atk/def boost adds its whole value to one channel and is then
    //    CLAMPED at floor(cap * (1 + level/100)) -- the overflow is destroyed, not banked. A spec
    //    boost instead CASCADES spec1 -> spec2 -> spec3, so its usable headroom is the sum of the
    //    three slots.
    //  - InventoryController.cubePower()/cubeToughness(): the cube is a SOFT sink. Past the softcap
    //    it returns softcap + sqrt(raw - softcap), so feeding it never becomes worthless, only
    //    sharply diminishing. Treating "cube at softcap" as zero demand understated late-run value.
    //  - InventoryController.boostRecycle: consuming a boost returns the NEXT TIER DOWN with
    //    probability Character.totalRecycleBonus(), recursively, and never below tier 1
    //    (ids 1/14/27 are excluded). The returned boost keeps its type.
    public static class BoostValueMath
    {
        // Boost values by tier, tier 1..13 == Ladder[0..12].
        public static readonly double[] Ladder = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000 };

        public static double ValueOfTier(int tier)
            => tier < 1 ? 0.0 : tier > Ladder.Length ? Ladder[Ladder.Length - 1] : Ladder[tier - 1];

        // Tier (1..13) whose value equals `value`, or 0 when it is not a ladder value.
        public static int TierOfValue(double value)
        {
            for (int i = 0; i < Ladder.Length; i++)
                if (Math.Abs(Ladder[i] - value) < 1e-9) return i + 1;
            return 0;
        }

        public static double RollProbability(double baseChance, double dcFactor, double cap)
            => Math.Min(baseChance * dcFactor, cap);

        // The cube's effective contribution for a raw invested amount (game: cubePower()).
        public static double CubeEffective(double raw, double softcap)
        {
            if (softcap <= 0.0) return 0.0;
            return raw <= softcap ? raw : softcap + Math.Sqrt(raw - softcap);
        }

        // Marginal effective gain from pushing `add` more raw stat into the cube.
        public static double CubeGain(double raw, double softcap, double add)
        {
            if (add <= 0.0) return 0.0;
            return CubeEffective(raw + add, softcap) - CubeEffective(raw, softcap);
        }

        // Points a single boost of `value` delivers into one channel. Gear is a HARD sink (overflow
        // wasted, so a boost is worth min(value, best single-item headroom)); the cube is a soft
        // sink. A boost goes where it helps most, hence the max.
        public static double Delivered(double value, double gearHeadroom, double cubeRaw, double cubeSoftcap, bool cubeUsable)
        {
            double gear = Math.Min(value, Math.Max(0.0, gearHeadroom));
            double cube = cubeUsable ? CubeGain(cubeRaw, cubeSoftcap, value) : 0.0;
            return Math.Max(gear, cube);
        }

        // Expected total delivery of a dropped boost of `tier`, following the recycling chain down
        // the ladder. `deliveredForTier` prices one boost of a given tier into the SAME channel
        // (recycling preserves the boost type).
        public static double WithRecycling(int tier, double recycleChance, Func<int, double> deliveredForTier)
        {
            if (deliveredForTier == null) return 0.0;
            double r = Math.Max(0.0, Math.Min(1.0, recycleChance));
            double total = 0.0;
            double weight = 1.0;
            for (int t = tier; t >= 1; t--)
            {
                total += weight * deliveredForTier(t);
                weight *= r;
                if (weight <= 1e-12) break;
            }
            return total;
        }

        // Kill-cycle length in seconds for one enemy, given how many hits it takes.
        //
        //  - Idle (AdventureController: idleAttackTimer = 0f on spawn, and the timer only advances
        //    while a fight is in progress): the first hit lands a full attackSpeed AFTER the spawn,
        //    so every kill pays that latency.
        //  - Manual: PlayerController.moveTimer keeps ticking through the respawn, so the advisor's
        //    LateUpdate fires the opening hit on the spawn frame itself; only the FOLLOW-UP hits
        //    cost a global cooldown.
        public static double CycleSeconds(bool idle, double respawn, double cadence, double hits)
        {
            if (hits <= 0.0 || cadence <= 0.0) return double.PositiveInfinity;
            if (idle) return respawn + hits * cadence;
            return Math.Max(respawn, cadence) + (hits - 1.0) * cadence;
        }

        // Hits to kill using the WORST roll for every swing, accounting for the enemy regenerating
        // between them (AdventureController: currentEnemy.curHP += regen * deltaTime while fighting).
        // Returns +inf when our damage per cadence slot cannot outpace the regen.
        //
        // This is the GUARANTEED bound -- right for "do we one-shot this?" gates, but pessimistic as a
        // cadence input for multi-swing fights, where the rolls average out. Use ExpectedHits there.
        public static double HitsToKill(double maxHP, double damagePerHit, double enemyRegen, double cadence)
        {
            if (damagePerHit <= 0.0) return double.PositiveInfinity;
            double net = damagePerHit - Math.Max(0.0, enemyRegen) * cadence;
            if (net <= 0.0) return double.PositiveInfinity;
            return Math.Ceiling(maxHP / net);
        }

        // Roll bounds of every attack move: PlayerController.idleAttack()/regularAttack()/
        // strongAttack()/... all scale by Random.Range(0.8f, 1.2f).
        public const double MinRoll = 0.8;
        public const double MaxRoll = 1.2;

        // EXPECTED swings to kill, for farm-rate math. Fractional on purpose: averaged over a long
        // farm the swing count per kill really is fractional, and rounding it up per enemy was
        // overstating multi-swing kill times by up to 1/0.8 = 25%.
        //
        // `meanDamagePerHit` is the damage at roll 1.0 (i.e. WITHOUT the 0.8 safety factor).
        // Three regimes, by r = maxHP / net damage per swing:
        //   r <= 0.8        every roll one-shots            -> exactly 1
        //   0.8 < r <= 1.2  the first swing kills with p = (1.2 - r)/0.4, and if it does not, the
        //                   remainder is at most 0.4x a swing so the second always finishes
        //                                                   -> 2 - p  (exact at both ends: 1 and 2)
        //   r > 1.2         renewal approximation: the overshoot past maxHP averages half a swing
        //                                                   -> max(2, r + 0.5)
        public static double ExpectedHits(double maxHP, double meanDamagePerHit, double enemyRegen, double cadence)
        {
            if (meanDamagePerHit <= 0.0) return double.PositiveInfinity;
            double net = meanDamagePerHit - Math.Max(0.0, enemyRegen) * cadence;
            if (net <= 0.0) return double.PositiveInfinity;

            double r = maxHP / net;
            if (r <= MinRoll) return 1.0;
            if (r <= MaxRoll) return 2.0 - (MaxRoll - r) / (MaxRoll - MinRoll);
            return Math.Max(2.0, r + 0.5);
        }

        // Damage per global-cooldown slot from a sustained manual rotation. Over a long fight each big
        // move fires at most once per its own cooldown and every remaining slot is a regular attack --
        // which is what CombatAI's gain/loss scheduler converges to without reimplementing it.
        //
        // `moves` are (damagePerHit, cooldownSeconds) for the unlocked big moves; `regularDamage` fills
        // the rest. Damage differs per move because Piercing halves the defense subtraction differently
        // (CombatAI: defense/3 vs defense/2), so the caller prices each move itself.
        public static double SustainedDamagePerSlot(double regularDamage, double globalCooldown, params double[][] moves)
        {
            if (globalCooldown <= 0.0) return 0.0;
            double slotsPerSecond = 1.0 / globalCooldown;
            double used = 0.0;
            double damagePerSecond = 0.0;
            if (moves != null)
            {
                foreach (double[] m in moves)
                {
                    if (m == null || m.Length < 2 || m[1] <= 0.0) continue;
                    double rate = Math.Min(1.0 / m[1], Math.Max(0.0, slotsPerSecond - used));
                    if (rate <= 0.0) continue;
                    used += rate;
                    damagePerSecond += rate * m[0];
                }
            }
            damagePerSecond += Math.Max(0.0, slotsPerSecond - used) * regularDamage;
            return damagePerSecond * globalCooldown;
        }
    }
}
