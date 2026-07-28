using System;

namespace NGUAdvisor.Managers
{
    // Route C3 Phase 0c: heuristic progression-stage detection. Maps the player's rebirth difficulty
    // (normal/evil/sadistic) + highest boss onto the community guide's 8 chapters, to drive a "recommended
    // for your stage" hint and the status HUD's "what you're working toward" line. This is a HINT only — it
    // never changes anything automatically. Thresholds are approximate and intentionally easy to tune (see
    // docs/NGU-KNOWLEDGE.md). All game reads are guarded so a not-yet-ready game returns Unknown.
    //
    // TWO CHAPTER ENGINES — this Chapter is NOT interchangeable with ProgressionAnalyzer.Chapter. This one is
    // the coarse BOSS-THRESHOLD heuristic: chapter is a function of difficulty + highestBoss only. Its Evil
    // boundaries are deliberately tuned to boss milestones (chapter stays 5 through Boss 166 so the boss-gated
    // EVIL CLIMB / AUGMENTATION segment logic in ChallengeOverlay and the LevelPlanner NGU-track switch stay
    // anchored) — those two consumers NEED a boss-anchored chapter, which is why this engine is retained. For
    // everything else (HUD stage, perk plan, profile recommendation) the canonical chapter is
    // ProgressionAnalyzer.Chapter, which reads actual TITAN KILLS. The two intentionally DIVERGE whenever boss
    // progress runs ahead of titan kills (e.g. Evil Boss 200 with T7 unbeaten: here=6 via boss<250, there=5 via
    // t7-not-beaten). That gap is EXPECTED, not a bug — never swap one Chapter for the other.
    public static class StageDetector
    {
        public struct Stage
        {
            public bool Known;
            public int Chapter;            // 1..8, 0 = unknown. Boss-threshold chapter (see class note) — NOT ProgressionAnalyzer.Chapter (titan-kill).
            public string Label;           // e.g. "Ch.3 T4-BAE"
            public string Difficulty;      // "Normal" / "Evil" / "Sadistic"
            public string SuggestedProfile;// non-binding profile-name hint
            public string Goal;            // short "what you're working toward" for this chapter
        }

        private static readonly Stage Unknown = new Stage
        {
            Known = false, Chapter = 0, Label = "Stage —", Difficulty = "", SuggestedProfile = "", Goal = ""
        };

        // Short per-chapter objective for the status HUD's "toward" line.
        private static string GoalFor(int chapter)
        {
            switch (chapter)
            {
                case 1: return "Kill Titan 1 (~1350 P/T)";
                case 2: return "Boss 100 → T4, farm Mega";
                case 3: return "T5/BAE, farm beards";
                case 4: return "T6 weapon, chocolate gear";
                case 5: return "Boss 125 → T7, start evil NGUs";
                case 6: return "T8, buy R3, Typo/Fad/JRPG";
                case 7: return "Boss 300, 24 AK kills, Rad set";
                case 8: return "Sadistic: fertilizer + muffins";
                default: return "";
            }
        }

        // Boss thresholds per difficulty band (approximate; guide milestones: B58≈T1, B100→T4 start,
        // B125→T7 push, B300 late T9). Refine with titan-version reads later if needed.
        public static Stage Detect()
        {
            try
            {
                var c = Main.Character;
                if (c == null || c.settings == null) return Unknown;

                int boss = ZoneHelpers.CurrentHighestBoss(c);
                var diff = c.settings.rebirthDifficulty;

                // SuggestedProfile points at an auto-installed Goal-* preset (Managers/PresetInstaller).
                if (diff == difficulty.sadistic)
                    return Make(8, "Sadistic", "Sadistic", "Goal-NGU");

                if (diff == difficulty.evil)
                {
                    // Ch.5 Evil-IDP spans the whole Evil re-climb THROUGH the IDP/T8 unlock (Boss 166) — the
                    // old <150 boundary cut it short. Sub-stage label tracks the guide's re-unlock ladder:
                    // climb → EV(58) → PPPL(100) → T7(125) → Meta(158) → IDP(166). Chapter stays 5 so the
                    // chapter-gated segment logic (EVIL CLIMB / AUGMENTATION) is unaffected.
                    if (boss < 166)
                    {
                        string sub = boss < 58 ? "climb" : boss < 100 ? "EV" : boss < 125 ? "PPPL"
                            : boss < 158 ? "T7" : "Meta/IDP";
                        return Make(5, $"Evil-IDP {sub}", "Evil", "Goal-NGU");
                    }
                    if (boss < 250) return Make(6, "T8-JRPG", "Evil", "Goal-NGU");
                    return Make(7, "T9", "Evil", "Goal-NGU");
                }

                // Normal difficulty
                if (boss < 58) return Make(1, "Start-HSB", "Normal", "Goal-Adventure");
                if (boss < 100) return Make(2, "T1-Mega", "Normal", "Goal-Adventure");
                if (boss < 129) return Make(3, "T4-BAE", "Normal", "Goal-AdvDC");
                return Make(4, "T6", "Normal", "Goal-AdvDC");
            }
            catch (Exception e)
            {
                Main.LogDebug($"StageDetector failed: {e.Message}");
                return Unknown;
            }
        }

        private static Stage Make(int chapter, string name, string diff, string profile) => new Stage
        {
            Known = true,
            Chapter = chapter,
            Label = $"Ch.{chapter} {name}",
            Difficulty = diff,
            SuggestedProfile = profile,
            Goal = GoalFor(chapter)
        };
    }
}
