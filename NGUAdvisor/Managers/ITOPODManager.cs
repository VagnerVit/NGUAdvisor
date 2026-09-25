using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static NGUAdvisor.Main;
using static NGUAdvisor.Managers.CombatHelpers;

namespace NGUAdvisor.Managers
{
    public static class ITOPODManager
    {
        private enum CombatMode
        {
            Farm,
            Push
        }

        private enum Buff
        {
            None,
            Charge,
            OffensiveBuff,
            UltimateBuff,
            MegaBuff
        }

        private static readonly Character _character = Main.Character;
        private static readonly AdventureController _ac = _character.adventureController;
        private static bool isFighting;
        private static CombatMode mode;
        private static int maxFloor;
        private static Queue<Buff> nextBuffs;
        private static bool haveCast;

        private static Adventure Adventure => _character.adventure;

        static ITOPODManager()
        {
            Initialize();
        }

        private static void Initialize()
        {
            isFighting = false;
            mode = CombatMode.Farm;
            nextBuffs = new Queue<Buff>();
            haveCast = false;
        }

        public static void Update()
        {
            CheckZone();
            PerformQuickActions();
        }

        private static void CheckZone()
        {
            if (Adventure.zone != 1000)
            {
                Initialize();
                isFighting = true; // To perform floor optimization
                _ac.zoneSelector.changeZone(1000);
            }
        }

        public static void PerformQuickActions()
        {
            if (!CheckBeastMode())
                return;
            CheckAttackMode();

            // Cast Move 69 if not pushing
            if (mode != CombatMode.Push && CastMove69())
                return;

            // Optimize floor after enemy death
            if (isFighting && !_ac.fightInProgress)
            {
                haveCast = false;
                PlanBuffs();
                OptimizeFloor();
            }

            isFighting = _ac.fightInProgress;

            CastBuff();
            if (haveCast)
                Fight();
        }

        private static bool CheckBeastMode()
        {
            if (mode == CombatMode.Farm && Settings.ITOPODBeastMode && BeastModeAvailable() && !BeastModeActive())
            {
                if (Settings.ITOPODCombatMode == 0)
                {
                    Adventure.autoattacking = false;
                    if (CastBeastMode())
                    {
                        Adventure.autoattacking = true;
                        return true;
                    }
                    return false;
                }
                CastBeastMode();
            }
            return true;
        }

        private static void CheckAttackMode()
        {
            if (!RegularAttackUnlocked())
            {
                if (!Adventure.autoattacking)
                    _ac.idleAttackMove.setToggle();
                return;
            }

            if (Adventure.autoattacking == Convert.ToBoolean(Settings.ITOPODCombatMode))
                _ac.idleAttackMove.setToggle();
        }

        private static void PlanBuffs()
        {
            if (Settings.ITOPODCombatMode == 0)
                return;

            if (mode == CombatMode.Push)
                return;

            nextBuffs.Clear();

            float time = RemainingRespawnTime() - BaseGlobalCooldown();
            float cooldown = RemainingGlobalCooldown();
            if (ChargeAvailable() && !ChargeActive() && Mathf.Max(ChargeCooldown(), cooldown) <= time)
            {
                nextBuffs.Enqueue(Buff.Charge);
                return;
            }

            if (MegaBuffAvailable())
            {
                if (Mathf.Max(MegaBuffCooldown(true), cooldown) <= time)
                {
                    nextBuffs.Enqueue(Buff.MegaBuff);
                    return;
                }
            }

            if (UltimateBuffAvailable() && Mathf.Max(UltimateBuffCooldown(), cooldown) <= time)
            {
                nextBuffs.Enqueue(Buff.UltimateBuff);
                return;
            }

            if (OffensiveBuffAvailable() && Mathf.Max(OffensiveBuffCooldown(), cooldown) <= time)
            {
                nextBuffs.Enqueue(Buff.OffensiveBuff);
                return;
            }
        }

