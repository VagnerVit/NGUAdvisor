using UnityEngine;
using static NGUAdvisor.Managers.CombatHelpers;

namespace NGUAdvisor.Managers
{
    public class CombatAI
    {
        private static readonly Character _character = Main.Character;
        private static readonly AdventureController _ac = _character.adventureController;
        private static readonly PlayerController _pc = _ac.playerController;
        private readonly EnemyAI _eai;
        private readonly Enemy _enemy;
        private readonly int _zone;

        private readonly int _combatMode;

        private readonly float _globalMoveCooldown;

        private readonly float _blockRemainingCooldown;
        private readonly bool _blockIsActive;
        private readonly float? _blockTimeLeft;
        private readonly bool _willBlockNextAttack;
        private readonly bool _willOnlyBlockNextAttack;

        private readonly float _parryRemainingCooldown;

        private readonly float _paralyzeRemainingCooldown;
        private readonly float? _paralyzeTimeLeft;
        private readonly bool _useOhShitInsteadOfParalyze;
        private readonly bool _prioritizeParalyze;

        private CombatSnapshot _combatSnapshot = null;

        private class CombatSnapshot
        {
            private readonly int _attackNumber;
            private readonly int _specialMoveNumber;
            private readonly int? _warningMoveNumber;

            public int LoopSize { get; }

            public float EnemyAttackRate { get; }

            public float TimeTillAttack { get; }

            public int AttacksToBlock { get; }

            public float OptimalTimeToBlock { get; }

            public float? TimeTillSpecialMove { get; } = null;

            public int? SpecialMoveInNumAttacks { get; } = null;

            public bool NextAttackSpecialMove { get; } = false;

            public bool NextAttackNoDamage { get; } = false;

            public bool BlockableAttackNoDamage { get; } = false;

            public float TimeTillNextDamagingAttack { get; } = 0f;

            public bool IsAttackingRapidly { get; } = false;

            public bool EnemyHasBlockableSpecialMove { get; } = false;

            public bool HoldBlock { get; set; } = false;

            public enum WalderpCombatMove { Regular = 3, Strong = 4, Piercing = 5, Ultimate = 6 }

            public WalderpCombatMove? ForcedMove { get; private set; } = null;

            public WalderpCombatMove? BannedMove { get; private set; } = null;

            public CombatSnapshot(float enemyAttackRate, float timeTillNextAttack)
            {
                EnemyAttackRate = enemyAttackRate;
                TimeTillAttack = timeTillNextAttack;
                TimeTillNextDamagingAttack = timeTillNextAttack;

                AttacksToBlock = Mathf.CeilToInt(2.9f / enemyAttackRate);
                OptimalTimeToBlock = 2.95f - enemyAttackRate * (AttacksToBlock - 1);
            }

            public CombatSnapshot(float enemyAttackRate, float timeTillNextAttack, int loopSize) : this(enemyAttackRate, timeTillNextAttack)
            {
                LoopSize = loopSize;
            }

            public CombatSnapshot(float enemyAttackRate, float timeTillNextAttack, int loopSize, int attackNumber, int specialMoveNumber, int? warningMoveNumber, bool isAttackingRapidly = false) : this(enemyAttackRate, timeTillNextAttack, loopSize)
            {
                _attackNumber = attackNumber % LoopSize;
                _specialMoveNumber = specialMoveNumber;
                _warningMoveNumber = warningMoveNumber;

                SpecialMoveInNumAttacks = ((_specialMoveNumber - 1 - _attackNumber + LoopSize) % LoopSize) + 1;
                TimeTillSpecialMove = ((SpecialMoveInNumAttacks - 1) * enemyAttackRate) + timeTillNextAttack;
                NextAttackSpecialMove = SpecialMoveInNumAttacks == 1;

                int attacksBetweenSpecialAndWarning = (!_warningMoveNumber.HasValue || _warningMoveNumber >= _specialMoveNumber) ? 0 : _specialMoveNumber - _warningMoveNumber.Value;
                NextAttackNoDamage = _warningMoveNumber.HasValue && SpecialMoveInNumAttacks <= attacksBetweenSpecialAndWarning + 1 && SpecialMoveInNumAttacks > 1;
                BlockableAttackNoDamage = _warningMoveNumber.HasValue && SpecialMoveInNumAttacks <= attacksBetweenSpecialAndWarning + AttacksToBlock && SpecialMoveInNumAttacks > AttacksToBlock;

                TimeTillNextDamagingAttack = NextAttackNoDamage ? TimeTillSpecialMove.Value : timeTillNextAttack;

                IsAttackingRapidly = isAttackingRapidly;

                EnemyHasBlockableSpecialMove = true;
                HoldBlock = NextAttackNoDamage || BlockableAttackNoDamage;
            }

            public void SetWalderpCombatMove(WalderpCombatMove? combatMove, bool walderpSays)
            {
                ForcedMove = walderpSays ? combatMove : null;
                BannedMove = walderpSays ? null : combatMove;
            }

            public void ClearWalderpCombatMove()
            {
                ForcedMove = null;
                BannedMove = null;
            }
        }

