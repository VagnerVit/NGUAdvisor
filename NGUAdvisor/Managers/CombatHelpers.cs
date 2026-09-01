using System.Collections.Generic;
using UnityEngine;

namespace NGUAdvisor.Managers
{
    public static class CombatHelpers
    {
        public enum Skill
        {
            RegularAttack,
            StrongAttack,
            Parry,
            PiercingAttack,
            UltimateAttack,
            Block,
            DefensiveBuff,
            Heal,
            OffensiveBuff,
            Charge,
            UltimateBuff,
            Paralyze,
            HyperRegen,
            BeastMode,
            MegaBuff
        }

        private static readonly Character _character = Main.Character;
        private static readonly AdventureController _ac = _character.adventureController;
        private static readonly PlayerController _pc = _ac.playerController;
        private static readonly Move69 move69 = Object.FindObjectOfType<Move69>();

        public static bool IsCurrentlyGoldSniping { get; set; }

        public static bool IsCurrentlyQuesting { get; set; }

        public static bool IsCurrentlyAdventuring { get; set; }

        public static bool IsCurrentlyFightingTitan { get; set; }

        // WHERE THE CHARACTER WAS ACTUALLY SENT, AND WHICH BRANCH DECIDED IT.
        //
        // Main.SnipeZone() is a priority cascade and eight of its branches take control with a
        // DoZone + return of their own: money pit (twice), gold snipe, titan, quest, GoldCBlockMode,
        // the EVIL CLIMB override and the no-TM gold-starvation override. Main.ResolveAdventureZone
        // models only the last three sources (gear hunt / Target ITOPOD / locked-zone fallback), so it
        // never sees any of them — and reporting used to RE-DERIVE the zone from that resolver, which
        // meant [ZoneDbg] could name the sniped zone while the character was gold-sniping or fighting
        // a titan.
        //
        // That is the defect the pick-vs-override comment in AdvisorApply.LogZoneDbg was written for,
        // one level deeper: three of these branches carry a "(user-caught)" note because the advisor
        // was somewhere other than it said. So the winning branch records itself instead of leaving
        // the answer to be guessed at afterwards.
        //
        // DIAGNOSTIC ONLY — nothing routes off these. Zone -1 means "not adventuring this pass".
        public static int RoutedZone { get; private set; }

        public static string RoutedBy { get; private set; }

        // Cleared at the top of every SnipeZone() pass, beside the IsCurrently* flags, so a branch that
        // stops winning cannot leave last pass's answer standing.
        public static void ClearRoute()
        {
            RoutedZone = -1;
            RoutedBy = null;
        }

        public static void NoteRoute(int zone, string by)
        {
            RoutedZone = zone;
            RoutedBy = by;
        }

        public static float BaseGlobalCooldown() => _character.inventory.itemList.redLiquidComplete ? 0.8f : 1f;

        public static float RemainingGlobalCooldown() => Mathf.Max(0f, _pc.moveTimer);

        public static float BaseRespawnTime() => _ac.respawnTime();

        public static float RemainingRespawnTime() => _ac.fightInProgress ? 0f : Mathf.Max(0f, BaseRespawnTime() - _ac.respawnTimer);

        #region Unlocked Moves
        private static long AttackTraining(int index) => _character.training.attackTraining[index];

        private static long DefenseTraining(int index) => _character.training.defenseTraining[index];

        public static bool RegularAttackUnlocked() => AttackTraining(0) >= 5000;

        public static bool StrongAttackUnlocked() => AttackTraining(1) >= 10000;

        public static bool ParryUnlocked() => AttackTraining(2) >= 15000;

        public static bool PiercingAttackUnlocked() => AttackTraining(3) >= 20000;

        public static bool UltimateAttackUnlocked() => AttackTraining(4) >= 25000;

        public static bool DefensiveBuffUnlocked() => DefenseTraining(0) >= 5000;

        public static bool HealUnlocked() => DefenseTraining(1) >= 10000;

        public static bool OffensiveBuffUnlocked() => DefenseTraining(2) >= 15000;

        public static bool ChargeUnlocked() => DefenseTraining(3) >= 20000;

        public static bool UltimateBuffUnlocked() => DefenseTraining(4) >= 25000;

        public static bool ParalyzeUnlocked() => _character.allChallenges.hasParalyze();

        public static bool HyperRegenUnlocked() => _character.settings.hasHyperRegen;

        public static bool BeastModeUnlocked() => _ac.hasBeastMode();

        public static bool MegaBuffUnlocked() => _character.wishes.wishes[8].level >= 1 && UltimateBuffUnlocked();