        private static void OptimizeFloor()
        {
            if (mode == CombatMode.Push)
                return;

            // A fixed floor is an instruction, not a guess: no per-kill re-optimization, no buff-aware
            // shifting. UpdateMaxFloor has already clamped the target to what we can actually reach.
            if (FixedFloor)
            {
                SetFloor(maxFloor);
                return;
            }

            float time = RemainingRespawnTime();
            if (nextBuffs.Count > 0 && nextBuffs.First() != Buff.None)
                time = Mathf.Max(time, RemainingGlobalCooldown() + BaseGlobalCooldown());

            var multi = 1f;
            if (ChargeActive())
                multi *= _character.chargePower();
            if (OffensiveBuffDuration() >= time + 0.05f)
                multi *= 1.2f;
            if (UltimateBuffDuration() >= time + 0.05f)
                multi *= 1.3f;
            if (MegaBuffDuration() >= time + 0.05f)
                multi *= 1.2f;

            if (nextBuffs.Count > 0)
            {
                switch (nextBuffs.First())
                {
                    case Buff.Charge:
                        multi *= _character.chargePower();
                        break;
                    case Buff.OffensiveBuff:
                        multi *= 1.2f;
                        break;
                    case Buff.UltimateBuff:
                        multi *= 1.3f;
                        break;
                    case Buff.MegaBuff:
                        multi *= 1.2f * 1.2f * 1.3f;
                        break;
                }
            }

            int floor = FloorFor(ChooseAttack(time), multi);

            if (floor > Adventure.highestItopodLevel - 1)
                floor = Adventure.highestItopodLevel - 1;

            SetFloor(floor);
        }

        private static void CastBuff()
        {
            if (haveCast)
                return;

            if (Settings.ITOPODCombatMode == 0)
                return;

            if (RemainingGlobalCooldown() > 0f)
                return;

            float respawnTime = RemainingRespawnTime();
            float globalCooldown = BaseGlobalCooldown();

            if (mode == CombatMode.Farm)
            {
                if (respawnTime > globalCooldown + 0.1f)
                    return;

                if (nextBuffs.Count <= 0)
                {
                    haveCast = true;
                    return;
                }

                switch (nextBuffs.First())
                {
                    case Buff.Charge:
                        if (CastCharge())
                        {
                            haveCast = true;
                            nextBuffs.Dequeue();
                        }
                        return;
                    case Buff.OffensiveBuff:
                        if (CastOffensiveBuff())
                        {
                            haveCast = true;
                            nextBuffs.Dequeue();
                        }
                        return;
                    case Buff.UltimateBuff:
                        if (CastUltimateBuff())
                        {
                            haveCast = true;
                            nextBuffs.Dequeue();
                        }
                        return;
                    case Buff.MegaBuff:
                        if (CastMegaBuff())
                        {
                            haveCast = true;
                            nextBuffs.Dequeue();
                        }
                        return;
                    default:
                        haveCast = true;
                        nextBuffs.Dequeue();
                        return;
                }
            }

            haveCast = true;
            return;
        }

        private static void Fight()
        {
            if (!isFighting)
                return;

            if (!_ac.playerController.canUseMove || !_ac.playerController.moveCheck())
                return;

            if (Adventure.autoattacking)
                return;

            if (mode == CombatMode.Farm)
            {
                var combatAI = new CombatAI(_character, 4);
                combatAI.DoCombat();
            }
            else if (mode == CombatMode.Push)
            {
                var combatAI = new CombatAI(_character, 2);

                if (combatAI.DoPreCombat())
                    return;

                if (combatAI.DoCombatBuffs())
                    return;

                combatAI.DoCombat();
            }

        }

        // Settings.ITOPODFloorMode values. Stored numbers, not UI positions: the settings file and the
        // retired grid's Auto-Push checkbox both write them.
        public const int FloorModeOptimal = 0;
        public const int FloorModeFixed = 1;
        public const int FloorModePush = 2;

