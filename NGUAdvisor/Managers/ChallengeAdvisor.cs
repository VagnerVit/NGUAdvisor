using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // Recommends STARTING a challenge — the one thing no module did before. LscAdvisor/BaseRebirth can
    // enter LSC, and ChallengeOverlay reshapes the allocation once you are already inside one, but
    // nothing ever told the user a challenge was worth running. That silence is expensive: the advisor's
    // own NGU math multiplies by 3 the moment Troll Challenge has a single completion (NGUBP.cs:152,
    // the game's own NGU speed formula), so a player at Troll 0 reads every projection already
    // discounted to a third and is never told why.
    //
    // Advice only — entering a challenge is a rebirth and stays the user's call. The gate table and the
    // ranking live in the Unity-free ChallengeGates.
    public static class ChallengeAdvisor
    {
        public struct Advice
        {
            public bool Known;
            public string Text;
            public int Severity;
        }

        public static Advice Analyze()
        {
            Advice a = new Advice { Known = false, Text = "", Severity = 0 };
            try
            {
                Character c = Main.Character;
                if (c == null) return a;
                // Inside a challenge there is nothing to recommend — finish this one first.
                if (ChallengeDetector.Current() != null) return a;

                Dictionary<string, int> have = new Dictionary<string, int>();
                // A challenge the game has not introduced yet throws on read; a missing key just means
                // "no gate to advise here", which ChallengeGates.Next already treats as skip.
                try { have["TC"] = c.allChallenges.trollChallenge.completions(); } catch (Exception) { }
                try { have["NOAUG"] = c.allChallenges.noAugsChallenge.completions(); } catch (Exception) { }
                if (have.Count == 0) return a;

                ChallengeGates.Gate? next = ChallengeGates.Next(have);
                if (next == null) return a;

                ChallengeGates.Gate g = next.Value;
                a.Known = true;
                // Severity 1 (suggest), never 2 (act), whatever the multiplier: the row cannot be
                // cleared by acting now — it asks for a REBIRTH into a challenge — and a severity-2 row
                // sorts to the top of a slot-limited dashboard and would sit there for weeks, pushing
                // out rows the user can actually do something about this minute.
                a.Severity = 1;
                a.Text = $"Run {g.Name} ({have[g.Key]}/{g.Needed}) — {g.Reward}";
                return a;
            }
            catch (Exception ex) { Main.LogDebug($"ChallengeAdvisor failed: {ex.Message}"); }
            return a;
        }
    }
}
