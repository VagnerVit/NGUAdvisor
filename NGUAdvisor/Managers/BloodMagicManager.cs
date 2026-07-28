using System;
using static NGUAdvisor.Main;

namespace NGUAdvisor.Managers
{
    public static class BloodMagicManager
    {
        public const double PillWorthFraction = 0.10;      // min pill effect as a fraction of base adventure power
        public const double PillMinAvailableSec = 1800.0;  // 30-min availability hold before a cast is allowed

        public abstract class Spell
        {
            protected string name;
            protected int cooldown;
            protected double minBlood;

            protected abstract bool Unlocked { get; }

            protected abstract double Time { get; }

            protected abstract double Threshold { get; }

            protected abstract bool CastOnRebirth { get; }

            protected Spell(string name, int cooldown, double minBlood)
            {
                this.name = name;
                this.cooldown = cooldown;
                this.minBlood = minBlood;
            }

            protected abstract double Effect(double bloodPoints);

            protected abstract void CastSpell();

            // Per-spell fail-safe evaluated right before casting, on top of the shared unlock/cooldown/
            // blood/threshold checks. Default: never holds. Returns true to HOLD (skip) the cast and sets
            // reason for logging. IronPill overrides this.
            protected virtual bool FailSafeHold(double effect, out string reason) { reason = null; return false; }

            public void Cast(bool rebirth = false)
            {
                if (!Settings.CastBloodSpells)
                    return;

                if (!Unlocked)
                    return;

                var forced = rebirth && CastOnRebirth;
                double threshold = Threshold;
                if (!forced && threshold < 1.0)
                    return;

                if (Time < cooldown)
                {
                    // Normal during rebirth prep (the prep loop retries) — debug only, the old Log
                    // line spammed once per attempt.
                    if (forced)
                        Main.LogDebug($"Blood Spell {name} skipped on rebirth - on cooldown");
                    return;
                }

                double bloodPoints = _character.bloodMagic.bloodPoints;
                if (bloodPoints < minBlood)
                {
                    if (forced || Time < cooldown + 10f)
                        Log($"Casting Failed: Blood Spell {name} - Below minimum blood threshold of {minBlood:F0}");
                    return;
                }

                double effect = Effect(bloodPoints);
                if (!forced && threshold.CompareTo(effect) > 0)
                {
                    if (Time < cooldown + 10f)
                        Log($"Casting Failed: Blood Spell {name} - Below configured power threshold ({effect:F0} of {threshold:F0}) and not force cast on rebirth");
                    return;
                }

                // Per-spell fail-safe (advisor-only; skipped on a forced rebirth cast, which is use-it-or-lose-it).
                if (!forced && FailSafeHold(effect, out var holdReason))
                {
                    if (Time < cooldown + 10f)
                        Log($"Casting Failed: Blood Spell {name} - {holdReason}");
                    return;
                }

                CastSpell();
                Log($"Casting Blood Spell {name} @ {effect:F0} power");
            }

            // Planner-driven cast (BloodPlanner decided the timing): safety checks only — unlock,
            // cooldown, minimum blood — no user threshold involved.
            public bool CastPlanned()
            {
                if (!Unlocked || Time < cooldown)
                    return false;
                double bloodPoints = _character.bloodMagic.bloodPoints;
                if (bloodPoints < minBlood)
                    return false;
                double effect = Effect(bloodPoints);
                if (FailSafeHold(effect, out var holdReason))
                {
                    Main.LogDebug($"Blood Spell {name} held (planner): {holdReason}");
                    return false;
                }
                CastSpell();
                Log($"Casting Blood Spell {name} @ {effect:F0} power (planner)");
                return true;
            }
        }

        public class IronPill : Spell
        {
            protected override bool Unlocked => true;

            protected override double Time => _character.bloodMagic.adventureSpellTime.totalseconds;

            protected override double Threshold => Settings.IronPillThreshold;