        public CombatAI(Character character, int combatMode)
        {
            _eai = _ac.enemyAI;
            _enemy = _ac.currentEnemy;
            _zone = _character.adventure.zone;
            _combatMode = combatMode;

            _globalMoveCooldown = BaseGlobalCooldown();

            _blockRemainingCooldown = BlockCooldown();
            _parryRemainingCooldown = ParryCooldown();
            _paralyzeRemainingCooldown = ParalyzeCooldown();

            EnemyIsParalyzed(_eai, out _paralyzeTimeLeft);
            _useOhShitInsteadOfParalyze = OhShitReady() && GetHPPercentage() < 0.6f;

            _prioritizeParalyze = false;
            // Block is not as useful against Poison enemies
            _prioritizeParalyze |= _enemy.AI == AI.poison;
            // Will slow down Grower enemies' growth rate by paralyzing them often
            _prioritizeParalyze |= _enemy.AI == AI.grower;
            // Jake disables skills, it's a good idea to paralyze him often
            _prioritizeParalyze |= _enemy.enemyType == enemyType.bigBoss3;
            // Jake also appears in Amalgamate
            _prioritizeParalyze |= _enemy.enemyType == enemyType.bigBoss12V3 || _enemy.enemyType == enemyType.bigBoss12V4;
            // We will also win time by paralyzing Walderp often
            _prioritizeParalyze |= ZoneHelpers.ZoneIsWalderp(_zone);
            // Try to spend less glop on the fight
            _prioritizeParalyze |= ZoneHelpers.ZoneIsItHungers(_zone);

            var enemyAttackTimer = EAIField<float>("enemyAttackTimer");
            float enemyAttackRate = _enemy.attackRate;

            // GM has 5x attack rate in explosion mode
            if (ZoneHelpers.ZoneIsGodmother(_zone) && _eai.explosionMode)
            {
                enemyAttackRate /= 5f;
            }
            // Jake and Tip can have rapid mode
            else if (_enemy.enemyType == enemyType.bigBoss3 || _enemy.enemyType == enemyType.finalBoss)
            {
                if (EAIField<bool>("rapidMode"))
                    enemyAttackRate *= 0.15f;
                // Rapid mode delay + skipped turn duration
                else if (EAIField<int>("skipturn") < 0)
                    enemyAttackRate *= 1.15f;
            }

            var timeTillAttack = enemyAttackRate - enemyAttackTimer + (_paralyzeTimeLeft ?? 0f);

            // Normal zones initial strike is slower than others
            if (!ZoneHelpers.ZoneIsTitan(_zone) && EAIField<bool>("firstStrike"))
                timeTillAttack += enemyAttackRate * 0.5f;

            SetCombatSnapshot(enemyAttackRate, timeTillAttack);
            if (_combatSnapshot == null)
                _combatSnapshot = new CombatSnapshot(enemyAttackRate, timeTillAttack);

            _blockIsActive = BlockActive();
            _blockTimeLeft = _blockIsActive ? (float?)(_character.blockDuration() - _pc.blockTime) : null;
            _willBlockNextAttack = _blockIsActive && (_blockTimeLeft ?? 0f) > timeTillAttack;
            // Next attack is only a warning or a delay, will not trigger block
            _willBlockNextAttack &= !_combatSnapshot.NextAttackNoDamage || _combatSnapshot.BlockableAttackNoDamage;
            _willOnlyBlockNextAttack = _willBlockNextAttack && (_blockTimeLeft ?? 0) < (timeTillAttack + enemyAttackRate);
        }

        private void SetCombatSnapshot(float enemyAttackRate, float timeTillAttack)
        {
            if (_enemy.enemyType == enemyType.guardian)
            {
                // Use default logic for guardians
                return;
            }
            if (ZoneHelpers.ZoneIsWalderp(_zone))
            {
                // Walderp calls out his move on 2 with no damage, and kills you on 3 if the conditions are not met
                // This works differently to the other "blockable" special moves and should be handled separately
                _combatSnapshot = new CombatSnapshot(enemyAttackRate, timeTillAttack, 6, _eai.growCount, 3, 2);
            }
            else if (ZoneHelpers.ZoneIsBeast(_zone))
            {
                // Beast has a loop size of 10
                _combatSnapshot = new CombatSnapshot(enemyAttackRate, timeTillAttack, 10);
            }
            else if (ZoneHelpers.ZoneIsNerd(_zone))
            {
                // Nerd does a damaging warning on 3 and a big attack on 4
                _combatSnapshot = new CombatSnapshot(enemyAttackRate, timeTillAttack, 8, _eai.growCount, 4, null);
            }
            else if (ZoneHelpers.ZoneIsGodmother(_zone))
            {
                // GM does a damaging warning on 3 and a big attack on 4
                _combatSnapshot = new CombatSnapshot(enemyAttackRate, timeTillAttack, 9, _eai.growCount, 4, null, EAIField<bool>("explosionMode"));
            }
            else if (ZoneHelpers.ZoneIsExile(_zone))
            {
                int loopSize;
                int specialMoveNumber;
                int? warningMoveNumber;

                switch (_enemy.enemyType)
                {
                    case enemyType.bigBoss9V1:
                        loopSize = 10;
                        specialMoveNumber = 4;
                        warningMoveNumber = 3;
                        break;
                    case enemyType.bigBoss9V2:
                        loopSize = 9;
                        specialMoveNumber = 4;
                        warningMoveNumber = 3;
                        break;
                    case enemyType.bigBoss9V3:
                        loopSize = 8;
                        specialMoveNumber = 3;
                        warningMoveNumber = null;
                        break;
                    case enemyType.bigBoss9V4:
                        loopSize = 7;
                        specialMoveNumber = 3;
                        warningMoveNumber = null;
                        break;
                    default:
                        return;
                }

                _combatSnapshot = new CombatSnapshot(enemyAttackRate, timeTillAttack, loopSize, _eai.growCount, specialMoveNumber, warningMoveNumber);
            }
            else if (_enemy.enemyType == enemyType.bigBoss3 || _enemy.enemyType == enemyType.finalBoss)
            {
                if (EAIField<int>("locustCount") >= 10)
                {
                    // Next attack will enable rapid mode on
                    if (EAIField<int>("skipturn") < 0)
                    {
                        _combatSnapshot = new CombatSnapshot(enemyAttackRate, timeTillAttack, 1, 1, 1, null);
                    }
                    // Every attack can be special now
                    else
                    {
                        _combatSnapshot = new CombatSnapshot(enemyAttackRate, timeTillAttack)
                        {
                            HoldBlock = true
                        };
                    }
                }
                else if (EAIField<bool>("rapidMode"))
                {
                    _combatSnapshot = new CombatSnapshot(enemyAttackRate, timeTillAttack, 1, 1, 1, null, true);
                }
            }
            // Chargers and Rapid attack numbers are a bit strange, they increment the count immediately upon entering the loop and reset to 0 after the big attack
            // So the size of the loop and the special/warning move numbers are off by one
            else if (_enemy.AI == AI.charger)
            {
                _combatSnapshot = new CombatSnapshot(enemyAttackRate, timeTillAttack, 5, EAIField<int>("chargeCooldown") - 1, 4, 2);
            }
            else if (_enemy.AI == AI.rapid)
            {
                _combatSnapshot = new CombatSnapshot(enemyAttackRate, timeTillAttack, 14,
                    EAIField<int>("rapidEffect") - 1, 7, 4, EAIField<bool>("rapidMode"));
            }
            else if (_enemy.AI == AI.exploder)
            {
                _combatSnapshot = new CombatSnapshot(enemyAttackRate, timeTillAttack, 1, 1, 1, null);
            }
        }

        private T EAIField<T>(string field) => _eai.GetFieldValue<EnemyAI, T>(field);

        public bool DoPreCombat()
        {
            if (ZoneHelpers.ZoneIsWalderp(_zone))
                return WalderpPreCombat();
            else if (_combatSnapshot.EnemyHasBlockableSpecialMove)
                return BlockableEnemyPreCombat();
            return false;
        }

        public void DoCombat() => CombatAttacks();

