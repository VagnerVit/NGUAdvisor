using System;
using System.Collections.Generic;
using System.Linq;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor.AllocationProfiles.RebirthStuff
{
    public static class BaseRebirth
    {
        private static readonly Character _character = Main.Character;
        private static RCTarget[] _challenges;

        private class RCTarget
        {
            private readonly string name;
            private readonly int index;
            private readonly string engage;
            private readonly int maxCompletions;
            private readonly Func<int> curCompletions;

            private RCTarget(string name, int index, string engage, int maxCompletions, Func<int> curCompletions)
            {
                this.name = name;
                this.index = index;
                this.maxCompletions = maxCompletions;
                this.curCompletions = curCompletions;
                this.engage = engage;
            }

            public static RCTarget CreateInstance(string name, int index)
            {
                var cc = _character.allChallenges;
                switch (name)
                {
                    case "BASIC":
                        return new RCTarget("Basic Challenge", index, "engageBasicChallenge",
                            cc.basicChallenge.maxCompletions, () => cc.basicChallenge.currentCompletions());
                    case "NOAUG":
                        return new RCTarget("No Augs Challenge", index, "engageNoAugsChallenge",
                            cc.noAugsChallenge.maxCompletions, () => cc.noAugsChallenge.currentCompletions());
                    case "24HR":
                        return new RCTarget("24 Hour Challenge", index, "engage24HourChallenge",
                            cc.hour24Challenge.maxCompletions, () => cc.hour24Challenge.currentCompletions());
                    case "100LC":
                        return new RCTarget("100 Level Challenge", index, "engagelevel100Challenge",
                            cc.level100Challenge.maxCompletions, () => cc.level100Challenge.currentCompletions());
                    case "NOEC":
                        return new RCTarget("No Equipment Challenge", index, "engageNoEquipChallenge",
                            cc.noEquipmentChallenge.maxCompletions, () => cc.noEquipmentChallenge.currentCompletions());
                    case "TC":
                        return new RCTarget("Troll Challenge", index, "engageTrollChallenge",
                            cc.trollChallenge.maxCompletions, () => cc.trollChallenge.currentCompletions());
                    case "NORB":
                        return new RCTarget("No Rebirth Challenge", index, "engageNoRebirthChallenge",
                            cc.noRebirthChallenge.maxCompletions, () => cc.noRebirthChallenge.currentCompletions());
                    case "LSC":
                        return new RCTarget("Laser Sword Challenge", index, "engageLaserSwordChallenge",
                            cc.laserSwordChallenge.maxCompletions, () => cc.laserSwordChallenge.currentCompletions());
                    case "BLIND":
                        return new RCTarget("Blind Challenge", index, "engageBlindChallenge",
                            cc.blindChallenge.maxCompletions, () => cc.blindChallenge.currentCompletions());
                    case "NONGU":
                        return new RCTarget("No NGU Challenge", index, "engageNGUChallenge",
                            cc.NGUChallenge.maxCompletions, () => cc.NGUChallenge.currentCompletions());
                    case "NOTM":
                        return new RCTarget("No TM Challenge", index, "engageTimeMachineChallenge",
                            cc.timeMachineChallenge.maxCompletions, () => cc.timeMachineChallenge.currentCompletions());
                }
                Log($"Incorrect challenge name: {name}");
                return null;
            }

            public bool ChallengeValid() => index <= maxCompletions && index == curCompletions() + 1;

            public bool Engage()
            {
                Log($"Rebirthing into {name}");
                _character.rebirth.CallMethod(engage);
                return true;
            }
        }

        public static void EngageRebirth()
        {
            Log("Rebirthing");
            _character.rebirth.CallMethod("engage");
        }

        public static void ParseChallenges(string[] challenges)
        {
            if (challenges == null)
            {
                _challenges = null;
                return;
            }

            var parsed = new List<RCTarget>();
            foreach (string c in challenges.Select(x => x.ToUpper()))
            {
                if (!c.Contains("-"))
                    continue;

                var split = c.Split('-');
                var challenge = split[0].ToUpper();

                if (!int.TryParse(split[1], out var index))
                    continue;

                var rc = RCTarget.CreateInstance(challenge, index);
                if (rc != null)
                    parsed.Add(rc);
            }

            _challenges = parsed.ToArray();
        }

        public static bool AnyChallengesValid() => _challenges?.Any(x => x.ChallengeValid()) ?? false;

        public static bool TryStartChallenge()
        {
            if (_challenges?.FirstOrDefault(x => x.ChallengeValid())?.Engage() ?? false)
                return true;
            return TryStartAdvisorLsc();
        }

        // Advisor opportunity (user-reported: LSC was recommended but the rebirth went into a plain
        // run — the recommendation was display-only). LSC doesn't reset the number and its condition
        // fits inside the augs window, so a SCHEDULED rebirth enters it instead of a plain run.
        // Profile-scheduled challenges keep precedence, and rebirth timing is untouched — this never
        // fires a rebirth early (AnyChallengesValid doesn't consult it). Gated on the challenge advisor.
        private static bool TryStartAdvisorLsc()
        {
            try
            {
                if (Settings == null || !Settings.AdvisorChallenges) return false;
                var v = LscAdvisor.Compute();
                if (!v.Known || !v.Recommended) return false;
                Log($"Rebirthing into Laser Sword Challenge — advisor: ~{Math.Max(1, v.EstMinutes):0}m to laser sword lv {v.Target}, number is not reset");
                ChallengeOverlay.Record("SEGMENT", "rebirth → LSC", $"advisor opportunity: ~{Math.Max(1, v.EstMinutes):0}m to laser sword lv {v.Target}");
                _character.rebirth.CallMethod("engageLaserSwordChallenge");
                return true;
            }
            catch (Exception e)
            {
                Main.LogDebug($"TryStartAdvisorLsc: {e.Message}");
                return false;
            }
        }

        public static bool RebirthAvailable()
        {
            if (!Settings.AutoRebirth)
                return false;

            // Currently busy with some task, can't rebirth
            if (!LockManager.CanSwap())
                return false;

            if (_character.rebirthTime.totalseconds < _character.rebirth.minRebirthTime() || _character.challenges.noRebirthChallenge.inChallenge)
                return false;

            return true;
        }

        public static bool DoRebirth()
        {
            if (PreRebirth())
                return false;

            if (!_character.challenges.inChallenge && TryStartChallenge())
                return true;

            EngageRebirth();
            return true;
        }

        public static bool PreRebirth()
        {
            bool delay = false;

            if (Settings.ManageYggdrasil && YggdrasilManager.AnyHarvestable())
            {
                if (LockManager.TryYggdrasilSwap(true))
                {
                    YggdrasilManager.HarvestAll(true);
                    Log("Delaying rebirth 1 loop to allow fruit effects");
                }
                else
                {
                    Log("Delaying rebirth to wait for Yggdrasil configuration");
                }
                delay = true;
            }

            if (ZoneHelpers.AnyTitansSpawningSoon())
            {
                Log("Delaying rebirth to kill Titans");
                delay = true;
            }

            DiggerManager.UpgradeCheapestDigger();

            if (CastBloodSpellsForRebirth())
            {
                Log("Delaying rebirth to update number spell multiplier");
                delay = true;
            }

            // To prevent delaying rebirth arbitrarily
            if (delay)
            {
                _character.removeAllEnergy();
                _character.removeAllMagic();
                _character.removeAllRes3();
            }
            
            return delay;
        }

        private static bool CastBloodSpellsForRebirth()
        {
            if (Settings.CastBloodSpells)
            {
                BloodMagicManager.guffB.Cast(true);
                BloodMagicManager.guffA.Cast(true);
                BloodMagicManager.ironPill.Cast(true);
            }

            // Use whatever blood we have left on blood number before rebirthing
            if (_character.bloodMagic.bloodPoints > 0)
            {
                Log($"Casting number blood spell with remaining {_character.bloodMagic.bloodPoints} blood before rebirth");
                _character.bloodSpells.castRebirthSpell();
                // Number spell requires at least one frame to increase the number, we need to wait before rebirth
                return true;
            }

            return false;
        }
    }
}
