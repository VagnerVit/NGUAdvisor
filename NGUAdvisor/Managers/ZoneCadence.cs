using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // Live per-zone kill cadence, read from the game instead of assumed.
    //
    // AdventureController.createEnemyTable() builds enemyList in CODE (not serialized scene data),
    // so every zone's full spawn table -- name, maxHP, defense, regen, attackRate, enemyType, AI --
    // is readable at runtime and never goes stale on a rebalance. AdventureController.spawnEnemy()
    // picks UNIFORMLY from enemyList[zone], which is what makes the per-type shares exact rather
    // than the 0.77/0.10 constants the farm advisors used to hardcode (the real normal share ranges
    // from 0.714 in Ancient Battlefield to 0.8125 in Cave of Many Things).
    //
    // This also replaces the OPower gate for boost farming. OPower is calibrated on the zone BOSS
    // (zone 0: 129.5 == bossHP 100 / 0.8 + bossDef 9 / 2), but boost rolls live exclusively in the
    // enemyType.normal branch of LootDrop.zone{N}Drop -- bosses drop no boosts at all. Gating boost
    // farming on one-shotting the boss demanded 1.79x more attack than the zone actually needs.
    public static class ZoneCadence
    {
        public class EnemyFacts
        {
            public string Name;
            public double MaxHP;
            public double Defense;
            public double Regen;
            public double AttackRate;
            public int SpriteId;
            public bool Normal;
            public bool Boss;
            public bool Paralyzes;
        }

        public class ZoneFacts
        {
            public int Count;
            public int NormalCount;
            public int BossCount;
            public double NormalShare;
            public double BossShare;
            public EnemyFacts[] Enemies;
        }

        public class Estimate
        {
            public bool Known;
            public bool Killable;
            public bool OneShotsEveryNormal;
            public bool OneShotsEverySpawn;    // boss included: nothing in the zone ever gets a turn
            public double WorstNormalHits;
            public double SecondsPerSpawn;         // averaged over the whole uniform spawn table
            public double NormalKillsPerSecond;
            public double BossKillsPerSecond;
        }

        private static readonly Dictionary<int, ZoneFacts> _facts = new Dictionary<int, ZoneFacts>();

        // Immutable for the process lifetime: createEnemyTable() runs once at startup and nothing
        // mutates enemyList afterwards (spawnEnemy hands out the stored instances; only ITOPOD's
        // powerUp() clones and scales, and ITOPOD is not in enemyList).
        public static ZoneFacts Facts(int zone)
        {
            if (_facts.TryGetValue(zone, out ZoneFacts cached)) return cached;
            ZoneFacts built = null;
            try
            {
                List<List<Enemy>> table = Main.Character.adventureController.enemyList;
                if (table != null && zone >= 0 && zone < table.Count && table[zone] != null && table[zone].Count > 0)
                {
                    List<Enemy> list = table[zone];
                    List<EnemyFacts> facts = new List<EnemyFacts>(list.Count);
                    int normals = 0;
                    int bosses = 0;
                    foreach (Enemy e in list)
                    {
                        if (e == null) continue;
                        bool isNormal = e.enemyType == enemyType.normal;
                        bool isBoss = e.enemyType == enemyType.boss;
                        if (isNormal) normals++;
                        if (isBoss) bosses++;
                        facts.Add(new EnemyFacts
                        {
                            Name = e.name,
                            MaxHP = e.maxHP,
                            Defense = e.defense,
                            Regen = e.regen,
                            AttackRate = e.attackRate,
                            SpriteId = e.spriteID,
                            Normal = isNormal,
                            Boss = isBoss,
                            Paralyzes = e.AI == AI.paralyze
                        });
                    }
                    if (facts.Count > 0)
                        built = new ZoneFacts
                        {
                            Count = facts.Count,
                            NormalCount = normals,
                            BossCount = bosses,
                            NormalShare = (double)normals / facts.Count,
                            BossShare = (double)bosses / facts.Count,
                            Enemies = facts.ToArray()
                        };
                }
            }
            catch (Exception e) { Main.LogDebug($"ZoneCadence.Facts({zone}): {e.Message}"); }
            _facts[zone] = built;
            return built;
        }

        public static bool IsIdle(int combatMode) => combatMode == 0 || !CombatHelpers.RegularAttackUnlocked();

        // Seconds between our swings. Idle attacks on Adventure.attackSpeed; manual moves share
        // PlayerController's global cooldown. Both are 1s base and both drop to 0.8s off the SAME
        // unlock -- AllItemListController sets adventure.setFasterIdleAttack() and
        // itemList.redLiquidComplete (which usedMove() reads) together when the Mysterious Red
        // Liquid is maxxed -- so the per-swing rate never differs between the two modes.
        public static double SwingSeconds(int combatMode)
        {
            if (IsIdle(combatMode))
            {
                float speed = Main.Character.adventure.attackSpeed;
                return speed > 0f ? speed : 1.0;
            }
            return CombatHelpers.BaseGlobalCooldown();
        }

        // The mode's damage multiplier on totalAdvAttack(). idleAttack() applies only
        // idleAttackPower(); regularAttack() applies regAttackMulti AND offenseBuffFactor.
        private static double AttackMultiplier(int combatMode)
        {
            Character c = Main.Character;
            if (IsIdle(combatMode))
            {
                double idleMulti = c.idleAttackPower();
                return idleMulti > 0.0 ? idleMulti : 1.0;
            }
            double multi = c.regAttackPower();
            if (multi <= 0.0) multi = 1.0;
            // Modes 1-3 keep the offensive buffs up between spawns (CombatManager.DoBuffs
            // back-schedules them); mode 4 is regular-attack-only and never buffs.
            if (combatMode <= 3)
                multi *= Math.Max(1.0, c.adventureController.playerController.offenseBuffFactor);
            return multi;
        }

        // Damage one swing lands on an enemy with `defense`, using the WORST random roll -- the same
        // guaranteed-kill convention CombatAI's one-shot check uses. Both idleAttack() and
        // regularAttack() roll Random.Range(0.8f, 1.2f) on Mathf.Max(minDamage(),
        // totalAdvAttack() - defense/2), and minDamage() returns 0.
        public static double DamagePerSwing(int combatMode, double defense)
        {
            try
            {
                return DamagePerSwing(Main.Character.totalAdvAttack(), AttackMultiplier(combatMode), defense);
            }
            catch (Exception e) { Main.LogDebug($"ZoneCadence.DamagePerSwing: {e.Message}"); return 0.0; }
        }

        private static double DamagePerSwing(double attack, double multiplier, double defense)
        {
            double raw = attack - defense / 2.0;
            return raw <= 0.0 ? 0.0 : raw * multiplier * BoostValueMath.MinRoll;
        }

        // Mean damage (roll 1.0) per global-cooldown slot for a MULTI-swing fight, from the sustained
        // rotation rather than regular-attack-only.
        //
        // The one-shot gates deliberately stay on the regular attack: CombatAI checks regular first
        // (CombatAttacks) and only escalates, so "do we one-shot?" is a question about the regular
        // attack. But a fight that takes several swings really does get Strong/Piercing/Ultimate as
        // their cooldowns come up, and costing those fights at regular-attack DPS under-ranked exactly
        // the multi-swing zones we now allow to compete.
        //
        // Piercing subtracts defense/3 instead of defense/2 (CombatAI's oneShotDamage), so each move
        // is priced against its own divisor.
        private static double MeanDamagePerSlot(int combatMode, double attack, double defense)
        {
            double Raw(double divisor) => Math.Max(0.0, attack - defense / divisor);

            if (IsIdle(combatMode))
                return Raw(2.0) * AttackMultiplier(combatMode);

            Character c = Main.Character;
            double buff = combatMode <= 3
                ? Math.Max(1.0, c.adventureController.playerController.offenseBuffFactor)
                : 1.0;
            double regular = Raw(2.0) * c.regAttackPower() * buff;

            // Mode 4 is regular-attack-only by construction (CombatManager.DoCombat).
            if (combatMode >= 4) return regular;

            List<double[]> moves = new List<double[]>();
            if (CombatHelpers.UltimateAttackUnlocked())
                moves.Add(new[] { Raw(2.0) * c.ultimateAttackPower() * buff, (double)c.ultimateAttackCooldown() });
            // strongAttackPower(), not pierceAttackPower(): PlayerController.pierceAttack() multiplies
            // by adventureController.strongAttackMulti. Character.pierceAttackPower() returns
            // pierceAttackMulti, which nothing in the damage path ever reads.
            if (CombatHelpers.PiercingAttackUnlocked())
                moves.Add(new[] { Raw(3.0) * c.strongAttackPower() * buff, (double)c.pierceAttackCooldown() });
            if (CombatHelpers.StrongAttackUnlocked())
                moves.Add(new[] { Raw(2.0) * c.strongAttackPower() * buff, (double)c.strongAttackCooldown() });

            return BoostValueMath.SustainedDamagePerSlot(regular, CombatHelpers.BaseGlobalCooldown(), moves.ToArray());
        }

        // Adventure attack needed to GUARANTEE a one-shot of the hardest thing the zone can spawn,
        // with an UNMULTIPLIED swing -- i.e. directly comparable to ZoneStatHelper.EffectiveAdvAttack().
        //
        // Derivation: a swing does (attack - defense/2) * multiplier * roll with roll in [0.8, 1.2],
        // so a guaranteed kill needs attack >= maxHP / (0.8 * multiplier) + defense/2.
        //
        // This exists because the shipped OPower column is not one quantity but three, filled in
        // three different sittings (measured against the live enemy table):
        //   zones 0-9   OPower == hardestHP/0.8 + def/2   -- guaranteed one-shot (ratio 1.0000)
        //   zones 10-28 OPower == hardestHP/1.2 + def/2   -- LUCKIEST-roll one-shot, 1.5x too low
        //   zones 29-41 OPower ~= IPower                  -- the idle-survival column, 10-18x too low
        //                                                    (zone 29 is 80x IPower: a stray digit)
        // Returns 0 when the enemy table cannot be read, so callers can fall back to the column.
        public static double RawOneShotPower(int zone) => OneShotPower(zone, 1.0, false);

        public static double OneShotPowerForMode(int zone, int combatMode, bool normalsOnly)
        {
            try { return OneShotPower(zone, AttackMultiplier(combatMode), normalsOnly); }
            catch { return 0.0; }
        }

        private static double OneShotPower(int zone, double multiplier, bool normalsOnly)
        {
            ZoneFacts facts = Facts(zone);
            if (facts == null || multiplier <= 0.0) return 0.0;
            double need = 0.0;
            foreach (EnemyFacts e in facts.Enemies)
            {
                if (normalsOnly && !e.Normal) continue;
                need = Math.Max(need, e.MaxHP / (0.8 * multiplier) + e.Defense / 2.0);
            }
            return need;
        }

        // Whether the given mode one-shots EVERY spawn in the zone, boss included -- the condition
        // under which nothing in the zone ever gets a turn.
        //
        // Measured against EffectiveAdvAttack(), i.e. WITHOUT beast mode, the same conservative
        // baseline every other zone gate uses. Beast mode triples incoming damage, so leaning on it
        // to justify a relaxed entry-HP threshold is exactly backwards.
        public static bool OneShotsEverySpawn(int zone, int combatMode)
        {
            try
            {
                double need = OneShotPowerForMode(zone, combatMode, false);
                return need > 0.0 && ZoneStatHelper.EffectiveAdvAttack() >= need;
            }
            catch { return false; }
        }

        public static Estimate For(int zone, int combatMode)
        {
            Estimate est = new Estimate();
            try
            {
                ZoneFacts facts = Facts(zone);
                if (facts == null) return est;

                bool idle = IsIdle(combatMode);
                double swing = SwingSeconds(combatMode);
                double respawn = CombatHelpers.BaseRespawnTime();
                if (respawn <= 0.0) respawn = 0.0;
                double attack = Main.Character.totalAdvAttack();
                double multiplier = AttackMultiplier(combatMode);

                double totalCycle = 0.0;
                int normalCount = 0;
                int bossCount = 0;
                double worstNormalHits = 0.0;
                bool oneShotsEveryNormal = true;
                bool oneShotsEverySpawn = true;

                // CheckEnemy() retreats to the Safe Zone rather than fighting these, so they cost a
                // round trip and yield nothing. bossOnly wipes out normal kills entirely -- which for
                // boost farming means zero boosts, since only enemyType.normal rolls them.
                int[] blacklist = Main.Settings?.BlacklistedBosses;
                bool bossOnly = Main.Settings != null && Main.Settings.SnipeBossOnly && !ZoneHelpers.ZoneIsTitan(zone);

                foreach (EnemyFacts e in facts.Enemies)
                {
                    bool skipped = (blacklist != null && Array.IndexOf(blacklist, e.SpriteId) >= 0)
                        || (bossOnly && !e.Boss);
                    if (skipped)
                    {
                        // A skipped spawn is a Safe-Zone round trip: leave, come back, wait out the
                        // respawn again. Two zone moves cost a global cooldown each.
                        totalCycle += respawn + 2.0 * swing;
                        continue;
                    }

                    // Two damage numbers, two jobs: the guaranteed regular-attack swing decides whether
                    // this is a one-shot (matching CombatAI's own check), the sustained rotation's mean
                    // decides how long a longer fight takes.
                    double guaranteed = DamagePerSwing(attack, multiplier, e.Defense);
                    bool oneShot = guaranteed > 0 && e.MaxHP <= guaranteed;
                    double hits = oneShot
                        ? 1.0
                        : BoostValueMath.ExpectedHits(e.MaxHP, MeanDamagePerSlot(combatMode, attack, e.Defense), e.Regen, swing);
                    if (double.IsInfinity(hits))
                    {
                        est.Known = true;
                        est.Killable = false;
                        return est;
                    }
                    double cycle = BoostValueMath.CycleSeconds(idle, respawn, swing, hits);

                    // AI.paralyze needs TWO landed attacks to freeze us for 2s (EnemyAI.paralyzeAI:
                    // paralyzeEffect 1 warns, 2 paralyzes). Non-titan zones delay the enemy's first
                    // strike by +50% of its attack rate (CombatAI's firstStrike), so a kill that
                    // ends before two enemy swings pays nothing.
                    double killTime = idle ? hits * swing : (hits - 1.0) * swing;
                    double enemySwings = e.AttackRate > 0.0 && killTime >= 1.5 * e.AttackRate
                        ? Math.Floor((killTime - 1.5 * e.AttackRate) / e.AttackRate) + 1.0
                        : 0.0;
                    if (e.Paralyzes && enemySwings >= 2.0) cycle += 2.0;

                    totalCycle += cycle;
                    // Keyed off the GUARANTEED one-shot, never off `hits`: the sustained rotation can
                    // average out to one swing while the regular attack still needs a lucky roll, and
                    // the entry-HP relaxation downstream must not rest on luck.
                    if (!oneShot) oneShotsEverySpawn = false;
                    if (e.Normal)
                    {
                        normalCount++;
                        worstNormalHits = Math.Max(worstNormalHits, hits);
                        if (!oneShot) oneShotsEveryNormal = false;
                    }
                    if (e.Boss) bossCount++;
                }

                est.Known = true;
                est.Killable = true;
                est.SecondsPerSpawn = totalCycle / facts.Count;
                // "One-shots everything" is vacuously true when we fight nothing (every spawn skipped),
                // so require having actually considered a spawn.
                est.OneShotsEveryNormal = oneShotsEveryNormal && normalCount > 0;
                est.OneShotsEverySpawn = oneShotsEverySpawn && normalCount + bossCount > 0;
                est.WorstNormalHits = worstNormalHits;
                if (est.SecondsPerSpawn > 0.0)
                {
                    est.NormalKillsPerSecond = (double)normalCount / facts.Count / est.SecondsPerSpawn;
                    est.BossKillsPerSecond = (double)bossCount / facts.Count / est.SecondsPerSpawn;
                }
            }
            catch (Exception e) { Main.LogDebug($"ZoneCadence.For({zone}): {e.Message}"); }
            return est;
        }

        // Whether the zone is safe to park in for a long farm.
        //
        // A manual mode that one-shots every normal on the opening swing is safe by construction --
        // nothing ever gets a turn. Otherwise fall back to the zone table's stat bar, and pick the
        // RIGHT bar: the guide's Idle stats assume you eat a full attack cycle per kill (which idle
        // mode does), while its Manual stats are the bar for actively fighting. Charging idle
        // requirements at a manual mode excluded zones that are genuinely farmable there.
        public static bool Survivable(int zone, int combatMode) => Survivable(zone, combatMode, For(zone, combatMode));

        public static bool Survivable(int zone, int combatMode, Estimate est)
        {
            try
            {
                if (est == null || !est.Known || !est.Killable) return false;
                bool idle = IsIdle(combatMode);
                // EVERY spawn, not just the normals: a boss that needs five swings gets five swings
                // back, and it is the hardest thing in the zone.
                if (!idle && est.OneShotsEverySpawn) return true;
                if (ZoneStatHelper.UserOverrides != null && ZoneStatHelper.UserOverrides.TryGetValue(zone, out ZoneStats st))
                {
                    double reqPower = idle ? st.IPower : st.MPower;
                    double reqToughness = idle ? st.IToughness : st.MToughness;
                    return ZoneStatHelper.EffectiveAdvAttack() >= reqPower
                        && Main.Character.totalAdvDefense() >= reqToughness;
                }
                return est.OneShotsEveryNormal;
            }
            catch { return false; }
        }
    }
}