        private float IncomingDamage(bool block, bool parry, bool charge)
        {
            float damage = Mathf.Max(_eai.minDamage(), _eai.baseDamage());
            if (ZoneHelpers.ZoneIsTitan(_zone))
                damage *= 1 + _eai.growCount / 100f;
            if (BeastModeActive())
                damage *= 3f;
            if (block)
                damage *= _character.advancedTrainingController.block.blockBonus(0);
            if (parry)
                damage /= 2f;
            if (charge && (block || parry))
                damage /= _ac.chargeMulti;

            switch (_enemy.AI)
            {
                case AI.poison:
                    var poisonEffect = EAIField<int>("poisonEffect") + 2;
                    if (poisonEffect >= 5)
                        poisonEffect = (poisonEffect - 3) / 2 + 3;
                    if (poisonEffect > 2 && poisonEffect < 6)
                        damage += _enemy.attack * 0.2f;
                    return damage;
                case AI.charger:
                    if (_combatSnapshot.NextAttackSpecialMove)
                        damage *= 4f;
                    return damage;
                case AI.exploder:
                    damage *= 1000f;
                    return damage;
                case AI.grower:
                    damage *= 1f + Mathf.Floor(_eai.growCount / 2) / 5f;
                    return damage;
            }
            return damage;
        }

        private bool WalderpPreCombat()
        {
            // Walderp issues a command and sets the inWaldoSaysLoop flag on attackNumber 2
            // If he issues a waldoSays command that attack must be used by attackNumber 3 
            // If he issues a !waldoSays command any attack aside from that one must be used by attackNumber 3 
            if (_eai.inWaldoSaysLoop)
                _combatSnapshot.SetWalderpCombatMove((CombatSnapshot.WalderpCombatMove)_eai.waldoAttackID, _eai.waldoSays);
            else
                _combatSnapshot.ClearWalderpCombatMove();

            if (_combatSnapshot.ForcedMove.HasValue)
            {
                switch (_combatSnapshot.ForcedMove)
                {
                    case CombatSnapshot.WalderpCombatMove.Regular:
                        if (RegularAttackReady())
                        {
                            _ac.regularAttackMove.doMove();
                            return true;
                        }
                        break;
                    case CombatSnapshot.WalderpCombatMove.Strong:
                        if (StrongAttackReady())
                        {
                            _ac.strongAttackMove.doMove();
                            return true;
                        }
                        break;
                    case CombatSnapshot.WalderpCombatMove.Piercing:
                        if (PiercingAttackReady())
                        {
                            _ac.pierceMove.doMove();
                            return true;
                        }
                        break;
                    case CombatSnapshot.WalderpCombatMove.Ultimate:
                        if (!UltimateAttackReady())
                            break;

                        // For Ultimate Attack we want to use Charge if possible
                        if (ChargeReady() || _combatSnapshot.TimeTillAttack <= _globalMoveCooldown + 0.05f)
                        {
                            _ac.ultimateAttackMove.doMove();
                            return true;
                        }
                        break;
                }
                // Might try to win time by using Paralyze
                return false;
            }
            else if (_combatSnapshot.BannedMove.HasValue && _combatSnapshot.TimeTillAttack <= _globalMoveCooldown + 0.05f)
            {
                CombatAttacks(true);
                return true;
            }

            return false;
        }

        private bool CanBlockBeforeHold(float offset = 0f)
        {
            // Leave block computation for the next move
            if (_blockRemainingCooldown > _globalMoveCooldown)
                return false;

            // Don't need to hold block if the enemy has no special attack
            if (!_combatSnapshot.EnemyHasBlockableSpecialMove)
                return true;

            // Adjust offset to account for remaining block cooldown
            if (_blockRemainingCooldown > offset)
                offset = _blockRemainingCooldown;

            // Time until special attack
            var remainingTime = _combatSnapshot.TimeTillSpecialMove - offset - 0.1f;

            // Can cast Paralyze to postpone special attack
            if (ParalyzeAvailable() && _paralyzeRemainingCooldown + 0.1f < remainingTime)
                remainingTime += 3f;

            // Block won't get off cooldown before special attack
            if (remainingTime < _character.blockCooldown())
                return false;

            return true;
        }

