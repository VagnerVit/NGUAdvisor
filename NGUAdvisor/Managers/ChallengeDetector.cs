using System;

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