        // Fixed: the user names the floor, so the solve is skipped entirely.
        private static bool FixedFloor => Settings.ITOPODFloorMode == FloorModeFixed;

        public static bool Pushing => mode == CombatMode.Push;

        public static void UpdateMaxFloor()
        {
            _character.arbitrary.lazyITOPODOn = false;

            // Pushing
            if (_ac.itopodLevel < Adventure.itopodEnd && Adventure.itopodStart < Adventure.itopodEnd)
            {
                // Have not died yet
                if (_ac.itopodLevel >= Adventure.highestItopodLevel - 1)
                {
                    mode = CombatMode.Push;
                    return;
                }
                // Have died - turn Auto Push off
                else
                {
                    Settings.ITOPODAutoPush = false;
                    // Max mode is nothing BUT the push, so leaving it selected would show a mode that
                    // no longer does anything. A fixed target survives — it just stops climbing and
                    // farms the highest floor reached instead.
                    if (Settings.ITOPODFloorMode == FloorModePush)
                        Settings.ITOPODFloorMode = FloorModeOptimal;
                }
            }

            if (FixedFloor)
            {
                maxFloor = Math.Min(Math.Max(1, Settings.ITOPODTargetFloor), ItopodConstants.MaxFloor);
            }
            else if (Settings.ITOPODFloorMode == FloorModePush)
            {
                maxFloor = PushTargetFloor();
            }
            else
            {
                maxFloor = SolvedMaxFloor();
            }

            // The solve above read the gear worn NOW; before the push set is on, its answer is not the
            // push set's, so neither a climb nor "push finished" may be decided from it.
            bool pushGearPending = AdvisorApply.ItopodPushGearPending();

            // Push finished: nothing left to climb in the push set, so the pod goes back to farming.
            if (Settings.ITOPODFloorMode == FloorModePush && !pushGearPending
                && maxFloor <= Adventure.highestItopodLevel - 1)
            {
                Settings.ITOPODFloorMode = FloorModeOptimal;
                Settings.ITOPODAutoPush = false;
                Main.Log($"ITOPOD push finished at floor {Adventure.highestItopodLevel} — floor back to Optimal");
            }

            // Need to push
            if (maxFloor > Adventure.highestItopodLevel - 1)
            {
                if (Settings.ITOPODAutoPush && !pushGearPending)
                {
                    SetFloor(Adventure.highestItopodLevel - 1, maxFloor + 1);
                    mode = CombatMode.Push;
                    return;
                }
                else
                {
                    maxFloor = Adventure.highestItopodLevel - 1;
                }
            }

            mode = CombatMode.Farm;
        }

        // Highest floor the strongest owned attack one-shots with every unlocked buff stacked, on the
        // gear worn now, rounded down to a 10.
        private static int SolvedMaxFloor()
        {
            float buffs = 1f;

            if (OffensiveBuffUnlocked())
                buffs *= 1.2f;

            if (ChargeUnlocked())
                buffs *= _character.chargePower();

            if (UltimateBuffUnlocked())
                buffs *= 1.3f;

            if (MegaBuffUnlocked())
                buffs *= 1.2f;

            int floor = FloorFor(ChooseMaxAttack(), buffs);
            return floor - floor % 10;
        }

        // The floor a Push climbs to: the highest one whose fight is won against every ITOPOD AI, or the
        // one-shot floor when that is higher (one-shots never let the enemy swing). Read-only.
        public static int PushTargetFloor()
        {
            int won = ItopodConstants.BestWinnableFloor(PushFighter());
            int target = Math.Min(_ac.maxItopodLevel(), Math.Max(won, SolvedMaxFloor()));
            return target - target % PushFloorStep;
        }

        // User rule: a push only ever lands on a round 10 — 418 is not worth the climb over 410.
        private const int PushFloorStep = 10;