        private bool BlockableEnemyPreCombat()
        {
            // Hold Block for the special attack
            _combatSnapshot.HoldBlock |= !CanBlockBeforeHold();

            if (_combatSnapshot.SpecialMoveInNumAttacks == 1)
            {
                // Charge is active, we should not waste it on block if possible
                if (ChargeActive() && CastParalyze(_useOhShitInsteadOfParalyze))
                    return true;

                // Exile only does the big attack some of the time
                if (ZoneHelpers.ZoneIsExile(_zone) && _eai.auraID != 1000)
                {
                    _combatSnapshot.HoldBlock = false;
                }
                else
                {
                    // Bypass all other combat logic to wait until the right time to block
                    if (_blockRemainingCooldown <= _globalMoveCooldown && _blockRemainingCooldown < _combatSnapshot.TimeTillAttack && _combatSnapshot.TimeTillAttack <= _globalMoveCooldown)
                    {
                        if (_combatSnapshot.TimeTillAttack < _combatSnapshot.OptimalTimeToBlock)
                            CastBlock();

                        return true;
                    }
                    else
                    {
                        _combatSnapshot.HoldBlock = true;
                    }

                    // Special incoming which cannot be blocked, red alert
                    if (_blockRemainingCooldown >= _combatSnapshot.TimeTillAttack)
                    {
                        // Paralyze cant help us either, try ANYTHING to keep us alive
                        if (_paralyzeRemainingCooldown >= _combatSnapshot.TimeTillAttack || _blockRemainingCooldown >= (_combatSnapshot.TimeTillAttack + 3))
                        {
                            // Beast mode makes us take 3x damage, that is the first to disable if possible
                            if (BeastModeActive() && BeastModeCooldown() <= 0f)
                            {
                                CastBeastMode();
                                return true; // Button might be still not interactable due to a bug
                            }

                            // Parry reduces incoming damage by 50%, this is the next best option
                            if (CastParry())
                                return true;

                            // Charge further reduces damage if Parry is active
                            if (_combatMode <= 2 && ParryActive() && CastCharge())
                                return true;

                            // See if we can cast any defensive buffs
                            if (CastMegaBuff())
                                return true;

                            if (!MegaBuffUnlocked())
                            {
                                if (CastUltimateBuff())
                                    return true;

                                if (CastDefensiveBuff())
                                    return true;
                            }

                            // Otherwise top off health and hope for the best
                            if (GetHPPercentage() < .90)
                            {
                                if (CastHeal())
                                    return true;

                                if (CastHyperRegen())
                                    return true;
                            }
                        }
                        else
                        {
                            if (_paralyzeRemainingCooldown < _globalMoveCooldown)
                            {
                                CastParalyze(_useOhShitInsteadOfParalyze);
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private bool DoHeal()
        {
            if (GetHPPercentage() < 0.75)
            {
                // Never cast Hyper Regen when regen negation aura is active
                if (_eai.auraID == 6)
                    return CastHeal();

                var enemyHasRegenNegation = _enemy.enemyType == enemyType.bigBoss6V4; // Beast V4 has regen negation aura
                enemyHasRegenNegation |= _enemy.enemyType == enemyType.bigBoss9V2; // Exile V2 and beyond has regen negation aura
                enemyHasRegenNegation |= _enemy.enemyType == enemyType.bigBoss9V3;
                enemyHasRegenNegation |= _enemy.enemyType == enemyType.bigBoss9V4;

                if (enemyHasRegenNegation)
                {
                    // Number of attacks until possible regen negation aura
                    var moves = 2 - _eai.growCount % _combatSnapshot.LoopSize;
                    if (moves < 0)
                        moves += _combatSnapshot.LoopSize;

                    // Amount of time until possible regen negation aura
                    var time = moves * _combatSnapshot.EnemyAttackRate + _combatSnapshot.TimeTillAttack;

                    // An opportunity to cast Hyper Regen, otherwise would be wasted
                    if (time <= 5.05f + _globalMoveCooldown && time >= 3f && CastHyperRegen())
                        return true;

                    return CastHeal();
                }

                if (CastHeal())
                    return true;

                if (GetHPPercentage() < 0.65)
                    return CastHyperRegen();
            }

            return false;
        }

        public bool DoCombatBuffs()
        {
            if (DefensiveRoutine())
                return true;

            // Mega Buff has an issue when the button is not immediately interactable after going off cooldown
            if (MegaBuffAvailable() && MegaBuffCooldown(true) == 0f)
            {
                CastMegaBuff();
                return true;
            }

            if (!MegaBuffUnlocked())
            {
                // If we are activating Beast Mode, we want all buffs at once
                if (BeastModeReady() || BeastModeActive())
                {
                    // Don't activate Beast Mode if HP is too low
                    var tooLowHP = false;
                    if (_combatMode == 1 && GetHPPercentage() < 0.9f)
                        tooLowHP = true;
                    else if (_combatMode == 2 && GetHPPercentage() < 0.75f)
                        tooLowHP = true;

                    if (!tooLowHP)
                    {
                        // Don't cast Defensive Buff in offensive mode
                        if (_combatMode <= 2 && CastDefensiveBuff())
                            return true;

                        if (CastOffensiveBuff() || CastUltimateBuff())
                            return true;
                    }
                }

                // Try to sync buffs in snipe and defensive mode
                var shouldCastBuff = _combatMode >= 3;
                shouldCastBuff |= UltimateBuffCooldown() <= _globalMoveCooldown;
                shouldCastBuff |= UltimateBuffDuration() >= (_globalMoveCooldown + 0.05f) * 5f;
                if ((shouldCastBuff && CastOffensiveBuff()) || CastUltimateBuff())
                    return true;

                // Only cast Defensive Buff in snipe or defensive mode and if HP is below 90%
                shouldCastBuff = _combatMode <= 2 && GetHPPercentage() < 0.9f && !_willBlockNextAttack;
                // Don't cast Defensive Buff too early
                shouldCastBuff &= _combatSnapshot.TimeTillNextDamagingAttack <= _globalMoveCooldown + 0.05f;
                // Don't cast Defensive Buff if our defense is buffed already
                shouldCastBuff &= _pc.defenseDebuffFactor * _pc.defenseBuffFactor <= 1f;
                if (shouldCastBuff && CastDefensiveBuff())
                    return true;
            }

            if (DoHeal())
                return true;

            if (CheckEnableBeastMode())
                return true;

            if (ChargeReady())
            {
                // Don't have enough time to cast Charge, Ultimate Attack and Block
                if (_combatSnapshot.TimeTillSpecialMove < (_globalMoveCooldown + 0.05f) * 2f)
                    return false;

                // If next attack will be blocked charge would be wasted
                var blockableAttackClose = _combatSnapshot.BlockableAttackNoDamage || !_combatSnapshot.NextAttackNoDamage;
                blockableAttackClose &= _combatSnapshot.TimeTillAttack < _globalMoveCooldown + 0.05f;
                if (_willBlockNextAttack && blockableAttackClose)
                    return false;
                var willBlock = !_combatSnapshot.HoldBlock || CanBlockBeforeHold(_blockRemainingCooldown + 0.05f);
                willBlock &= _blockRemainingCooldown < _globalMoveCooldown * 2f + 0.05f && blockableAttackClose;
                if (willBlock)
                    return false;

                // In snipe and defensive modes only use Charge when Offensive and Ultimate buffs are active (if they are not disabled)
                if (_combatMode <= 2)
                {
                    // Don't wait for Ultimate Buff if it is disabled or not unlocked
                    bool waitForUltimateBuff = UltimateBuffAvailable();
                    // Don't wait for Ultimate Buff because we still can utilize it
                    waitForUltimateBuff &= UltimateBuffDuration() <= (_globalMoveCooldown + 0.05f) * 2f;
                    // Don't wait for Ultimate Buff if we will be able to use Charge again when it will be active next time
                    waitForUltimateBuff &= _character.chargeCooldown() > UltimateBuffCooldown() + _character.ultimateBuffDuration() - (_globalMoveCooldown + 0.05f) * 5f;

                    // Don't wait for Offensive Buff if it is disabled or not unlocked
                    bool waitForOffensiveBuff = OffensiveBuffAvailable();
                    // Don't wait for Offensive Buff because we still can utilize it
                    waitForOffensiveBuff &= OffensiveBuffDuration() <= (_globalMoveCooldown + 0.05f) * 2f;
                    // Don't wait for Offensive Buff if we will be able to use Charge again when it will be active next time
                    waitForOffensiveBuff &= _character.chargeCooldown() > OffensiveBuffCooldown() + _character.offenseBuffDuration() - (_globalMoveCooldown + 0.05f) * 5f;
                    if (waitForUltimateBuff || waitForOffensiveBuff)
                        return false;
                }

                bool ultimateAttackAvailable = UltimateAttackAvailable();
                float ultimateAttackCooldown = UltimateAttackCooldown();
                // Ignore Parry for Charge if it does not deal 3x damage
                bool ignoreParry = !_character.inventory.itemList.beast1complete || !ParryAvailable();
                // Use Charge if Parry is active and next Ultimate Attack has a long cooldown
                bool chargeParry = !ignoreParry && (ultimateAttackCooldown > _globalMoveCooldown * 5f || !ultimateAttackAvailable) && ParryActive();
                // Don't activate Charge too early
                chargeParry &= _combatSnapshot.TimeTillAttack < _globalMoveCooldown + 0.05f;
                // Use Charge if next ultimate attack is close
                bool chargeUltimate = ultimateAttackAvailable && ultimateAttackCooldown <= _globalMoveCooldown;

                bool willParryNextAttack = ParryActive();
                willParryNextAttack &= !_combatSnapshot.NextAttackNoDamage || _combatSnapshot.BlockableAttackNoDamage;
                // Enemy will trigger Parry before we can cast Ultimate Attack
                if (willParryNextAttack && blockableAttackClose)
                    chargeUltimate = false;

                if (chargeUltimate || chargeParry || (!ultimateAttackAvailable && ignoreParry))
                    return CastCharge();
            }

            return false;
        }

        private float StrongestAttack()
        {
            var damage = 0f;
            if (UltimateAttackReady())
                damage = _character.ultimateAttackPower();
            else if (ParryActive() && _character.inventory.itemList.beast1complete)
                damage = 3f;
            else if (PiercingAttackReady() || StrongAttackReady())
                damage = _character.strongAttackPower();
            else if (RegularAttackReady())
                damage = _character.regAttackPower();
            else if (ParryActive())
                damage = 1f;
            return damage;
        }

        private bool ShouldBlock()
        {
            // Can't cast block
            if (_blockRemainingCooldown > _globalMoveCooldown)
                return false;

            // Don't block if Block is held for a special attack or a better moment
            if (_combatSnapshot.HoldBlock)
                return false;

            var timeToBlock = _combatSnapshot.TimeTillNextDamagingAttack - _combatSnapshot.OptimalTimeToBlock;
            if (timeToBlock < _globalMoveCooldown && CanBlockBeforeHold(timeToBlock))
                return true;

            var withoutBlock = IncomingDamage(false, ParryActive(), ChargeActive());
            var withBlock = IncomingDamage(true, ParryActive(), ChargeActive());
            float savedHP = (withoutBlock - withBlock) / _ac.maxHP();

            // Don't block if won't save HP
            if (savedHP <= 0f)
                return false;

            var shouldBlock = true;

            // Don't block weak attacks
            if (_combatMode == 1 && savedHP < 0.03f)
                shouldBlock = false;
            if (_combatMode == 2 && savedHP < 0.05f)
                shouldBlock = false;

            float damage = StrongestAttack() * _pc.chargeFactor;
            var extraDamage = 0f;

            if (ChargeActive())
            {
                damage *= _pc.offenseBuffFactor;
                extraDamage = damage - damage / _pc.chargeFactor;
            }
            else if (MegaBuffActive())
            {
                // Last chance to utilize active Mega Buff
                if (MegaBuffDuration() < BaseGlobalCooldown() + 0.05f)
                    extraDamage = damage * (_pc.offenseBuffFactor - 1f);
            }
            else
            {
                // Last chance to utilize active Ultimate Buff
                if (UltimateBuffActive() && UltimateBuffDuration() < BaseGlobalCooldown() + 0.05f)
                    extraDamage = damage * 0.3f * (OffensiveBuffActive() ? 1.2f : 1f);
                // Last chance to utilize active Offensive Buff
                else if (OffensiveBuffActive() && OffensiveBuffDuration() < BaseGlobalCooldown() + 0.05f)
                    extraDamage = damage * 0.2f * (UltimateBuffActive() ? 1.3f : 1f);
            }

            // Don't block if our attack is stronger than enemy's
            if (extraDamage / _enemy.maxHP > savedHP)
                shouldBlock = false;

            return shouldBlock;
        }

        private bool DoBlock()
        {
            var shouldBlock = ShouldBlock();
            // Delay - If the time till the next attack is above the optimal time for blocking multiple attacks
            var delayBlock = shouldBlock && _combatSnapshot.TimeTillNextDamagingAttack > _combatSnapshot.OptimalTimeToBlock;
            // WaitFor - The best time to block is immediately after the optimal time to block (..duh)
            var waitForBlock = (delayBlock || (shouldBlock && (_blockRemainingCooldown + 0.05f) < _combatSnapshot.TimeTillNextDamagingAttack)) && _blockRemainingCooldown < _globalMoveCooldown && (_combatSnapshot.TimeTillNextDamagingAttack - _globalMoveCooldown) < _combatSnapshot.OptimalTimeToBlock;

            // Block is the most critical defensive cooldown. Try to use as much as possible and as close to the optimal time as possible to block multiple attacks.
            if (shouldBlock && !delayBlock)
            {
                if (CastBlock())
                    return true;
            }

            // Defensive cooldowns are critical to staying alive, its worth delaying combat for fractions of a second to optimize uptime
            return waitForBlock;
        }

        private bool ShouldParalyze()
        {
            if (!ParalyzeUnlocked())
                return false;

            // Don't use Paralyze amidst rapid attack mode
            if (_combatSnapshot.IsAttackingRapidly)
                return false;

            // Casting Paralyze now could waste Block
            if (_combatMode > 1 && _willBlockNextAttack)
                return false;

            // Knee cap is one of the worst moments to use Paralyze at
            if (_eai.kneeCapped)
                return false;

            if (_prioritizeParalyze)
                return true;

            // Paralyze if it will take off at least 2 seconds from block's cooldown (optimally it will take off all 3 seconds)
            if (_blockRemainingCooldown >= 2.0f)
                return true;

            return false;
        }

        private bool DoParalyze()
        {
            var shouldParalyze = ShouldParalyze();
            // Delay - If we're blocking the next attack (never Paralyze an attack that can be blocked)
            var delayParalyze = shouldParalyze && _willBlockNextAttack && !_combatSnapshot.ForcedMove.HasValue;
            // WaitFor - The best time to paralyze is immediately after the final blocked attack occurs
            var waitForParalyze = (delayParalyze || (shouldParalyze && _paralyzeRemainingCooldown < _combatSnapshot.TimeTillNextDamagingAttack)) && _paralyzeRemainingCooldown <= _globalMoveCooldown && _willOnlyBlockNextAttack && _combatSnapshot.TimeTillNextDamagingAttack < _globalMoveCooldown;

            // Paralyze pauses the enemy attack timer, cast ASAP if Block will not cover the next damaging attack and if Block is on cooldown to maximize Block uptime
            if (shouldParalyze && !delayParalyze)
            {
                if (CastParalyze(_useOhShitInsteadOfParalyze))
                    return true;
            }

            // Defensive cooldowns are critical to staying alive, its worth delaying combat for fractions of a second to optimize uptime
            return waitForParalyze;
        }

        private bool ShouldParry(out bool waitForParry)
        {
            waitForParry = false;

            if (!ParryUnlocked() || ParryActive())
                return false;

            // Don't use Parry if next attack will be blocked
            if (_combatMode >= 3 && _willBlockNextAttack)
                return false;

            // Don't use Parry if we are going to use Block for a special attack
            if (_combatSnapshot.NextAttackSpecialMove)
            {
                if (BlockReady())
                    return false;

                if (_combatSnapshot.TimeTillSpecialMove >= _blockRemainingCooldown + 0.05f)
                    return false;
            }

            var savedHP = 0f;
            if (_combatMode <= 2)
            {
                var withoutParry = IncomingDamage(BlockActive(), false, ChargeActive());
                var withParry = IncomingDamage(BlockActive(), true, ChargeActive());
                savedHP = (withoutParry - withParry) / _ac.maxHP();

                // We should only wait for Parry if we are using it defensively
                if (_combatMode == 1 && savedHP > 0.03f * _pc.chargeFactor)
                    waitForParry = true;
                else if (_combatMode == 2 && savedHP > 0.05f * _pc.chargeFactor)
                    waitForParry = true;
            }

            // Give some leeway for delay between frames
            var timeTillDamagingAttack = _combatSnapshot.TimeTillNextDamagingAttack - 0.05f;

            if (ChargeActive() && timeTillDamagingAttack <= _globalMoveCooldown)
            {
                var ultimateAttackCooldown = UltimateAttackCooldown();
                var time = ultimateAttackCooldown - _globalMoveCooldown - 0.1f;
                // Keep Charge for Ultimate Attack if it deals more damage than it saves HP
                if (ultimateAttackCooldown <= _globalMoveCooldown * 3f)
                {
                    var damage = _pc.baseDamage() * _pc.offenseDebuffFactor * (_pc.chargeFactor - 1f) * _character.ultimateAttackPower();
                    if (MegaBuffDuration() > ultimateAttackCooldown - 0.1f || MegaBuffCooldown() < time)
                        damage *= 1.2f * 1.3f * 1.2f;
                    else
                    {
                        if (UltimateBuffDuration() > ultimateAttackCooldown - 0.1f || UltimateBuffCooldown() < time)
                        {
                            damage *= 1.3f;
                            time -= _globalMoveCooldown + 0.05f;
                            if (OffensiveBuffDuration() > ultimateAttackCooldown - 0.1f || OffensiveBuffCooldown() < time)
                                damage *= 1.2f;
                        }
                        else if (OffensiveBuffDuration() > ultimateAttackCooldown - 0.1f || OffensiveBuffCooldown() < time)
                        {
                            damage *= 1.2f;
                            time -= _globalMoveCooldown + 0.05f;
                            if (UltimateBuffDuration() > ultimateAttackCooldown - 0.1f || UltimateBuffCooldown() < time)
                                damage *= 1.3f;
                        }
                    }
                    if (damage >= savedHP)
                        return false;
                }
            }

            // Use Parry for 3x damage
            if (_character.inventory.itemList.beast1complete && timeTillDamagingAttack <= _globalMoveCooldown)
            {
                return true;
            }
            else
            {
                // Don't use Parry if Block or Paralyze will be off cooldown before next enemy attack
                if (_blockRemainingCooldown < timeTillDamagingAttack && _paralyzeRemainingCooldown < timeTillDamagingAttack)
                    waitForParry = false;

                return waitForParry;
            }
        }

        private bool DoParry()
        {
            var shouldParry = ShouldParry(out var waitForParry);
            // Delay - If we're blocking the next attack (never Parry an attack that can be blocked)
            var delayParry = shouldParry && _willBlockNextAttack;
            // WaitFor - The best time to parry is immediately after the final blocked attack occurs
            waitForParry &= (delayParry || (shouldParry && (_parryRemainingCooldown + 0.05f) < _combatSnapshot.TimeTillNextDamagingAttack)) && _parryRemainingCooldown < _globalMoveCooldown && _willOnlyBlockNextAttack && _combatSnapshot.TimeTillNextDamagingAttack < _globalMoveCooldown;

            // Parry works best when staggered with Block and cast when Block will not be able to cover the next attack
            // Parry does next to nothing when active during a blocked attack, but it halves incoming damage from an unblocked attack
            // While not as impactful as Block, weaving with Block will stretch out the coverage of defensive buffs as much as possible
            if (shouldParry && !delayParry)
            {
                if (CastParry())
                    return true;
            }

            // Defensive cooldowns are critical to staying alive, its worth delaying combat for fractions of a second to optimize uptime
            return waitForParry;
        }

        private bool CheckFatalBlow()
        {
            if (_combatSnapshot.TimeTillNextDamagingAttack >= _globalMoveCooldown + 0.05f)
                return false;

            float HP = _character.adventure.curHP;
            var damage = IncomingDamage(_blockTimeLeft > _combatSnapshot.TimeTillAttack, ParryActive(), ChargeActive());

            // Enemy attack would be fatal
            if (damage >= HP)
            {
                var damageWithBlock = IncomingDamage(true, ParryActive(), ChargeActive());
                var damageWithParry = IncomingDamage(_blockTimeLeft > _combatSnapshot.TimeTillAttack, true, ChargeActive());
                var damageWithCharge = IncomingDamage(_blockTimeLeft > _combatSnapshot.TimeTillAttack, ParryActive(), true);

                if (_combatSnapshot.HoldBlock || _blockRemainingCooldown > _combatSnapshot.TimeTillNextDamagingAttack - 0.05f)
                {
                    // First thing to do is to postpone enemy attack to wait for Block
                    if (DoParalyze())
                        return true;

                    // Second thing to do is to disable Beast Mode
                    if (BeastModeActive() && BeastModeCooldown() < _combatSnapshot.TimeTillNextDamagingAttack - 0.05f)
                    {
                        CastBeastMode();
                        return true; // Button might be still not interactable due to a bug
                    }

                    // Parry is another defensive move
                    if (damageWithParry < HP && DoParry())
                        return true;

                    // The blow would be fatal anyway, might as well block it no matter what
                    // if (_combatSnapshot.TimeTillNextDamagingAttack <= _globalMoveCooldown + 0.05f)
                    _combatSnapshot.HoldBlock = false;
                }

                if (damageWithBlock < HP)
                {
                    if (DoBlock())
                        return true;
                }
                else
                {
                    if (DoParalyze())
                        return true;

                    // Charge can help us if Block or Parry is active
                    if (damageWithCharge < HP && _combatSnapshot.TimeTillNextDamagingAttack < _globalMoveCooldown + 0.05f && CastCharge())
                        return true;
                }
            }
            return false;
        }

        private bool DefensiveRoutine()
        {
            // Each defensive move is broken down into 3 logic checks:
            //  1) Should - Do we want to use the move?
            //  2) Delay - Do we want to use the move, but the timing isn't right? If so delay usage.
            //  3) WaitFor - Do we need to delay, or do we want to use the move and it will come off cooldown before the next enemy attack? If so, is the remaining cooldown less than the global move cooldown?
            //               If all of those conditions are met and it is not the best time to use the move, then don't allow another action to take place until we can use the move.

            // In offensive mode use Parry before other moves
            if (_combatMode == 3)
            {
                // Casting paralyze delays enemy paralyze
                if (_enemy.AI == AI.paralyze && CastParalyze(_useOhShitInsteadOfParalyze))
                    return true;

                if (DoParry())
                    return true;

                // Offensive combat mode is intended for fast combat
                return false;
            }

            if (CheckFatalBlow())
                return true;

            if (_prioritizeParalyze && DoParalyze())
                return true;

            if (DoBlock())
                return true;

            if (!_prioritizeParalyze && DoParalyze())
                return true;

            if (CheckDisableBeastMode())
                return true;

            if (DoParry())
                return true;

            return false;
        }

        private bool CheckDisableBeastMode()
        {
            if (!BeastModeActive() || !BeastModeAvailable() || BeastModeCooldown() > 0f)
                return false;

            // Don't disable Beast Mode in offensive mode
            if (_combatMode >= 3)
                return false;

            // If we still have enough time before an enemy attack, don't turn off Beast Mode
            var time = _combatSnapshot.TimeTillNextDamagingAttack;
            var block = _blockTimeLeft > time;
            if (!block && _blockRemainingCooldown < time - 0.05f)
            {
                block = true;
                time -= _globalMoveCooldown + 0.05f;
            }
            if (time > _globalMoveCooldown + 0.05f)
                return false;

            float HP = GetHPPercentage() - IncomingDamage(block, ParryActive(), ChargeActive()) / _ac.maxHP();
            var tooLowHP = false;
            if (_combatMode == 1 && HP < 0.7f)
                tooLowHP = true;
            else if (_combatMode == 2 && HP < 0.6f)
                tooLowHP = true;

            // Disable Beast Mode if HP is too low
            if (tooLowHP)
            {
                CastBeastMode();
                return true; // Button might be still not interactable due to a bug
            }

            // Only do following checks for sniping
            if (_combatMode >= 2)
                return false;

            float blockDuration = BlockDuration();
            if (_blockRemainingCooldown < _combatSnapshot.TimeTillNextDamagingAttack - 0.05f)
                blockDuration = _combatSnapshot.TimeTillNextDamagingAttack + _character.blockDuration();
            float defBuffDuration = DefensiveBuffReady() ? _character.defenseBuffDuration() : DefensiveBuffDuration();
            float ultBuffDuration = UltimateBuffReady() ? _character.ultimateBuffDuration() : UltimateBuffDuration();
            // When sniping we want both Buffs at once
            float buffDuration = Mathf.Max(blockDuration, Mathf.Min(defBuffDuration, ultBuffDuration));

            if (buffDuration > 0f)
            {
                float ultRemainingCooldown = UltimateAttackCooldown();
                var canCastUltimate = ultRemainingCooldown < buffDuration;

                // Turn off Beast Mode if we wont be able to use Piercing or Ultimate attacks before the buffs expire
                if (_combatMode == 1 && !canCastUltimate)
                {
                    CastBeastMode();
                    return true; // Button might be still not interactable due to a bug
                }
            }
            // Turn off Beast Mode if a defensive buff is not active and the enemy will attack soon
            else if (_combatSnapshot.TimeTillNextDamagingAttack <= _globalMoveCooldown + 0.05f)
            {
                CastBeastMode();
                return true; // Button might be still not interactable due to a bug
            }

            return false;
        }

        private bool CheckEnableBeastMode()
        {
            if (BeastModeActive() || !BeastModeAvailable() || BeastModeCooldown() > 0f)
                return false;

            // Honor the per-context beast setting (user-reported death loop: the titan advisor set
            // TitanBeastMode=false but offensive mode force-cast it anyway — this AI rule silently
            // outranked every setting and toggle in the app).
            if (!CombatManager.DesiredBeastMode())
                return false;

            // Always run Beast Mode in offensive mode when possible
            if (_combatMode >= 3)
            {
                CastBeastMode();
                return true; // The button might be still not interactable due to a glitch
            }

            // Only enable Beast Mode if it is not active already and we have enough time before enemy attack
            var shouldEnable = !BeastModeActive() && _combatSnapshot.TimeTillNextDamagingAttack >= _globalMoveCooldown + 0.05f;
            // Only enable Beast Mode if we have enough cooldown reduction
            shouldEnable &= _character.totalCooldownBonus() <= 1.01f / 1.5f;
            // Block must be strong enough
            shouldEnable &= _character.advancedTrainingController.block.blockBonus(0) <= 0.01f;
            // Check that Block and Paralyze will be available
            shouldEnable &= _blockRemainingCooldown <= _combatSnapshot.TimeTillAttack && ParalyzeCooldown() <= 3f + _combatSnapshot.TimeTillAttack;
            // Special logic for Godmother due to knee cap
            shouldEnable &= !ZoneHelpers.ZoneIsGodmother(_zone) || _combatSnapshot.NextAttackSpecialMove;

            if (shouldEnable)
            {
                CastBeastMode();
                return true; // The button might be still not interactable due to a glitch
            }

            float HP = GetHPPercentage();
            var tooLowHP = false;
            if (_combatMode == 1 && HP < 0.8f)
                tooLowHP = true;
            else if (_combatMode == 2 && HP < 0.7f)
                tooLowHP = true;

            // Do not enable Beast Mode when HP is too low
            if (tooLowHP)
                return false;

            float buffDuration;
            if (_combatMode == 1)
                // When sniping we want both buffs at once
                buffDuration = Mathf.Max(BlockDuration(), Mathf.Min(DefensiveBuffDuration(), UltimateBuffDuration()));
            else
                buffDuration = Mathf.Max(BlockDuration(), DefensiveBuffDuration(), UltimateBuffDuration());

            // Only enable Beast Mode if not sniping or we can turn Beast Mode off before buffs run out
            shouldEnable = _combatMode > 1 || _character.beastModeCooldown() <= buffDuration;
            // Only enable Beast Mode if we have active buffs or block
            shouldEnable &= buffDuration > 0f;
            // Only enable Beast Mode if we can use Ultimate Attack before buffs expire
            shouldEnable &= UltimateBuffAvailable() && UltimateAttackCooldown() < buffDuration;

            if (shouldEnable)
            {
                CastBeastMode();
                return true; // The button might be still not interactable due to a glitch
            }

            return false;
        }

        private void CombatAttacks(bool bannedMove = false)
        {
            // "Walderp says" is covered in WalderpPreCombat()
            if (_combatSnapshot.ForcedMove.HasValue)
                return;

            if (_combatSnapshot.BannedMove.HasValue && !bannedMove)
                return;

            // Charge waste check
            if (!bannedMove && ChargeActive() && _combatMode <= 3)
            {
                float ultimateAttackCooldown = UltimateAttackCooldown();

                // Do not waste Charge on weak attacks
                bool willWasteCharge = !UltimateAttackReady();
                willWasteCharge &= UltimateAttackAvailable() && ultimateAttackCooldown <= _globalMoveCooldown * 3f;
                // Only wait for Ultimate Attack if we don't need to block a special attack
                willWasteCharge &= ultimateAttackCooldown < _combatSnapshot.TimeTillSpecialMove;
                willWasteCharge &= _blockRemainingCooldown < _combatSnapshot.TimeTillSpecialMove - 0.05f;
                // Do not waste Charge on weak attacks
                if (willWasteCharge)
                    return;

                // Just let Charge be consumed by Parry
                willWasteCharge = ParryActive() && !_willBlockNextAttack && _character.inventory.itemList.beast1complete;
                // But don't wait too long for this
                willWasteCharge &= _combatSnapshot.TimeTillNextDamagingAttack <= _globalMoveCooldown + 0.05f;

                if (willWasteCharge)
                    return;
            }

            float attackMultiplier = _pc.baseDamage() * _pc.offenseBuffFactor * _pc.offenseDebuffFactor * _pc.chargeFactor * 0.8f;
            float oneShotDamage = _enemy.curHP + _enemy.defense / 2;

            var gain = 0f;
            var loss = 0f;
            var newGain = 0f;

            // Check if Regular Attack is available
            bool canRegAttack = RegularAttackAvailable();
            // Walderp check
            canRegAttack &= _combatSnapshot.BannedMove != CombatSnapshot.WalderpCombatMove.Regular;
            if (canRegAttack)
                // Refresh best potential attack
                gain = _character.regAttackPower() * (_globalMoveCooldown - RegularAttackCooldown());

            // Check if Regular Attack is ready
            canRegAttack &= RegularAttackReady();
            if (canRegAttack)
            {
                // If Regular Attack one-shots the enemy, use it
                if (oneShotDamage <= attackMultiplier * _character.regAttackPower())
                {
                    _ac.regularAttackMove.doMove();
                    return;
                }
                // Refresh best actual attack
                if (gain > loss)
                    loss = gain;
            }

            // Check if Strong Attack is available
            bool canStrongAttack = StrongAttackAvailable();
            // Walderp check
            canStrongAttack &= _combatSnapshot.BannedMove != CombatSnapshot.WalderpCombatMove.Strong;
            // Should keep Strong Attack for Walderp
            if (!bannedMove && ZoneHelpers.ZoneIsWalderp(_zone))
                canStrongAttack &= _combatSnapshot.TimeTillSpecialMove > Mathf.Max(_character.strongAttackCooldown(), _globalMoveCooldown) + 0.05f;
            if (canStrongAttack)
            {
                // Refresh best potential attack
                newGain = _character.strongAttackPower() * (_globalMoveCooldown - StrongAttackCooldown());
                if (newGain > gain)
                    gain = newGain;
            }

            // Check if Strong Attack is ready
            canStrongAttack &= StrongAttackReady();
            if (canStrongAttack)
            {
                // If Strong Attack one-shots the enemy, use it
                if (oneShotDamage <= attackMultiplier * _character.strongAttackPower())
                {
                    _ac.strongAttackMove.doMove();
                    return;
                }
                // Refresh best actual attack
                if (newGain > loss)
                    loss = newGain;
            }

            // Check if Piercing Attack is available
            bool canPierce = PiercingAttackAvailable();
            // Walderp check
            canPierce &= _combatSnapshot.BannedMove != CombatSnapshot.WalderpCombatMove.Piercing;
            // Should keep Piercing Attack for Walderp
            if (!bannedMove && ZoneHelpers.ZoneIsWalderp(_zone))
                canPierce &= _combatSnapshot.TimeTillSpecialMove > Mathf.Max(_character.pierceAttackCooldown(), _globalMoveCooldown) + 0.05f;
            if (canPierce)
            {
                // Refresh best potential attack
                newGain = _character.strongAttackPower() * (_globalMoveCooldown - PiercingAttackCooldown());
                if (newGain > gain)
                    gain = newGain;
            }

            // Check if Piercing Attack is ready
            canPierce &= PiercingAttackReady();
            if (canPierce)
            {
                // If Piercing Attack one-shots the enemy, use it
                if (_enemy.curHP + _enemy.defense / 3 <= attackMultiplier * _character.strongAttackPower())
                {
                    _ac.pierceMove.doMove();
                    return;
                }
                // Refresh best actual attack
                if (newGain > loss)
                    loss = newGain;
            }

            // Check if Ultimate Attack is available
            bool canUltimate = UltimateAttackAvailable();
            // Walderp check
            canUltimate &= _combatSnapshot.BannedMove != CombatSnapshot.WalderpCombatMove.Ultimate;
            // Don't keep Ultimate Attack for Walderp, there is only 1 in 8 chance we will need it
            if (canUltimate)
                // Refresh best potential attack
                newGain = _character.ultimateAttackPower() * (_globalMoveCooldown - UltimateAttackCooldown());

            // Check if Ultimate Attack is ready
            if (canUltimate && UltimateAttackReady())
            {
                // In one-shot mode use Ultimate Attack whenever possible (used in ITOPOD)
                var shouldUltimate = _combatMode >= 4;
                // Always use Ultimate Attack when Charge is active
                shouldUltimate |= ChargeActive();
                // Wait for Charge if Charge remaining cooldown is short enough
                var waitForCharge = !shouldUltimate && ChargeCooldown() <= (_character.ultimateAttackCooldown() - _globalMoveCooldown) / _character.chargePower();
                // But don't wait for Charge if buffs will expire
                waitForCharge &= !OffensiveBuffActive() || OffensiveBuffDuration() - _globalMoveCooldown - 0.05f > ChargeCooldown();
                waitForCharge &= !UltimateBuffActive() || UltimateBuffDuration() - _globalMoveCooldown - 0.05f > ChargeCooldown();
                if (!waitForCharge)
                {
                    _ac.ultimateAttackMove.doMove();
                    return;
                }
                // Don't have to refresh best actual attack, because there is not stronger attack
            }
            else if (newGain > gain)
            {
                // Only wait for Ultimate Attack if not waiting for Charge
                gain = newGain;
            }

            // It's optimal to wait for the stronger attack
            if (_combatSnapshot.TimeTillNextDamagingAttack - _combatSnapshot.OptimalTimeToBlock > _globalMoveCooldown * 2f && gain > loss)
                return;

            if (canPierce)
                _ac.pierceMove.doMove();
            else if (canStrongAttack)
                _ac.strongAttackMove.doMove();
            else if (canRegAttack)
                _ac.regularAttackMove.doMove();
        }
    }
}