        public static bool OhShitUnlocked() => _character.wishes.wishes[58].level >= 1 && ParalyzeUnlocked() && HealUnlocked() && HyperRegenUnlocked();
        #endregion

        #region Ready Moves
        public static bool RegularAttackReady() => _ac.regularAttackMove.button.IsInteractable();

        public static bool StrongAttackReady() => _ac.strongAttackMove.button.IsInteractable();

        public static bool ParryReady() => _ac.parryMove.button.IsInteractable();

        public static bool PiercingAttackReady() => _ac.pierceMove.button.IsInteractable();

        public static bool UltimateAttackReady() => _ac.ultimateAttackMove.button.IsInteractable();

        public static bool BlockReady() => _ac.blockMove.button.IsInteractable();

        public static bool DefensiveBuffReady() => _ac.defenseBuffMove.button.IsInteractable();

        public static bool HealReady() => _ac.healMove.button.IsInteractable();

        public static bool OffensiveBuffReady() => _ac.offenseBuffMove.button.IsInteractable();

        public static bool ChargeReady() => _ac.chargeMove.button.IsInteractable();

        public static bool UltimateBuffReady() => _ac.ultimateBuffMove.button.IsInteractable();

        public static bool ParalyzeReady() => _ac.paralyzeMove.button.IsInteractable();

        public static bool HyperRegenReady() => _ac.hyperRegenMove.button.IsInteractable();

        public static bool BeastModeReady() => _ac.beastModeMove.button.IsInteractable();

        public static bool MegaBuffReady() => _ac.megaBuffMove.button.IsInteractable();

        public static bool OhShitReady() => _ac.ohShitMove.button.IsInteractable();

        public static bool Move69Ready() => move69.button.IsInteractable() && _character.adventure.move69Used < 69;
        #endregion

        #region Timers
        private static float GetTimer<T>(T move, string name) => move.GetFieldValue<T, float>(name);

        private static float RegularAttackTimer() => GetTimer(_ac.regularAttackMove, "regularAttackTimer");

        private static float StrongAttackTimer() => GetTimer(_ac.strongAttackMove, "strongAttackTimer");

        private static float ParryTimer() => GetTimer(_ac.parryMove, "parryTimer");

        private static float PiercingAttackTimer() => GetTimer(_ac.pierceMove, "attackTimer");

        private static float UltimateAttackTimer() => GetTimer(_ac.ultimateAttackMove, "ultimateAttackTimer");

        private static float BlockTimer() => GetTimer(_ac.blockMove, "blockTimer");

        public static float BlockDuration() => BlockActive() ? _ac.blockDuration - _pc.blockTime : 0f;

        private static float DefensiveBuffTimer() => _ac.defenseBuffMove.defenseBuffTimer;

        public static float DefensiveBuffDuration() => DefensiveBuffActive() ? _ac.defenseBuffDuration - _pc.defenseBuffTime : 0f;

        private static float HealTimer() => _ac.healMove.healTimer;

        private static float OffensiveBuffTimer() => _ac.offenseBuffMove.offenseBuffTimer;

        public static float OffensiveBuffDuration() => OffensiveBuffActive() ? _ac.offenseBuffDuration - _pc.offenseBuffTime : 0f;

        private static float ChargeTimer() => GetTimer(_ac.chargeMove, "chargeTimer");

        private static float UltimateBuffTimer() => _ac.ultimateBuffMove.ultimateBuffTimer;

        public static float UltimateBuffDuration() => UltimateBuffActive() ? _character.ultimateBuffDuration() - _pc.ultimateBuffTime : 0f;

        private static float ParalyzeTimer() => _ac.paralyzeMove.attackTimer;

        private static float HyperRegenTimer() => _ac.hyperRegenMove.healTimer;

        public static float HyperRegenDuration() => HyperRegenActive() ? _pc.hyperRegenTime : 0f;

        private static float BeastModeTimer() => GetTimer(_ac.beastModeMove, "attackTimer");

        private static float MegaBuffTimer() => GetTimer(_ac.megaBuffMove, "megaBuffTimer");

        public static float MegaBuffDuration() => MegaBuffActive() ? _character.megaBuffDuration() - _pc.megaBuffTime : 0f;
        #endregion

        #region Cooldowns
        public static float RegularAttackCooldown() => Mathf.Max(0f, _character.regAttackCooldown() - RegularAttackTimer());

        public static float StrongAttackCooldown() => Mathf.Max(0f, _character.strongAttackCooldown() - StrongAttackTimer());

        public static float ParryCooldown() => Mathf.Max(0f, _character.parryCooldown() - ParryTimer());

        public static float PiercingAttackCooldown() => Mathf.Max(0f, _character.pierceAttackCooldown() - PiercingAttackTimer());

