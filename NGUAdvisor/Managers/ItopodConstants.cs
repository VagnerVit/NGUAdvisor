using System;

namespace NGUAdvisor.Managers
{
    /// <summary>
    /// ITOPOD floor math, derived from the game's own mob table and damage formula rather than
    /// from a precomputed normalizer. Unity-free so tests/NGUAdvisor.Tests can link it.
    ///
    /// Game truth (decomp):
    ///   AdventureController.createEnemyTable() -- every ITOPOD spawn is
    ///       Enemy(name, AR 1.2, atk 10, def 10, regen 1, hp 600)
    ///   AdventureController.powerUp(e, L)      -- attack/defense/maxHP/regen *= 1.05^L,
    ///                                             then each *= Random.Range(0.98f, 1.02f)
    ///   PlayerController                       -- damage = (totalAdvAttack - defense/divisor)
    ///                                             * multiplier * Random.Range(0.8f, 1.2f)
    ///                                             divisor = 3 for pierceAttack, else 2
    ///
    /// The defense subtraction happens BEFORE the multiplier, so the defense term does NOT shrink
    /// as the rotation gets stronger. The old FloorHpNormalizer/PiercingHpNormalizer constants
    /// (771.375 and 769.25) folded it in the other way -- they are exactly
    /// (600*1.02 + 10*1.02/divisor) / 0.8, i.e. correct only at multiplier 1, and increasingly
    /// OPTIMISTIC above it: +1.2 floors at multiplier 10, +3.4 at 29 (a full ult/charge/mega
    /// stack), +10.3 at 100. Those are floors the advisor would park on without being able to
    /// guarantee the one-shot it assumed.
    /// </summary>
    public static class ItopodConstants
    {
        // Per-floor stat growth: powerUp() raises every stat by this base per floor.
        public const double FloorGrowthBase = 1.05;

        // AdventureController.maxItopodLevel().
        public const int MaxFloor = 1600;

        // The single ITOPOD mob archetype, before powerUp().
        public const double BaseHp = 600.0;
        public const double BaseDefense = 10.0;

        // powerUp()'s per-stat jitter, worst case for us.
        public const double WorstEnemyRoll = 1.02;

        /// <summary>
        /// Adventure attack that GUARANTEES a one-shot on floor 0 with the given damage
        /// multiplier. Everything above scales by 1.05^floor, so this is the unit the floor
        /// logarithm is taken against.
        /// </summary>
        public static double AttackPerFloorUnit(double multiplier, bool piercing)
        {
            if (multiplier <= 0.0) return double.PositiveInfinity;
            double divisor = piercing ? 3.0 : 2.0;
            return BaseHp * WorstEnemyRoll / (BoostValueMath.MinRoll * multiplier)
                 + BaseDefense * WorstEnemyRoll / divisor;
        }

        /// <summary>
        /// Attack expressed in floor-0 one-shot units. Feed to <see cref="FloorOfNormalized"/>.
        /// </summary>
        public static double NormalizedAttack(double attack, double multiplier, bool piercing)
        {
            double unit = AttackPerFloorUnit(multiplier, piercing);
            if (unit <= 0.0 || double.IsInfinity(unit) || double.IsNaN(unit)) return 0.0;
            return attack / unit;
        }

        public static int FloorOfNormalized(double normalizedAttack)
        {
            if (normalizedAttack <= 1.0 || double.IsNaN(normalizedAttack)) return 0;
            double floor = Math.Floor(Math.Log(normalizedAttack, FloorGrowthBase));
            if (floor < 0.0) return 0;
            if (floor > MaxFloor) return MaxFloor;
            return (int)floor;
        }

        /// <summary>Highest floor this attack and multiplier can one-shot on the worst roll.</summary>
        public static int BestFloor(double attack, double multiplier, bool piercing)
            => FloorOfNormalized(NormalizedAttack(attack, multiplier, piercing));

        /// <summary>
        /// Damage multiplier required to one-shot <paramref name="floor"/>. Returns +inf when no
        /// multiplier can do it -- past a point the scaled defense alone consumes the whole swing,
        /// which is the failure mode the old formula could not express at all.
        /// </summary>
        public static double MultiplierForFloor(double attack, int floor, bool piercing)
        {
            if (attack <= 0.0) return double.PositiveInfinity;
            if (floor < 0) floor = 0;
            double perUnit = attack / Math.Pow(FloorGrowthBase, floor);
            double defenseTerm = BaseDefense * WorstEnemyRoll / (piercing ? 3.0 : 2.0);
            double headroom = perUnit - defenseTerm;
            if (headroom <= 0.0) return double.PositiveInfinity;
            return BaseHp * WorstEnemyRoll / (BoostValueMath.MinRoll * headroom);
        }

        // ---- Push: the highest floor whose FIGHT we win, not the one we one-shot ----
        //
        // Game truth (decomp EnemyAI):
        //   hit        = Mathf.Max(attack * 0.1, attack - totalAdvDefense/2) * Random.Range(0.8, 1.2)
        //   PlayerController.takeDamage: x3 while beast mode is on
        //   every AI counter resets to 0 at spawn; the ITOPOD list holds one of each AI below
        //   AdventureController regen tick: player += totalAdvHPRegen/s, enemy += regen/s (capped)
        public const double BaseAttack = 10.0;
        public const double BaseRegen = 1.0;
        public const double AttackRateSeconds = 1.2;
        public const double EnemyMinDamageShare = 0.1;
        public const double BeastDamageTakenFactor = 3.0;
        public const double PoisonAttackShare = 0.2;
        public const double RapidIntervalShare = 0.3;
        public const double ParalyzeSeconds = 2.0;
        public const double ChargerHitFactor = 4.0;