        // Live stats for the fight replay. The rotation is the one ChooseMaxAttack draws from; beast
        // mode is read live because PlayerController.takeDamage triples what it lets through.
        private static ItopodConstants.Fighter PushFighter()
        {
            bool idle = ZoneCadence.IsIdle(Settings.ITOPODCombatMode);
            var moves = new List<ItopodConstants.Move>();
            if (!idle)
            {
                if (UltimateAttackUnlocked())
                    moves.Add(new ItopodConstants.Move { Multiplier = _character.ultimateAttackPower(), CooldownSeconds = _character.ultimateAttackCooldown() });
                if (PiercingAttackUnlocked())
                    moves.Add(new ItopodConstants.Move { Multiplier = _character.strongAttackPower(), CooldownSeconds = _character.pierceAttackCooldown(), Piercing = true });
                if (StrongAttackUnlocked())
                    moves.Add(new ItopodConstants.Move { Multiplier = _character.strongAttackPower(), CooldownSeconds = _character.strongAttackCooldown() });
            }
            return new ItopodConstants.Fighter
            {
                Attack = _character.totalAdvAttack(),
                Defense = _character.totalAdvDefense(),
                MaxHp = _character.totalAdvHP(),
                Regen = _character.totalAdvHPRegen(),
                RegularMultiplier = idle ? _character.idleAttackPower() : _character.regAttackPower(),
                GlobalCooldownSeconds = idle ? ZoneCadence.SwingSeconds(0) : BaseGlobalCooldown(),
                Moves = moves.ToArray(),
                BeastMode = Adventure.beastModeOn,
            };
        }

        // The attack we will actually swing with, and what it multiplies totalAdvAttack() by.
        //
        // Piercing carries its own flag because it subtracts defense/3 rather than defense/2. Its
        // multiplier is strongAttackMulti and NOT pierceAttackMulti: PlayerController.pierceAttack()
        // reads character.adventureController.strongAttackMulti, which leaves
        // Character.pierceAttackPower() dead code as far as damage is concerned.
        private class AttackChoice
        {
            public float Multiplier;
            public bool Piercing;
        }

        private static AttackChoice Choice(float multiplier, bool piercing = false)
            => new AttackChoice { Multiplier = multiplier, Piercing = piercing };

        // The attack available within `time` seconds. time == -1 resolves it against the live
        // respawn/cooldown window.
        private static AttackChoice ChooseAttack(float time = -1f)
        {
            if (Settings.ITOPODCombatMode == 0 || !RegularAttackUnlocked())
                return Choice(_character.idleAttackPower());

            if (time == -1f)
                time = Mathf.Max(RemainingRespawnTime(), RemainingGlobalCooldown());

            if (UltimateAttackAvailable() && UltimateAttackCooldown() <= time)
                return Choice(_character.ultimateAttackPower());
            if (PiercingAttackAvailable() && PiercingAttackCooldown() <= time)
                return Choice(_character.strongAttackPower(), true);
            if (StrongAttackAvailable() && StrongAttackCooldown() <= time)
                return Choice(_character.strongAttackPower());
            return Choice(_character.regAttackPower());
        }

        // The strongest attack we own, ignoring cooldowns.
        private static AttackChoice ChooseMaxAttack()
        {
            if (Settings.ITOPODCombatMode == 0 || !RegularAttackUnlocked())
                return Choice(_character.idleAttackPower());

            if (UltimateAttackUnlocked())
                return Choice(_character.ultimateAttackPower());
            if (PiercingAttackUnlocked())
                return Choice(_character.strongAttackPower(), true);
            if (StrongAttackUnlocked())
                return Choice(_character.strongAttackPower());
            return Choice(_character.regAttackPower());
        }

