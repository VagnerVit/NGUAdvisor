using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // Detects which rebirth Challenge is currently active (for challenge-aware loadout/allocation swapping
    // inside a challenge block) and supplies built-in "smart default" gear objectives per challenge.
    //
    // Challenge codes match the profile "Challenges" list vocabulary (see BaseRebirth.RCTarget):
    // BASIC, NOAUG, 24HR, 100LC, NOEC, TC, NORB, LSC, BLIND, NONGU, NOTM. Only one challenge is active
    // at a time. All reads are on the main thread (called from DoAllocations) and guarded.
    public static class ChallengeDetector
    {
        public class GearDefault
        {
            public readonly string Objective;
            public readonly bool ForceRespawn;
            public GearDefault(string objective, bool forceRespawn) { Objective = objective; ForceRespawn = forceRespawn; }
        }

        // The active challenge code, or null if not currently in any challenge.
        public static string Current()
        {
            try
            {
                var c = Main.Character;
                var cc = c?.challenges;   // type Challenges: Challenge-typed fields, each with .inChallenge
                if (cc == null || !cc.inChallenge) return null;

                if (cc.noRebirthChallenge.inChallenge) return "NORB";
                if (cc.timeMachineChallenge.inChallenge) return "NOTM";
                if (cc.noAugsChallenge.inChallenge) return "NOAUG";
                if (cc.nguChallenge.inChallenge) return "NONGU";
                if (cc.blindChallenge.inChallenge) return "BLIND";
                if (cc.trollChallenge.inChallenge) return "TC";
                if (cc.laserSwordChallenge.inChallenge) return "LSC";
                if (cc.noEquipmentChallenge.inChallenge) return "NOEC";
                if (cc.levelChallenge10k.inChallenge) return "100LC";
                if (cc.hour24Challenge.inChallenge) return "24HR";
                if (cc.basicChallenge.inChallenge) return "BASIC";
                return null;
            }
            catch (Exception e)
            {
                Main.LogDebug($"ChallengeDetector.Current failed: {e.Message}");
                return null;
            }
        }

        // Guide ch.5: Evil climbs on resources ("you don't need the adventure stats"), and every phase of
        // its Evil day wears resource gear — so an Evil boss push wears E/M cap + power, never Power/Toughness.
        // Titan fights are not pushes and keep their own Adventure gear (GearOptimizer).
        public const string EvilPushObjective = "Time Machine";

        public static string BossPushObjective()
        {
            try { return Main.Character.settings.rebirthDifficulty == difficulty.evil ? EvilPushObjective : "Adventure"; }
            catch { return "Adventure"; }
        }

        // Guide ch.5's CBlock 3 (rule in ChallengeGates.CBlock3Complete). Reads evilCompletions() and
        // completions() explicitly: currentCompletions() follows the rebirth difficulty, so the Normal
        // rebirth the block itself asks for would read as "not done". Main thread; false on any failed read.
        public static bool CBlock3Complete()
        {
            try
            {
                AllChallengesController ac = Main.Character?.allChallenges;
                if (ac == null) return false;
                Dictionary<string, ChallengeGates.Completions> all = new Dictionary<string, ChallengeGates.Completions>
                {
                    { "BASIC", Of(ac.basicChallenge.evilCompletions(), ac.basicChallenge.completions(), ac.basicChallenge.maxCompletions) },
                    { "NOAUG", Of(ac.noAugsChallenge.evilCompletions(), ac.noAugsChallenge.completions(), ac.noAugsChallenge.maxCompletions) },
                    { "24HR", Of(ac.hour24Challenge.evilCompletions(), ac.hour24Challenge.completions(), ac.hour24Challenge.maxCompletions) },
                    { "100LC", Of(ac.level100Challenge.evilCompletions(), ac.level100Challenge.completions(), ac.level100Challenge.maxCompletions) },
                    { "NOEC", Of(ac.noEquipmentChallenge.evilCompletions(), ac.noEquipmentChallenge.completions(), ac.noEquipmentChallenge.maxCompletions) },
                    { "TC", Of(ac.trollChallenge.evilCompletions(), ac.trollChallenge.completions(), ac.trollChallenge.maxCompletions) },
                    { "NORB", Of(ac.noRebirthChallenge.evilCompletions(), ac.noRebirthChallenge.completions(), ac.noRebirthChallenge.maxCompletions) },
                    { "LSC", Of(ac.laserSwordChallenge.evilCompletions(), ac.laserSwordChallenge.completions(), ac.laserSwordChallenge.maxCompletions) },
                    { "BLIND", Of(ac.blindChallenge.evilCompletions(), ac.blindChallenge.completions(), ac.blindChallenge.maxCompletions) },
                    { "NONGU", Of(ac.NGUChallenge.evilCompletions(), ac.NGUChallenge.completions(), ac.NGUChallenge.maxCompletions) },
                    { "NOTM", Of(ac.timeMachineChallenge.evilCompletions(), ac.timeMachineChallenge.completions(), ac.timeMachineChallenge.maxCompletions) },
                };
                return ChallengeGates.CBlock3Complete(all);
            }
            catch (Exception e)
            {
                Main.LogDebug($"ChallengeDetector.CBlock3Complete failed: {e.Message}");
                return false;
            }
        }

        private static ChallengeGates.Completions Of(int evil, int normal, int max) =>
            new ChallengeGates.Completions { Evil = evil, Normal = normal, Max = max };

        // Built-in gear objective for a challenge when the profile has no challenge-specific gear defined.
        // Most challenges just want to push power (BossPushObjective) + a respawn item; NOTM leans on Gold (no
        // TM); No-Equipment can't wear gear (null). Returns null => fall through to the profile's timeline.
        public static GearDefault DefaultGear(string code)
        {
            string push = BossPushObjective();
            switch (code)
            {
                case "NORB": return new GearDefault(push, true);
                case "NOTM": return new GearDefault("Gold Drops", true);
                case "NOAUG": return new GearDefault(push, true);
                case "NONGU": return new GearDefault(push, true);
                case "BLIND": return new GearDefault(push, true);
                case "TC": return new GearDefault(push, true);
                case "LSC": return new GearDefault(push, true);
                case "100LC": return new GearDefault(push, true);
                case "BASIC": return new GearDefault(push, true);
                // NOEC: no gear allowed. 24HR: normal timeline is fine.
                default: return null;
            }
        }
    }
}