        // A fight this long (~2 h of enemy actions) is not a push floor; also keeps the replay cheap
        // enough for the UI thread.
        public const int MaxFightActions = 20000;

        public enum PodAi { Normal, Charger, Poison, Rapid, Grower, Paralyze }

        public static readonly PodAi[] PodAis =
            { PodAi.Normal, PodAi.Charger, PodAi.Poison, PodAi.Rapid, PodAi.Grower, PodAi.Paralyze };

        // One manual move of the rotation: damage multiplier, cooldown, and its defense divisor.
        public sealed class Move
        {
            public double Multiplier;
            public double CooldownSeconds;
            public bool Piercing;
        }

        public sealed class Fighter
        {
            public double Attack;
            public double Defense;
            public double MaxHp;
            public double Regen;
            public double RegularMultiplier;
            public double GlobalCooldownSeconds;
            public Move[] Moves = new Move[0];
            public bool BeastMode;
        }

        // Mean damage per second of the sustained rotation against `defense` (mean roll: a fight is
        // many swings). Buffs are left out — the conservative side.
        public static double RotationDps(Fighter f, double defense)
        {
            if (f == null || f.GlobalCooldownSeconds <= 0.0) return 0.0;
            double Raw(bool piercing) => Math.Max(0.0, f.Attack - defense / (piercing ? 3.0 : 2.0));
            double[][] moves = new double[f.Moves.Length][];
            for (int i = 0; i < f.Moves.Length; i++)
                moves[i] = new[] { Raw(f.Moves[i].Piercing) * f.Moves[i].Multiplier, f.Moves[i].CooldownSeconds };
            double perSlot = BoostValueMath.SustainedDamagePerSlot(Raw(false) * f.RegularMultiplier, f.GlobalCooldownSeconds, moves);
            return perSlot / f.GlobalCooldownSeconds;
        }

        // Action-by-action replay of one fight against `ai` on `floor`. Enemy stats and its damage roll
        // take the WORST side for us (1.02 jitter, 1.2 roll): a push death ends the push.
        public static bool WinsFight(Fighter f, int floor, PodAi ai)
        {
            if (f == null || f.MaxHp <= 0.0) return false;
            double scale = Math.Pow(FloorGrowthBase, floor) * WorstEnemyRoll;
            double enemyAttack = BaseAttack * scale;
            double enemyHp = BaseHp * scale;
            double enemyRegen = BaseRegen * scale;
            double netDps = RotationDps(f, BaseDefense * scale) - enemyRegen;
            if (netDps <= 0.0) return false;

            double hit = Math.Max(enemyAttack * EnemyMinDamageShare, enemyAttack - f.Defense / 2.0)
                       * BoostValueMath.MaxRoll * (f.BeastMode ? BeastDamageTakenFactor : 1.0);
            double poisonTick = Math.Floor(enemyAttack * PoisonAttackShare * BoostValueMath.MaxRoll);

            double hp = f.MaxHp;
            double paralyzedFor = 0.0;
            bool rapid = false;
            int counter = 0;
            int grow = 0;
            // Paralysis stretches the fight past enemyHp / netDps; twice that bounds it with room to spare.
            double actionBound = Math.Ceiling(2.0 * enemyHp / netDps / (AttackRateSeconds * RapidIntervalShare)) + 2;
            if (actionBound > MaxFightActions) return false;
            int maxActions = (int)actionBound;
            for (int action = 0; action < maxActions; action++)
            {
                double dt = AttackRateSeconds * (rapid ? RapidIntervalShare : 1.0);
                double attackingFor = Math.Max(0.0, dt - paralyzedFor);
                paralyzedFor = Math.Max(0.0, paralyzedFor - dt);
                enemyHp -= netDps * attackingFor - enemyRegen * (dt - attackingFor);
                if (enemyHp <= 0.0) return true;
                hp = Math.Min(f.MaxHp, hp + f.Regen * dt);

                switch (ai)
                {
                    case PodAi.Charger:
                        counter++;
                        if (counter < 3) hp -= hit;
                        else if (counter >= 5) { hp -= hit * ChargerHitFactor; counter = 0; }
                        break;
                    case PodAi.Poison:
                        hp -= hit;
                        if (counter >= 1 && counter <= 5) hp -= poisonTick;
                        if (counter > 5) counter = -3;
                        counter++;
                        break;
                    case PodAi.Rapid:
                        counter++;
                        if (counter < 5) hp -= hit;
                        else if (counter >= 8)
                        {
                            hp -= hit;
                            if (counter == 8) rapid = true;
                            else if (counter >= 14) { rapid = false; counter = 0; }
                        }
                        break;
                    case PodAi.Grower:
                        grow++;
                        hp -= hit * (1.0 + Math.Floor(grow / 2.0) / 5.0);
                        break;
                    case PodAi.Paralyze:
                        if (counter < 0 || counter == 1) hp -= hit;
                        else if (counter == 2) { hp -= hit; paralyzedFor = ParalyzeSeconds; counter = -10; }
                        counter++;
                        break;
                    default:
                        hp -= hit;
                        break;
                }
                if (hp <= 0.0) return false;
            }
            return false;
        }

        public static bool WinsEveryFight(Fighter f, int floor)
        {
            foreach (PodAi ai in PodAis)
                if (!WinsFight(f, floor, ai)) return false;
            return true;
        }

        // Highest floor won against every ITOPOD AI. Monotone in the floor (every enemy stat grows,
        // ours do not), so a bisection is exact.
        public static int BestWinnableFloor(Fighter f)
        {
            if (!WinsEveryFight(f, 0)) return 0;
            int lo = 0, hi = MaxFloor;
            if (WinsEveryFight(f, hi)) return hi;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (WinsEveryFight(f, mid)) lo = mid; else hi = mid;
            }
            return lo;
        }
    }
}
