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

            if (Settings.ITOPODOptimizeMode == 2)
            {
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

            if (Settings.ITOPODOptimizeMode == 3)
            {
                int kills = _ac.lootDrop.killsUntilAP(maxFloor);
                if (kills != 3)
                    return;

                nextBuffs.Clear();

                if (!OffensiveBuffUnlocked())
                    return;

                int bestFloor = CalculateBestFloor(CalculateMaxAttack(true));
                if (bestFloor >= 1550)
                    return;

                float threshold = Mathf.Pow(1.05f, maxFloor) / CalculateMaxAttack();
                if (threshold <= 1f)
                    return;

                int tier = _ac.lootDrop.itopodTier(bestFloor);
                if (tier >= 20 && BaseRespawnTime() < 2f * BaseGlobalCooldown())
                    return;

                float chargePower = _character.chargePower();

                // Alternate between Charge, Offensive Buff and Ultimate Buff
                if (threshold <= 1.3f)
                {
                    float time = Mathf.Max(RemainingGlobalCooldown(), RemainingRespawnTime());
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime());
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime() - BaseGlobalCooldown());
                    if (tier < 20 && time < 4f)
                        time = 4f;

                    if (ChargeUnlocked() && ChargeCooldown() <= time)
                    {
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.Charge);
                    }
                    else if (threshold <= 1.2f && OffensiveBuffCooldown() <= time)
                    {
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.OffensiveBuff);
                    }
                    else if (UltimateBuffUnlocked() && UltimateBuffCooldown() <= time)
                    {
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.UltimateBuff);
                    }

                    return;
                }

                // Alternate between Charge and Buffs
                if (UltimateBuffUnlocked() && threshold <= 1.2f * 1.3f)
                {
                    float time = Mathf.Max(RemainingGlobalCooldown(), RemainingRespawnTime());
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime());
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime() - BaseGlobalCooldown());
                    if (tier < 20 && time < 4f)
                        time = 4f;

                    if (ChargeCooldown() <= time)
                    {
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.Charge);

                        return;
                    }

                    nextBuffs.Enqueue(Buff.None);

                    time = Mathf.Max(RemainingGlobalCooldown(), RemainingRespawnTime());
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime() - BaseGlobalCooldown());

                    if (OffensiveBuffCooldown() <= time)
                    {
                        nextBuffs.Enqueue(Buff.OffensiveBuff);
                    }
                    else 
                    {
                        nextBuffs.Clear();
                        return;
                    }

                    time += BaseGlobalCooldown();
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime() - BaseGlobalCooldown());

                    if (UltimateBuffCooldown() <= time)
                        nextBuffs.Enqueue(Buff.UltimateBuff);
                    else
                        nextBuffs.Clear();

                    return;
                }

                // Alternate between Charge and Mega Buff
                if (MegaBuffUnlocked() && threshold <= 1.2f * 1.2f * 1.3f)
                {
                    float time = Mathf.Max(RemainingGlobalCooldown(), RemainingRespawnTime());
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime());
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime() - BaseGlobalCooldown());
                    if (tier < 20 && time < 4f)
                        time = 4f;

                    if (ChargeCooldown() <= time)
                    {
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.Charge);
                    }
                    else if (MegaBuffCooldown(true) <= time)
                    {
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.MegaBuff);
                    }

                    return;
                }

                if (!ChargeUnlocked())
                    return;

                // Charge is both necessary and sufficient
                if (threshold <= chargePower)
                {
                    float time = Mathf.Max(RemainingGlobalCooldown(), RemainingRespawnTime());
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime());
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime() - BaseGlobalCooldown());
                    if (tier < 20 && time < 4f)
                        time = 4f;

                    if (ChargeCooldown() <= time)
                    {
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.None);
                        nextBuffs.Enqueue(Buff.Charge);
                    }

                    return;
                }

                // Alternate between Charge + Offensive Buff and Charge + Ultimate Buff
                if (threshold <= chargePower * 1.3f)
                {
                    float time = Mathf.Max(RemainingGlobalCooldown(), RemainingRespawnTime());
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime() - BaseGlobalCooldown());

                    nextBuffs.Enqueue(Buff.None);

                    if (threshold <= chargePower * 1.2f && OffensiveBuffCooldown() < time)
                    {
                        nextBuffs.Enqueue(Buff.OffensiveBuff);
                    }
                    else if (UltimateBuffUnlocked() && UltimateBuffCooldown() < time)
                    {
                        nextBuffs.Enqueue(Buff.UltimateBuff);
                    }
                    else
                    {
                        nextBuffs.Clear();
                        return;
                    }

                    time += BaseGlobalCooldown();
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime() - BaseGlobalCooldown());

                    if (ChargeCooldown() <= time)
                        nextBuffs.Enqueue(Buff.Charge);
                    else
                        nextBuffs.Clear();

                    return;
                }

                if (!UltimateBuffUnlocked())
                    return;

                // Use Charge with both Buffs
                if (threshold <= chargePower * 1.2f * 1.3f)
                {
                    float time = Mathf.Max(RemainingGlobalCooldown(), RemainingRespawnTime() - BaseGlobalCooldown());

                    if (OffensiveBuffCooldown() < time)
                        nextBuffs.Enqueue(Buff.OffensiveBuff);
                    else
                        return;

                    time += BaseGlobalCooldown();
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime() - BaseGlobalCooldown());

                    if (UltimateBuffCooldown() < time)
                    {
                        nextBuffs.Enqueue(Buff.UltimateBuff);
                    }
                    else
                    {
                        nextBuffs.Clear();
                        return;
                    }

                    time += BaseGlobalCooldown();
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime() - BaseGlobalCooldown());

                    if (ChargeCooldown() <= time)
                        nextBuffs.Enqueue(Buff.Charge);
                    else
                        nextBuffs.Clear();

                    return;
                }

                if (MegaBuffUnlocked() && threshold <= chargePower * 1.2f * 1.2f * 1.3f)
                {
                    float time = Mathf.Max(RemainingGlobalCooldown(), RemainingRespawnTime());
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime() - BaseGlobalCooldown());

                    nextBuffs.Enqueue(Buff.None);

                    if (MegaBuffCooldown(true) <= time)
                    {
                        nextBuffs.Enqueue(Buff.MegaBuff);
                    }
                    else
                    {
                        nextBuffs.Clear();
                        return;
                    }

                    time += BaseGlobalCooldown();
                    time += Mathf.Max(BaseGlobalCooldown(), BaseRespawnTime() - BaseGlobalCooldown());

                    if (ChargeCooldown() <= time)
                        nextBuffs.Enqueue(Buff.Charge);
                    else
                        nextBuffs.Clear();
                }
            }
        }

        private static void OptimizeFloor()
        {
            if (Settings.ITOPODOptimizeMode == 0)
                return;

            if (mode == CombatMode.Push)
                return;

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

            // Behaves like lazy ITOPOD shifter
            if (Settings.ITOPODOptimizeMode == 1)
            {
                if (_character.arbitrary.boughtLazyITOPOD && _character.arbitrary.lazyITOPODOn)
                    return;

                float attack = CalculateAttack();
                int floor = CalculateBestFloor(attack);

                if (floor > Adventure.highestItopodLevel - 1)
                    floor = Adventure.highestItopodLevel - 1;

                SetFloor(floor);
            }
            else
            {
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

                if (Settings.ITOPODOptimizeMode == 2)
                {
                    int floor = CalculateBestFloor(CalculateAttack(time) * multi);

                    if (floor > Adventure.highestItopodLevel - 1)
                        floor = Adventure.highestItopodLevel - 1;

                    SetFloor(floor);
                }

                if (Settings.ITOPODOptimizeMode == 3)
                {
                    int defaultFloor = CalculateBestFloor(CalculateMaxAttack(true) * multi);
                    if (defaultFloor > Adventure.highestItopodLevel - 1)
                        defaultFloor = Adventure.highestItopodLevel - 1;

                    int floor = CalculateBestFloor(CalculateAttack(time) * multi);
                    if (floor > Adventure.highestItopodLevel - 1)
                        floor = Adventure.highestItopodLevel - 1;

                    if (_ac.lootDrop.itopodTier(floor) <= _ac.lootDrop.itopodTier(defaultFloor))
                        floor = defaultFloor;

                    int tier = _ac.lootDrop.itopodTier(floor);
                    for (int i = tier; i > 0; i--)
                    {
                        int newFloor = Math.Min(floor, i * 50 - 1);
                        if (_ac.lootDrop.killsUntilAP(newFloor) == 1)
                        {
                            if (_ac.lootDrop.itopodTier(newFloor) == _ac.lootDrop.itopodTier(defaultFloor))
                                SetFloor(defaultFloor);
                            else
                                SetFloor(newFloor);
                            return;
                        }
                    }

                    SetFloor(defaultFloor);
                }
            }
        }

        private static void CastBuff()
        {
            if (haveCast)
                return;

            if (Settings.ITOPODCombatMode == 0)
                return;

            if (Settings.ITOPODOptimizeMode < 2)
            {
                haveCast = true;
                return;
            }

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

        public static void UpdateMaxFloor()
        {
            // Floor optimization is disabled
            if (Settings.ITOPODOptimizeMode == 0)
                return;

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
                }
            }

            float attack = CalculateMaxAttack();

            if (OffensiveBuffUnlocked())
                attack *= 1.2f;

            if (ChargeUnlocked())
                attack *= _character.chargePower();

            if (UltimateBuffUnlocked())
                attack *= 1.3f;

            if (MegaBuffUnlocked())
                attack *= 1.2f;

            maxFloor = CalculateBestFloor(attack);

            if (Settings.ITOPODOptimizeMode == 2)
                maxFloor -= maxFloor % 10;
            else if (Settings.ITOPODOptimizeMode == 3)
                maxFloor -= maxFloor % 50;

            // Need to push
            if (maxFloor > Adventure.highestItopodLevel - 1)
            {
                if (Settings.ITOPODAutoPush)
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

        private static float CalculateAttack(float time = -1f)
        {
            float attack = Main.Character.totalAdvAttack();

            // Using idle attack
            if (Settings.ITOPODCombatMode == 0 || !RegularAttackUnlocked())
                return attack * _character.idleAttackPower() / ItopodConstants.FloorHpNormalizerF;

            // Using regular attack
            if (Settings.ITOPODOptimizeMode == 1)
                return attack * _character.regAttackPower() / ItopodConstants.FloorHpNormalizerF;

            if (time == -1f)
                time = Mathf.Max(RemainingRespawnTime(), RemainingGlobalCooldown());

            // Using strongest attacks
            if (UltimateAttackAvailable() && UltimateAttackCooldown() <= time)
                return attack * _character.ultimateAttackPower() / ItopodConstants.FloorHpNormalizerF;
            else if (PiercingAttackAvailable() && PiercingAttackCooldown() <= time)
                return attack * _character.strongAttackPower() / ItopodConstants.PiercingHpNormalizerF;
            else if (StrongAttackAvailable() && StrongAttackCooldown() <= time)
                return attack * _character.strongAttackPower() / ItopodConstants.FloorHpNormalizerF;
            else
                return attack * _character.regAttackPower() / ItopodConstants.FloorHpNormalizerF;
        }

        private static float CalculateMaxAttack(bool regularAttack = false)
        {
            float attack = Main.Character.totalAdvAttack();

            // Using idle attack
            if (Settings.ITOPODCombatMode == 0 || !RegularAttackUnlocked())
                return attack * _character.idleAttackPower() / ItopodConstants.FloorHpNormalizerF;

            // Using regular attack
            if (Settings.ITOPODOptimizeMode == 1 || regularAttack)
                return attack * _character.regAttackPower() / ItopodConstants.FloorHpNormalizerF;

            // Using strongest attacks
            if (UltimateAttackUnlocked())
                return attack * _character.ultimateAttackPower() / ItopodConstants.FloorHpNormalizerF;
            else if (PiercingAttackUnlocked())
                return attack * _character.strongAttackPower() / ItopodConstants.PiercingHpNormalizerF;
            else if (StrongAttackUnlocked())
                return attack * _character.strongAttackPower() / ItopodConstants.FloorHpNormalizerF;
            else
                return attack * _character.regAttackPower() / ItopodConstants.FloorHpNormalizerF;
        }

        // Highest floor a given Adventure combat mode can still one-shot, for advisors that need to
        // price ITOPOD against adventure zones without disturbing the live ITOPOD settings. Same
        // normalizer and growth base as CalculateMaxAttack/CalculateBestFloor above; the mode is a
        // parameter here because the caller is asking "what if", not "what is".
        public static int OptimalFloorForMode(int combatMode)
        {
            try
            {
                float attack = Main.Character.totalAdvAttack();
                float multi = combatMode == 0 || !RegularAttackUnlocked()
                    ? _character.idleAttackPower()
                    : _character.regAttackPower();
                return CalculateBestFloor(attack * multi / ItopodConstants.FloorHpNormalizerF);
            }
            catch (Exception e) { Main.LogDebug($"OptimalFloorForMode: {e.Message}"); return 0; }
        }

        private static int CalculateBestFloor(float attack)
        {
            int floor = (int)Math.Floor(Math.Log(attack, ItopodConstants.FloorGrowthBase));

            if (floor < 0)
                return 0;

            int maxLevel = _ac.maxItopodLevel();
            if (floor > maxLevel)
                floor = maxLevel;

            return floor;
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