        public static float UltimateAttackCooldown() => Mathf.Max(0f, _character.ultimateAttackCooldown() - UltimateAttackTimer());

        public static float BlockCooldown() => Mathf.Max(0f, _character.blockCooldown() - BlockTimer());

        public static float DefensiveBuffCooldown() => Mathf.Max(0f, _character.defenseBuffCooldown() - DefensiveBuffTimer());

        public static float HealCooldown() => Mathf.Max(0f, _character.healCooldown() - HealTimer());

        public static float OffensiveBuffCooldown() => Mathf.Max(0f, _character.offenseBuffCooldown() - OffensiveBuffTimer());

        public static float ChargeCooldown() => Mathf.Max(0f, _character.chargeCooldown() - ChargeTimer());

        public static float UltimateBuffCooldown() => Mathf.Max(0f, _character.ultimateBuffCooldown() - UltimateBuffTimer());

        public static float ParalyzeCooldown() => Mathf.Max(0f, _character.paralyzeCooldown() - ParalyzeTimer());

        public static float HyperRegenCooldown() => Mathf.Max(0f, _character.hyperRegenCooldown() - HyperRegenTimer());

        public static float BeastModeCooldown() => Mathf.Max(0f, _character.beastModeCooldown() - BeastModeTimer());

        public static float MegaBuffCooldown(bool total = false)
        {
            var cooldown = Mathf.Max(0f, _character.megaBuffCooldown() - MegaBuffTimer());
            if (total)
                cooldown = Mathf.Max(cooldown, DefensiveBuffCooldown(), OffensiveBuffCooldown(), UltimateBuffCooldown());
            return cooldown;
        }

        public static Dictionary<Skill, float> AllCooldowns()
        {
            Dictionary<Skill, float> result = new Dictionary<Skill, float>();
            if (RegularAttackUnlocked())
                result.Add(Skill.RegularAttack, RegularAttackCooldown());
            if (StrongAttackUnlocked())
                result.Add(Skill.StrongAttack, StrongAttackCooldown());
            if (ParryUnlocked())
                result.Add(Skill.Parry, ParryCooldown());
            if (PiercingAttackUnlocked())
                result.Add(Skill.PiercingAttack, PiercingAttackCooldown());
            if (UltimateAttackUnlocked())
                result.Add(Skill.UltimateAttack, UltimateAttackCooldown());
            if (DefensiveBuffUnlocked())
                result.Add(Skill.DefensiveBuff, DefensiveBuffCooldown());
            if (HealUnlocked())
                result.Add(Skill.Heal, HealCooldown());
            if (OffensiveBuffUnlocked())
                result.Add(Skill.OffensiveBuff, OffensiveBuffCooldown());
            if (ChargeUnlocked())
                result.Add(Skill.Charge, ChargeCooldown());
            if (UltimateBuffUnlocked())
                result.Add(Skill.UltimateBuff, UltimateBuffCooldown());
            if (ParalyzeUnlocked())
                result.Add(Skill.Paralyze, ParalyzeCooldown());
            if (HyperRegenUnlocked())
                result.Add(Skill.HyperRegen, HyperRegenCooldown());
            if (BeastModeUnlocked())
                result.Add(Skill.BeastMode, BeastModeCooldown());
            if (MegaBuffUnlocked())
                result.Add(Skill.MegaBuff, MegaBuffCooldown(true));
            return result;
        }
        #endregion

        #region Available Moves
        public static bool RegularAttackAvailable()
        {
            if (RegularAttackReady())
                return true;

            if (!RegularAttackUnlocked())
                return false;
            if (_pc.regularDisabled)
                return false;
            return true;
        }

        public static bool StrongAttackAvailable()
        {
            if (StrongAttackReady())
                return true;

            if (!StrongAttackUnlocked())
                return false;
            if (_pc.strongDisabled)
                return false;
            return true;
        }

        public static bool ParryAvailable()
        {
            if (ParryReady())
                return true;

            if (!ParryUnlocked())
                return false;
            return true;
        }

        public static bool PiercingAttackAvailable()
        {
            if (PiercingAttackReady())
                return true;

            if (!PiercingAttackUnlocked())
                return false;
            if (_pc.pierceDisabled)
                return false;
            return true;
        }

        public static bool UltimateAttackAvailable()
        {
            if (UltimateAttackReady())
                return true;

            if (!UltimateAttackUnlocked())
                return false;
            if (_pc.ultimateDisabled)
                return false;
            return true;
        }

        public static bool OffensiveBuffAvailable()
        {
            if (OffensiveBuffReady())
                return true;

            if (!OffensiveBuffUnlocked())
                return false;
            if (_pc.offBuffDisabled)
                return false;
            return true;
        }

