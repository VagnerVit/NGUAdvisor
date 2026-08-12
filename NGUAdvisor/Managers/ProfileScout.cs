using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NGUAdvisor.AllocationProfiles;
using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using SimpleJSON;

namespace NGUAdvisor.Managers
{
    // Scores the profile FILES ON DISK against what the plan currently wants to feed, so a
    // recommendation can name the user's own profile instead of only ever naming a shipped preset.
    //
    // WHY. ProgressionAnalyzer picked from installed presets only, and said so in a caveat — a user
    // running their own LRB profile read "Recommended: Normal-24hr" as a verdict ON that profile when
    // it had never been considered (2026-08-12). The caveat was a prop; this is the thing it stood in
    // for.
    //
    // Two stages, because neither alone is enough:
    //   1. A HARD FILTER on rebirth style. An LRB profile (nothing ends the run on its own) and a
    //      cadence profile are not interchangeable at any score — recommending the wrong kind is worse
    //      than recommending nothing, so the wrong kind is never ranked, only excluded.
    //   2. A RANKING by how much of the plan's NGU lanes the profile actually funds. The filter alone
    //      cannot choose: the user has FOUR profiles with no auto-rebirth (LRB, LRB-AdvDC, LRB-PAWG,
    //      LRB-RaiseStats), and they feed very different things.
    //
    // It answers with a REASON, never a bare name — the whole failure it fixes was a name with no
    // visible basis.
    //
    // MAIN THREAD ONLY (NGUAdvisors.Compute reads live Character). Disk reads are cheap and throttled
    // by the caller.
    public static class ProfileScout
    {
        public class Candidate
        {
            public string Name;
            public bool NoAutoRebirth;    // an LRB-style profile: nothing rebirths it on its own
            public bool IsPreset;
            public int Matched;           // plan lanes this profile feeds
            public int Wanted;            // plan lanes in total
            public List<string> MatchedNames = new List<string>();
            public double Overlap => Wanted > 0 ? (double)Matched / Wanted : 0.0;
        }

        // Throttled because this reads every profile file off disk and the caller sits behind
        // ProgressionAnalyzer.Detect's 750 ms cache — which would be a directory sweep more than once a
        // second. Profiles change at human speed; 10 s is the same interval GetOptimalFocus uses for
        // the same reason.
        private const double ScoutMs = 10000;
        private static Candidate _cached;
        private static string _cachedReason = "";
        private static bool _cachedWantLrb;
        private static DateTime _cachedAt = DateTime.MinValue;
        private static string _loggedLine = "";

        // wantLrb: the caller already decided WHICH KIND of run this is (ProgressionAnalyzer's
        // TitanPushInReach gate). fallback: the preset the caller will otherwise name.
        //
        // Returns null unless a file BEATS that fallback outright. A tie changes the answer without
        // improving it — Goal-AdvDC and Normal-24hr fund exactly the same lanes, and picking between
        // them on lane count alone would be a coin toss dressed as a verdict. The lane overlap is the
        // only thing measured here, so it is the only thing allowed to overrule the caller.
        public static Candidate Best(bool wantLrb, string fallback, out string reason)
        {
            if (_cachedWantLrb == wantLrb && (DateTime.UtcNow - _cachedAt).TotalMilliseconds < ScoutMs)
            {
                reason = _cachedReason;
                return _cached;
            }
            _cachedWantLrb = wantLrb;
            _cachedAt = DateTime.UtcNow;
            _cached = null;
            _cachedReason = "";

            reason = "";
            try
            {
                var plan = NGUAdvisors.Compute(
                    ChallengeOverlay.ChapterNguIds(ResourceType.Energy),
                    ChallengeOverlay.ChapterNguIds(ResourceType.Magic));
                if (!plan.Known) return Nothing(wantLrb, fallback, "the NGU plan is not resolved yet");

                int wanted = plan.EnergyTargets.Length + plan.MagicTargets.Length;
                if (wanted == 0) return Nothing(wantLrb, fallback, "the plan wants no NGU lanes");

                var candidates = Scan(plan, wanted).Where(c => c.NoAutoRebirth == wantLrb).ToList();
                if (candidates.Count == 0)
                    return Nothing(wantLrb, fallback, $"no profile on disk is {(wantLrb ? "an LRB" : "a cadence")} profile");

                // Highest overlap wins; among equals the user's own file, then by name so the answer is
                // stable from one refresh to the next.
                var best = candidates
                    .OrderByDescending(c => c.Overlap)
                    .ThenBy(c => c.IsPreset ? 1 : 0)
                    .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .First();

                // Nothing to say when a profile funds none of the plan: that is not a recommendation,
                // it is the absence of one.
                if (best.Matched == 0)
                    return Nothing(wantLrb, fallback, $"none of {candidates.Count} candidate(s) funds any planned lane");

                // Must BEAT the fallback, not merely equal it (see the method comment). A fallback that
                // is not among the candidates scores 0, so any funded profile outranks it.
                var current = candidates.FirstOrDefault(c => string.Equals(c.Name, fallback, StringComparison.OrdinalIgnoreCase));
                if (current != null && best.Matched <= current.Matched)
                    return Nothing(wantLrb, fallback,
                        $"{fallback} already funds {current.Matched}/{current.Wanted}; best on disk is {best.Name} at {best.Matched}/{best.Wanted}");

                string lanes = best.MatchedNames.Count > 0
                    ? string.Join(", ", best.MatchedNames.ToArray())
                    : "-";
                reason = $"Feeds {best.Matched}/{best.Wanted} of the plan's NGU lanes ({lanes})"
                       + (best.IsPreset ? " — shipped preset." : " — your own profile.");

                // Logged only when the pick CHANGES: at a 10 s throttle an unchanging answer would
                // still write six lines a minute for the whole run.
                string line = $"want={(wantLrb ? "LRB" : "timed")} pick={best.Name} {best.Matched}/{best.Wanted} [{lanes}]"
                            + $" from {candidates.Count} candidate(s): "
                            + string.Join(", ", candidates.Select(c => $"{c.Name} {c.Matched}/{c.Wanted}").ToArray());
                if (line != _loggedLine)
                {
                    _loggedLine = line;
                    Main.LogDebug($"[ProfileDbg] {line}");
                }

                _cached = best;
                _cachedReason = reason;
                return best;
            }
            catch (Exception e) { Main.LogDebug($"ProfileScout: {e.Message}"); }
            return null;
        }