        // Highest floor this attack, with `buffMulti` of buffs on top, still one-shots on the worst
        // roll. The buff multiplier is passed in rather than pre-multiplied into a normalized attack
        // because the enemy's defense term does not shrink with our multiplier -- see ItopodConstants.
        private static int FloorFor(AttackChoice choice, float buffMulti = 1f)
        {
            int floor = ItopodConstants.BestFloor(_character.totalAdvAttack(),
                choice.Multiplier * buffMulti, choice.Piercing);
            int maxLevel = _ac.maxItopodLevel();
            return floor > maxLevel ? maxLevel : floor;
        }

        // One kill's worth of the rotation: the share of kills that land at `Floor`.
        public class RotationSlice
        {
            public double Fraction;
            public int Floor;
        }

        // What ITOPOD looks like at a given Adventure combat mode, for advisors pricing it against
        // adventure zones. A "what if" question, so nothing here touches live settings.
        //
        // The old answer was a single floor derived from the regular attack alone. That is not what
        // the pod actually runs: OptimizeFloor re-picks the floor between every pair of kills from
        // whatever move is off cooldown, so the yield is an AVERAGE over the rotation, and the
        // spread between a regular swing and an ultimate under buffs is 20-30x -- roughly 66 floors,
        // more than a boost tier and a third of the PP per kill.
        public class Profile
        {
            public bool Known;
            public int CombatMode;
            public double CycleSeconds;
            public double KillsPerSecond;
            public int DefaultFloor;      // plain swing, no big move, no buff
            public int PeakFloor;
            public RotationSlice[] Slices;
        }

        private class RotationMove
        {
            public float Multiplier;
            public bool Piercing;
            public float Cooldown;
        }

        // Fraction of the time a buff is up, from its own duty cycle.
        private static double Uptime(double duration, double cooldown)
        {
            if (duration <= 0.0 || cooldown <= 0.0) return 0.0;
            return Math.Min(1.0, duration / cooldown);
        }