        public static bool ChargeAvailable()
        {
            if (ChargeReady())
                return true;

            if (!ChargeUnlocked())
                return false;
            return true;
        }

        public static bool UltimateBuffAvailable()
        {
            if (UltimateBuffReady())
                return true;

            if (!UltimateBuffUnlocked())
                return false;
            if (_pc.ultiBuffDisabled)
                return false;
            return true;
        }

        public static bool ParalyzeAvailable()
        {
            if (ParalyzeReady())
                return true;

            if (!ParalyzeUnlocked())
                return false;
            return true;
        }

        public static bool BeastModeAvailable()
        {
            if (BeastModeReady())
                return true;

            if (!BeastModeUnlocked())
                return false;
            return true;
        }

        public static bool MegaBuffAvailable()
        {
            if (MegaBuffReady())
                return true;

            if (!MegaBuffUnlocked())
                return false;
            if (_pc.megaBuffDisabled)
                return false;
            return true;
        }
        #endregion

        #region Cast Moves
        public static bool CastParry()
        {
            if (ParryReady() && !ParryActive())
            {
                _ac.parryMove.doMove();
                return true;
            }

            return false;
        }

        public static bool CastBlock()
        {
            if (BlockReady())
            {
                _ac.blockMove.doMove();
                return true;
            }

            return false;
        }

        public static bool CastDefensiveBuff()
        {
            if (DefensiveBuffReady())
            {
                _ac.defenseBuffMove.doMove();
                return true;
            }

            return false;
        }

        public static bool CastHeal()
        {
            if (HealReady())
            {
                _ac.healMove.doMove();
                return true;
            }

            return false;
        }

        public static bool CastOffensiveBuff()
        {
            if (OffensiveBuffReady())
            {
                _ac.offenseBuffMove.doMove();
                return true;
            }

            return false;
        }

        public static bool CastCharge()
        {
            if (ChargeReady() && !ChargeActive())
            {
                _ac.chargeMove.doMove();
                return true;
            }

            return false;
        }

        public static bool CastUltimateBuff()
        {
            if (UltimateBuffReady())
            {
                _ac.ultimateBuffMove.doMove();
                return true;
            }

            return false;
        }

        public static bool CastParalyze(bool useOhShitInstead)
        {
            if (useOhShitInstead && CastOhShit())
                return true;

            if (ParalyzeReady())
            {
                _ac.paralyzeMove.doMove();
                return true;
            }

            return false;
        }

        public static bool CastHyperRegen()
        {
            if (HyperRegenReady())
            {
                _ac.hyperRegenMove.doMove();
                return true;
            }

            return false;
        }

        public static bool CastBeastMode()
        {
            if (BeastModeReady())
            {
                _ac.beastModeMove.doMove();
                return true;
            }

            return false;
        }

        public static bool CastMegaBuff()
        {
            if (MegaBuffReady())
            {
                _ac.megaBuffMove.doMove();
                return true;
            }

            return false;
        }

        public static bool CastOhShit()
        {
            if (OhShitReady())
            {
                _ac.ohShitMove.doMove();
                return true;
            }

            return false;
        }

        public static bool CastMove69()
        {
            if (Move69Ready())
            {
                move69.doMove();
                return true;
            }

            return false;
        }
        #endregion

        #region Healing
        // Max HP can be growing due to BEARd, Advanced Training, etc.
        public static bool HasFullHP() => GetHPPercentage() >= 0.99f;

        public static float GetHPPercentage() => _character.adventure.curHP / _character.totalAdvHP();
        #endregion

        #region Active Skills
        public static bool ParryActive() => _pc.isParrying;

        public static bool BlockActive() => _pc.isBlocking;

        public static bool DefensiveBuffActive() => _pc.defenseBuffTime >= 0f;

        public static bool ChargeActive() => _pc.chargeFactor > 1f;

        public static bool OffensiveBuffActive() => _pc.offenseBuffTime >= 0f;

        public static bool UltimateBuffActive() => _pc.ultimateBuffTime >= 0f;

        public static bool HyperRegenActive() => _pc.hyperRegenTime > 0f;

        public static bool BeastModeActive() => _character.adventure.beastModeOn;

        public static bool MegaBuffActive() => _pc.megaBuffTime >= 0f;
        #endregion

        #region Other Cooldowns and Secondary Buffs
        public static bool EnemyIsParalyzed(EnemyAI eai, out float? timeLeft)
        {
            var paralyzeTime = eai.GetFieldValue<EnemyAI, float>("paralyzeTime");

            bool isActive = paralyzeTime > 0;

            timeLeft = isActive ? (float?)paralyzeTime : null;

            return isActive;
        }
        #endregion
    }
}