            protected override bool CastOnRebirth => Settings.IronPillOnRebirth;

            public IronPill(string name, int cooldown, double minBlood) : base(name, cooldown, minBlood) { }

            protected override double Effect(double bloodPoints)
            {
                var result = Math.Floor(Math.Pow(bloodPoints, 0.25));
                if (_character.settings.rebirthDifficulty >= difficulty.evil)
                    result *= _character.adventureController.itopod.ironPillBonus();
                return result;
            }

            protected override void CastSpell() => _bloodSpells.castAdventurePowerupSpell();

            // Advisor-only fail-safes (the on-rebirth forced cast bypasses these):
            //  1. Do not fire within the first 30 minutes the pill is available (past its cooldown), so
            //     blood keeps pooling into a stronger pill instead of firing the moment it comes ready.
            //  2. Do not fire for a gain under 10% of the current BASE adventure power (adventure.attack --
            //     the same base-stat yardstick BloodPlanner measures pill worth against).
            protected override bool FailSafeHold(double effect, out string reason)
            {
                double availableFor = Time - cooldown;
                if (availableFor < PillMinAvailableSec)
                {
                    reason = $"available {Math.Max(0, availableFor) / 60.0:F0}m (< 30m fail-safe)";
                    return true;
                }
                double baseAdvPower = Math.Max(1.0, _character.adventure.attack);
                if (effect < baseAdvPower * PillWorthFraction)
                {
                    reason = $"gain {effect:F0} < 10% of base adv power {baseAdvPower:F0}";
                    return true;
                }
                reason = null;
                return false;
            }
        }

        public class GuffA : Spell
        {
            protected override bool Unlocked => _character.adventure.itopod.perkLevel[72] >= 1L;

            protected override double Time => _character.bloodMagic.macguffin1Time.totalseconds;

            protected override double Threshold => Settings.BloodMacGuffinAThreshold;

            protected override bool CastOnRebirth => Settings.BloodMacGuffinAOnRebirth;

            public GuffA(string name, int cooldown, double minBlood) : base(name, cooldown, minBlood) { }

            protected override double Effect(double bloodPoints)
            {
                var result = Math.Log(bloodPoints / _bloodSpells.minMacguffin1Blood(), 10.0) + 1.0;
                result *= _character.wishesController.totalBloodGuffbonus();
                return Math.Floor(result);
            }

            protected override void CastSpell() => _bloodSpells.castMacguffin1Spell();
        }

        public class GuffB : Spell
        {
            protected override bool Unlocked => _character.adventure.itopod.perkLevel[73] >= 1L && _character.settings.rebirthDifficulty >= difficulty.evil;

            protected override double Time => _character.bloodMagic.macguffin2Time.totalseconds;

            protected override double Threshold => Settings.BloodMacGuffinBThreshold;

            protected override bool CastOnRebirth => Settings.BloodMacGuffinBOnRebirth;

            public GuffB(string name, int cooldown, double minBlood) : base(name, cooldown, minBlood) { }

            protected override double Effect(double bloodPoints) => Math.Floor(Math.Log(bloodPoints / _bloodSpells.minMacguffin2Blood(), 20.0) + 1.0);

            protected override void CastSpell() => _bloodSpells.castMacguffin2Spell();
        }

        private static readonly Character _character = Main.Character;
        private static readonly RebirthPowerSpell _bloodSpells = _character.bloodSpells;

        public static readonly Spell ironPill = new IronPill("Iron Pill", _bloodSpells.adventureSpellCooldown, _bloodSpells.minAdventureBlood());
        public static readonly Spell guffA = new GuffA("MacGuffin α", _bloodSpells.macguffin1Cooldown, _bloodSpells.minMacguffin1Blood());
        public static readonly Spell guffB = new GuffB("MacGuffin β", _bloodSpells.macguffin2Cooldown, _bloodSpells.minMacguffin2Blood());
    }
}