        // "I found nothing better" is a DECISION and has to be as visible as a pick. Returning a silent
        // null left no way to tell "the scout ran and the preset held" from "the scout never ran" —
        // which is the exact class of invisible precedence this work set out to remove (2026-08-12: the
        // first live run logged nothing at all and the answer was unreadable either way).
        private static Candidate Nothing(bool wantLrb, string fallback, string why)
        {
            string line = $"want={(wantLrb ? "LRB" : "timed")} keeping {fallback} — {why}";
            if (line != _loggedLine)
            {
                _loggedLine = line;
                Main.LogDebug($"[ProfileDbg] {line}");
            }
            return null;
        }

        private static List<Candidate> Scan(NGUAdvisors.Plan plan, int wanted)
        {
            var found = new List<Candidate>();
            string dir = Main.GetProfilesDir();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return found;

            foreach (string path in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    JSONNode bps = JSON.Parse(File.ReadAllText(path))["Breakpoints"];
                    if (bps == null) continue;

                    var c = new Candidate
                    {
                        Name = name,
                        IsPreset = PresetInstaller.IsPreset(name),
                        NoAutoRebirth = !HasAutoRebirth(bps),
                        Wanted = wanted
                    };

                    Score(c, bps["Energy"], plan.EnergyTargets, NGUAdvisors.ENames);
                    Score(c, bps["Magic"], plan.MagicTargets, NGUAdvisors.MNames);
                    found.Add(c);
                }
                catch (Exception e) { Main.LogDebug($"ProfileScout '{Path.GetFileName(path)}': {e.Message}"); }
            }
            return found;
        }

        // Does anything end the run on its own? That — not "is there a clock" — is what separates an LRB
        // push from every other profile, and it is the wording the recommendation itself uses ("one long
        // push, no auto-rebirth").
        //
        // A NUMBER/BOSS target rebirths the run just as surely as a timer does: the user's CBlock1 carries
        // only `Number/target=1000` and no Time entry, so a clock-only test filed it as an LRB candidate.
        // Mirrors BreakpointWrapper's reading of the two shapes (CustomAllocation.cs:371) — a typed
        // `Rebirth` array, or the legacy scalar `RebirthTime`, where `-1` and a missing key both mean
        // "never".
        private static bool HasAutoRebirth(JSONNode bps)
        {
            JSONNode rebirth = bps["Rebirth"];
            if (rebirth != null && rebirth.AsArray != null && rebirth.AsArray.Count > 0)
            {
                foreach (JSONNode entry in rebirth.Children)
                {
                    if (entry["Type"] == null) continue;
                    if (entry["Type"].Value.ToUpper() == "TIME")
                    {
                        if (entry["Time"] != null && CustomAllocation.ParseTime(entry["Time"]) > 0) return true;
                    }
                    // Non-TIME entries need a Target to be accepted at all (the wrapper skips them
                    // otherwise), and one with a target WILL rebirth the run.
                    else if (entry["Target"] != null) return true;
                }
                return false;
            }
            JSONNode legacy = bps["RebirthTime"];
            return legacy != null && CustomAllocation.ParseTime(legacy) > 0;
        }

        // A profile's priority tokens across ALL its breakpoints — what it feeds at any point in a run,
        // not just at time 0. NGU-<n> and CAPNGU-<n> both count: a CAP lane drinks what is left when its
        // turn comes, which is still funding.
        private static void Score(Candidate c, JSONNode section, int[] targets, string[] names)
        {
            if (section == null || targets == null || targets.Length == 0) return;
            var fed = new HashSet<int>();
            foreach (JSONNode bp in section.Children)
            {
                JSONNode priorities = bp["Priorities"];
                if (priorities == null) continue;
                foreach (JSONNode token in priorities.Children)
                {
                    int id;
                    if (TryNguId(token.Value, out id)) fed.Add(id);
                }
            }
            foreach (int id in targets)
            {
                if (!fed.Contains(id)) continue;
                c.Matched++;
                c.MatchedNames.Add(id >= 0 && id < names.Length ? names[id] : $"#{id}");
            }
        }

        private static bool TryNguId(string token, out int id)
        {
            id = -1;
            if (string.IsNullOrEmpty(token)) return false;
            string t = token.Trim().ToUpperInvariant();
            string prefix = t.StartsWith("CAPNGU-", StringComparison.Ordinal) ? "CAPNGU-"
                          : t.StartsWith("NGU-", StringComparison.Ordinal) ? "NGU-"
                          : null;
            if (prefix == null) return false;
            return int.TryParse(t.Substring(prefix.Length), out id);
        }
    }
}