        public static Profile ProfileForMode(int combatMode)
        {
            Profile p = new Profile { CombatMode = combatMode };
            try
            {
                bool idle = ZoneCadence.IsIdle(combatMode);
                double swing = ZoneCadence.SwingSeconds(combatMode);
                double cycle = BoostValueMath.CycleSeconds(idle, BaseRespawnTime(), swing, 1.0);
                if (cycle <= 0.0 || double.IsInfinity(cycle) || double.IsNaN(cycle)) return p;

                // beastModeBonus() is folded into totalAdvAttack(), so sampling it live would make
                // this answer depend on whether beast happened to be up at the moment of the call.
                // Start from the beast-free baseline and add the mode's own beast policy back.
                double attack = ZoneStatHelper.EffectiveAdvAttack();
                if (Settings.ITOPODBeastMode && BeastModeUnlocked())
                    attack *= _character.inventory.itemList.purpleLiquidComplete ? 1.5 : 1.4;

                int ceiling = Math.Min(_ac.maxItopodLevel(), Math.Max(0, Adventure.highestItopodLevel - 1));
                Func<double, bool, int> floorOf = (multi, pierce) =>
                    Math.Min(ceiling, ItopodConstants.BestFloor(attack, multi, pierce));

                double regularMulti = idle || !RegularAttackUnlocked()
                    ? _character.idleAttackPower()
                    : _character.regAttackPower();
                if (regularMulti <= 0f) regularMulti = 1f;

                List<RotationSlice> slices = new List<RotationSlice>();

                if (idle)
                {
                    slices.Add(new RotationSlice { Fraction = 1.0, Floor = floorOf(regularMulti, false) });
                }
                else
                {
                    // Each big move fires at most once per its own cooldown; one kill happens per
                    // cycle, so its share of kills is cycle/cooldown. Strongest first, remainder to
                    // the regular attack -- the same accounting SustainedDamagePerSlot uses for
                    // multi-swing fights, applied to one-swing kills.
                    List<RotationMove> moves = new List<RotationMove>();
                    if (UltimateAttackUnlocked())
                        moves.Add(new RotationMove { Multiplier = _character.ultimateAttackPower(), Cooldown = _character.ultimateAttackCooldown() });
                    if (PiercingAttackUnlocked())
                        moves.Add(new RotationMove { Multiplier = _character.strongAttackPower(), Piercing = true, Cooldown = _character.pierceAttackCooldown() });
                    if (StrongAttackUnlocked())
                        moves.Add(new RotationMove { Multiplier = _character.strongAttackPower(), Cooldown = _character.strongAttackCooldown() });

                    // Buffs multiply every attack, so they split each attack share again. Treated as
                    // independent of the move schedule, which is an approximation -- CombatAI does
                    // try to line the big moves up with the buffs, so this is the conservative side.
                    double offUp = OffensiveBuffUnlocked()
                        ? Uptime(_ac.offenseBuffDuration, _character.offenseBuffCooldown()) : 0.0;
                    double ultUp = UltimateBuffUnlocked()
                        ? Uptime(_character.ultimateBuffDuration(), _character.ultimateBuffCooldown()) : 0.0;
                    double[][] buffStates =
                    {
                        new[] { (1.0 - offUp) * (1.0 - ultUp), 1.0 },
                        new[] { offUp * (1.0 - ultUp), 1.2 },
                        new[] { (1.0 - offUp) * ultUp, 1.3 },
                        new[] { offUp * ultUp, 1.2 * 1.3 },
                    };

                    List<RotationMove> schedule = new List<RotationMove>();
                    List<double> shares = new List<double>();
                    double left = 1.0;
                    foreach (RotationMove m in moves)
                    {
                        if (left <= 0.0) break;
                        if (m.Cooldown <= 0f || m.Multiplier <= 0f) continue;
                        double share = Math.Min(left, cycle / m.Cooldown);
                        if (share <= 0.0) continue;
                        schedule.Add(m);
                        shares.Add(share);
                        left -= share;
                    }
                    schedule.Add(new RotationMove { Multiplier = (float)regularMulti });
                    shares.Add(Math.Max(0.0, left));

                    for (int i = 0; i < schedule.Count; i++)
                    {
                        if (shares[i] <= 0.0) continue;
                        foreach (double[] buff in buffStates)
                        {
                            if (buff[0] <= 0.0) continue;
                            slices.Add(new RotationSlice
                            {
                                Fraction = shares[i] * buff[0],
                                Floor = floorOf(schedule[i].Multiplier * buff[1], schedule[i].Piercing)
                            });
                        }
                    }
                }

                if (slices.Count == 0) return p;

                p.Known = true;
                p.CycleSeconds = cycle;
                p.KillsPerSecond = 1.0 / cycle;
                p.DefaultFloor = floorOf(regularMulti, false);
                p.Slices = slices.ToArray();
                foreach (RotationSlice s in slices)
                    if (s.Floor > p.PeakFloor) p.PeakFloor = s.Floor;
            }
            catch (Exception e) { Main.LogDebug($"ITOPOD ProfileForMode({combatMode}): {e.Message}"); }
            return p;
        }

        private static void SetFloor(int start, int end = 0)
        {
            if (start > Adventure.highestItopodLevel - 1)
                start = Adventure.highestItopodLevel - 1;
            if (start < 0)
                start = 0;
            if (start > _ac.maxItopodLevel())
                start = _ac.maxItopodLevel();

            if (end < start)
                end = start;
            if (end < 1)
                end = 1;
            if (end > _ac.maxItopodLevel())
                end = _ac.maxItopodLevel();

            if (Adventure.itopodStart == start && Adventure.itopodEnd == end)
                return;

            Adventure.itopodStart = start;
            Adventure.itopodEnd = end;

            if (_ac.itopodLevel >= start && _ac.itopodLevel <= end)
                return;

            _ac.zoneSelector.changeZone(1000);
        }
    }
}
