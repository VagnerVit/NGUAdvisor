using System;
using System.Collections.Generic;
using System.Linq;
using SimpleJSON;

namespace NGUAdvisor.Managers
{
    // Editable in-memory model of an allocation profile's "Breakpoints".
    //
    // Parses with SimpleJSON (same lib the advisor uses) and serializes back to clean, indented JSON.
    // Zero UI / game dependencies so the load->edit->save round-trip is unit-testable in isolation.
    //
    // SAFETY MODEL: only the systems the editor actually edits are modeled as typed data (currently the
    // three resource priority timelines: Energy, Magic, R3). EVERY other system (Gear, Diggers, Beards,
    // Wandoos, NGUDiff, Consumables, Rebirth, Challenges, and anything unknown) is passed through VERBATIM
    // so it can never be lost or corrupted by a round-trip. Later phases model more systems one at a time,
    // each re-verified by the round-trip test. System ordering (both the top-level system order captured in
    // _systemOrder and key order within nested objects) is preserved by re-emitting in the order SimpleJSON
    // enumerated on load. NOTE: SimpleJSON's JSONObject is backed by a plain Dictionary<string,JSONNode>
    // (SimpleJson.cs), whose enumeration order is not a guaranteed contract. It equals insertion (file) order
    // here ONLY because the load->edit->save path never removes or clears keys, so on Mono/.NET Framework the
    // never-shrunk Dictionary happens to enumerate in insertion order. If SimpleJSON is ever swapped for a
    // hash-randomizing map, back JSONObject with an insertion-ordered structure to keep this round-trip stable.
    //
    // "GUI owns the file": within a MODELED breakpoint, human-comment fields are dropped on save. The drop
    // decision is made purely by KEY NAME via IsCommentKey (see CommentExact denylist + prefix rules below),
    // NOT by value type. A key matches the comment denylist (Comment*, Note*, Thresholds, Priorities1..9 doc
    // lines, etc.) is dropped; EVERY other extra key is preserved verbatim into Extras regardless of its value
    // type - including named alternate priority/gear sets (arrays like "AdvDC"/"PrioritiesDefault") AND
    // string-valued backup loadouts (e.g. "Default (MeepleMolotovEMPC)": "[ 326, ... ]"), which are user data.
    public class ProfileModel
    {
        public class PriorityBreakpoint
        {
            public int TimeSeconds;
            public List<string> Priorities = new List<string>();
            // Challenge tag: when set, the runtime prefers this breakpoint while that challenge is
            // active (BaseBreakpoints challenge-aware selection). "" = normal timeline breakpoint.
            public string Challenge = "";
            // Preserved functional (non-string) extra keys, e.g. named alternate priority sets.
            public readonly List<KeyValuePair<string, JSONNode>> Extras = new List<KeyValuePair<string, JSONNode>>();

            public int Hours => TimeSeconds / 3600;
            public int Minutes => (TimeSeconds % 3600) / 60;
            public int Seconds => TimeSeconds % 60;
        }

        // A breakpoint that carries an ordered list of integer indices (Diggers, Beards).
        public class ListBreakpoint
        {
            public int TimeSeconds;
            public List<int> Items = new List<int>();
            // Gear only: when set, the advisor optimizes gear for this objective instead of using Items.
            public string Objective = "";
            // Gear only: when optimizing, always pin the single best Respawn item into the loadout.
            public bool ForceRespawn = false;
            public string Challenge = "";
            public readonly List<KeyValuePair<string, JSONNode>> Extras = new List<KeyValuePair<string, JSONNode>>();

            public int Hours => TimeSeconds / 3600;
            public int Minutes => (TimeSeconds % 3600) / 60;
            public int Seconds => TimeSeconds % 60;
        }

        // A breakpoint carrying an ordered list of string tokens (Consumables "Items": ["EPOT-B","MPOT-B:5"]).
        public class StringListBreakpoint
        {
            public int TimeSeconds;
            public List<string> Items = new List<string>();
            public string Challenge = "";
            public readonly List<KeyValuePair<string, JSONNode>> Extras = new List<KeyValuePair<string, JSONNode>>();
            public int Hours => TimeSeconds / 3600;
            public int Minutes => (TimeSeconds % 3600) / 60;
            public int Seconds => TimeSeconds % 60;
        }

        // One entry of the Rebirth array: a Type + optional trigger time + optional numeric Target. Any other
        // keys are preserved.
        public class RebirthEntry
        {
            public string Type = "";
            public int TimeSeconds;
            public double? Target;
            public readonly List<KeyValuePair<string, JSONNode>> Extras = new List<KeyValuePair<string, JSONNode>>();
            public int Hours => TimeSeconds / 3600;
            public int Minutes => (TimeSeconds % 3600) / 60;
            public int Seconds => TimeSeconds % 60;
        }

        // A breakpoint carrying a single integer value (Wandoos OS, NGU difficulty).
        public class ValueBreakpoint
        {
            public int TimeSeconds;
            public int Value;
            public string Challenge = "";
            public readonly List<KeyValuePair<string, JSONNode>> Extras = new List<KeyValuePair<string, JSONNode>>();

            public int Hours => TimeSeconds / 3600;
            public int Minutes => (TimeSeconds % 3600) / 60;
            public int Seconds => TimeSeconds % 60;
        }

        // Modeled systems.
        public List<PriorityBreakpoint> Energy = new List<PriorityBreakpoint>();
        public List<PriorityBreakpoint> Magic = new List<PriorityBreakpoint>();
        public List<PriorityBreakpoint> R3 = new List<PriorityBreakpoint>();
        public List<ListBreakpoint> Diggers = new List<ListBreakpoint>();
        public List<ListBreakpoint> Beards = new List<ListBreakpoint>();
        public List<ListBreakpoint> Gear = new List<ListBreakpoint>();   // payload key "ID"
        public List<ValueBreakpoint> Wandoos = new List<ValueBreakpoint>();   // payload key "OS"
        public List<ValueBreakpoint> NGUDiff = new List<ValueBreakpoint>();   // payload key "Diff"
        public List<StringListBreakpoint> Consumables = new List<StringListBreakpoint>();  // payload key "Items"
        public List<RebirthEntry> Rebirth = new List<RebirthEntry>();
        public List<string> Challenges = new List<string>();   // flat top-level array (not time-based)

        private static readonly HashSet<string> ModeledSystems =
            new HashSet<string>(StringComparer.Ordinal) { "Energy", "Magic", "R3", "Diggers", "Beards", "Gear", "Wandoos", "NGUDiff", "Consumables", "Rebirth", "Challenges" };

        // Original "Breakpoints" object and its key order, for verbatim passthrough of unmodeled systems.
        // Captures SimpleJSON's (Dictionary-backed) key enumeration order at load; == insertion order because the round-trip never removes keys.
        private readonly List<string> _systemOrder = new List<string>();
        private readonly Dictionary<string, JSONNode> _passthrough = new Dictionary<string, JSONNode>(StringComparer.Ordinal);

        // ----- Load -----

        public static ProfileModel Load(string json)
        {
            var root = JSON.Parse(json);
            if (root == null)
                throw new Exception("Profile could not be parsed as JSON.");
            var bps = root["Breakpoints"];
            if (bps == null || !bps.IsObject)
                throw new Exception("Profile has no \"Breakpoints\" object.");

            var m = new ProfileModel();
            foreach (var kv in bps.AsObject)
            {
                m._systemOrder.Add(kv.Key);
                if (kv.Key == "Energy") m.Energy = LoadPriorities(kv.Value);
                else if (kv.Key == "Magic") m.Magic = LoadPriorities(kv.Value);
                else if (kv.Key == "R3") m.R3 = LoadPriorities(kv.Value);
                else if (kv.Key == "Diggers") m.Diggers = LoadList(kv.Value, "List");
                else if (kv.Key == "Beards") m.Beards = LoadList(kv.Value, "List");
                else if (kv.Key == "Gear") m.Gear = LoadList(kv.Value, "ID");
                else if (kv.Key == "Wandoos") m.Wandoos = LoadValue(kv.Value, "OS");
                else if (kv.Key == "NGUDiff") m.NGUDiff = LoadValue(kv.Value, "Diff");
                else if (kv.Key == "Consumables") m.Consumables = LoadStringList(kv.Value, "Items");
                else if (kv.Key == "Rebirth") m.Rebirth = LoadRebirth(kv.Value);
                else if (kv.Key == "Challenges") { foreach (var c in ArrayChildren(kv.Value)) m.Challenges.Add(c.Value); }
                else m._passthrough[kv.Key] = kv.Value; // verbatim
            }
            return m;
        }

        private static IEnumerable<JSONNode> ArrayChildren(JSONNode node) =>
            node != null && node.IsArray ? node.Children : Enumerable.Empty<JSONNode>();

        // Keys that are pure human documentation and are dropped on save. Everything NOT matched here is
        // preserved verbatim - including named alternate priority/gear sets (arrays) AND string-valued
        // backup loadouts like "Default (MeepleMolotovEMPC)": "[ 326, ... ]" which are user data, not comments.
        private static readonly HashSet<string> CommentExact =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Comment", "Note", "Thresholds", "Timing", "DiggerOptions", "BeardOptions", "GO Notes", "GO Note" };

        private static bool IsCommentKey(string key)
        {
            if (CommentExact.Contains(key)) return true;
            if (key.StartsWith("Comment", StringComparison.OrdinalIgnoreCase)) return true;  // Comment2, CommentB
            if (key.StartsWith("Note", StringComparison.OrdinalIgnoreCase)) return true;      // Note1, Note2
            if (key.StartsWith("GO Note", StringComparison.OrdinalIgnoreCase)) return true;
            if (key.StartsWith("PriorityComment", StringComparison.OrdinalIgnoreCase)) return true;
            if (key.StartsWith("PriorityExample", StringComparison.OrdinalIgnoreCase)) return true;
            if (key.StartsWith("PriorityPercent", StringComparison.OrdinalIgnoreCase)) return true;
            // "Priorities" + a digit (Priorities1..9 doc lines) - but NOT PrioritiesDefault / PrioritiesX (data)
            if (key.Length > 10 && key.StartsWith("Priorities", StringComparison.OrdinalIgnoreCase) && char.IsDigit(key[10]))
                return true;
            return false;
        }

        private static List<PriorityBreakpoint> LoadPriorities(JSONNode node)
        {
            var list = new List<PriorityBreakpoint>();
            foreach (var bp in ArrayChildren(node))
            {
                var b = new PriorityBreakpoint { TimeSeconds = ParseTime(bp["Time"]) };
                foreach (var p in ArrayChildren(bp["Priorities"]))
                    b.Priorities.Add(p.Value);

                if (bp.IsObject)
                {
                    foreach (var kv in bp.AsObject)
                    {
                        if (kv.Key == "Time" || kv.Key == "Priorities") continue;
                        if (kv.Key == "Challenge") { b.Challenge = kv.Value.Value ?? ""; continue; }
                        if (IsCommentKey(kv.Key)) continue;
                        b.Extras.Add(new KeyValuePair<string, JSONNode>(kv.Key, kv.Value));
                    }
                }
                list.Add(b);
            }
            return list;
        }

        private static List<ListBreakpoint> LoadList(JSONNode node, string payloadKey)
        {
            var list = new List<ListBreakpoint>();
            foreach (var bp in ArrayChildren(node))
            {
                var b = new ListBreakpoint { TimeSeconds = ParseTime(bp["Time"]) };
                foreach (var it in ArrayChildren(bp[payloadKey]))
                    b.Items.Add(it.AsInt);

                if (bp.IsObject)
                    foreach (var kv in bp.AsObject)
                    {
                        if (kv.Key == "Time" || kv.Key == payloadKey) continue;
                        if (kv.Key == "Objective") { b.Objective = kv.Value.Value; continue; }
                        if (kv.Key == "TopRespawn") { b.ForceRespawn = kv.Value.AsBool; continue; }
                        if (kv.Key == "Challenge") { b.Challenge = kv.Value.Value ?? ""; continue; }
                        if (IsCommentKey(kv.Key)) continue;
                        b.Extras.Add(new KeyValuePair<string, JSONNode>(kv.Key, kv.Value));
                    }
                list.Add(b);
            }
            return list;
        }

        private static List<StringListBreakpoint> LoadStringList(JSONNode node, string payloadKey)
        {
            var list = new List<StringListBreakpoint>();
            foreach (var bp in ArrayChildren(node))
            {
                var b = new StringListBreakpoint { TimeSeconds = ParseTime(bp["Time"]) };
                foreach (var it in ArrayChildren(bp[payloadKey]))
                    b.Items.Add(it.Value);
                if (bp.IsObject)
                    foreach (var kv in bp.AsObject)
                    {
                        if (kv.Key == "Time" || kv.Key == payloadKey) continue;
                        if (kv.Key == "Challenge") { b.Challenge = kv.Value.Value ?? ""; continue; }
                        if (IsCommentKey(kv.Key)) continue;
                        b.Extras.Add(new KeyValuePair<string, JSONNode>(kv.Key, kv.Value));
                    }
                list.Add(b);
            }
            return list;
        }

        private static List<RebirthEntry> LoadRebirth(JSONNode node)
        {
            var list = new List<RebirthEntry>();
            foreach (var bp in ArrayChildren(node))
            {
                var b = new RebirthEntry
                {
                    Type = bp["Type"] != null ? bp["Type"].Value : "",
                    TimeSeconds = ParseTime(bp["Time"])
                };
                if (bp["Target"] != null && bp["Target"].IsNumber) b.Target = bp["Target"].AsDouble;
                if (bp.IsObject)
                    foreach (var kv in bp.AsObject)
                    {
                        if (kv.Key == "Type" || kv.Key == "Time" || kv.Key == "Target") continue;
                        if (IsCommentKey(kv.Key)) continue;
                        b.Extras.Add(new KeyValuePair<string, JSONNode>(kv.Key, kv.Value));
                    }
                list.Add(b);
            }
            return list;
        }

        private static List<ValueBreakpoint> LoadValue(JSONNode node, string payloadKey)
        {
            var list = new List<ValueBreakpoint>();
            foreach (var bp in ArrayChildren(node))
            {
                var b = new ValueBreakpoint { TimeSeconds = ParseTime(bp["Time"]) };
                if (bp[payloadKey] != null && bp[payloadKey].IsNumber) b.Value = bp[payloadKey].AsInt;
                if (bp.IsObject)
                    foreach (var kv in bp.AsObject)
                    {
                        if (kv.Key == "Time" || kv.Key == payloadKey) continue;
                        if (kv.Key == "Challenge") { b.Challenge = kv.Value.Value ?? ""; continue; }
                        if (IsCommentKey(kv.Key)) continue;
                        b.Extras.Add(new KeyValuePair<string, JSONNode>(kv.Key, kv.Value));
                    }
                list.Add(b);
            }
            return list;
        }

        // Mirrors BaseBreakpoints.ParseTime: number = seconds; object = sum of h/m/(other=seconds).
        private static int ParseTime(JSONNode timeNode)
        {
            if (timeNode == null) return 0;
            if (timeNode.IsNumber) return timeNode.AsInt;
            int t = 0;
            if (timeNode.IsObject)
            {
                foreach (var kv in timeNode.AsObject)
                {
                    if (!kv.Value.IsNumber) continue;
                    switch (kv.Key.ToLower())
                    {
                        case "h": t += 3600 * kv.Value.AsInt; break;
                        case "m": t += 60 * kv.Value.AsInt; break;
                        default: t += kv.Value.AsInt; break;
                    }
                }
            }
            return t;
        }

        // ----- Save -----

        public string ToJson()
        {
            var bps = new JSONObject();

            // Emit systems in their original order; regenerate modeled ones, pass others through verbatim.
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in _systemOrder)
            {
                if (emitted.Contains(key)) continue;
                emitted.Add(key);
                if (key == "Energy") bps["Energy"] = PrioritiesToJson(Energy);
                else if (key == "Magic") bps["Magic"] = PrioritiesToJson(Magic);
                else if (key == "R3") bps["R3"] = PrioritiesToJson(R3);
                else if (key == "Diggers") bps["Diggers"] = ListToJson(Diggers, "List");
                else if (key == "Beards") bps["Beards"] = ListToJson(Beards, "List");
                else if (key == "Gear") bps["Gear"] = ListToJson(Gear, "ID");
                else if (key == "Wandoos") bps["Wandoos"] = ValueToJson(Wandoos, "OS");
                else if (key == "NGUDiff") bps["NGUDiff"] = ValueToJson(NGUDiff, "Diff");
                else if (key == "Consumables") bps["Consumables"] = StringListToJson(Consumables, "Items");
                else if (key == "Rebirth") bps["Rebirth"] = RebirthToJson(Rebirth);
                else if (key == "Challenges") bps["Challenges"] = ChallengesToJson(Challenges);
                else if (_passthrough.TryGetValue(key, out var raw)) bps[key] = raw;
            }

            // Modeled systems that were not present originally but now have content (defensive).
            if (!emitted.Contains("Energy") && Energy.Count > 0) bps["Energy"] = PrioritiesToJson(Energy);
            if (!emitted.Contains("Magic") && Magic.Count > 0) bps["Magic"] = PrioritiesToJson(Magic);
            if (!emitted.Contains("R3") && R3.Count > 0) bps["R3"] = PrioritiesToJson(R3);
            if (!emitted.Contains("Diggers") && Diggers.Count > 0) bps["Diggers"] = ListToJson(Diggers, "List");
            if (!emitted.Contains("Beards") && Beards.Count > 0) bps["Beards"] = ListToJson(Beards, "List");
            if (!emitted.Contains("Gear") && Gear.Count > 0) bps["Gear"] = ListToJson(Gear, "ID");
            if (!emitted.Contains("Wandoos") && Wandoos.Count > 0) bps["Wandoos"] = ValueToJson(Wandoos, "OS");
            if (!emitted.Contains("NGUDiff") && NGUDiff.Count > 0) bps["NGUDiff"] = ValueToJson(NGUDiff, "Diff");
            if (!emitted.Contains("Consumables") && Consumables.Count > 0) bps["Consumables"] = StringListToJson(Consumables, "Items");
            if (!emitted.Contains("Rebirth") && Rebirth.Count > 0) bps["Rebirth"] = RebirthToJson(Rebirth);
            if (!emitted.Contains("Challenges") && Challenges.Count > 0) bps["Challenges"] = ChallengesToJson(Challenges);

            var root = new JSONObject();
            root["Breakpoints"] = bps;
            return root.ToString(2);
        }

        private static JSONNode TimeToJson(int seconds)
        {
            if (seconds <= 0)
                return new JSONNumber(0);
            var o = new JSONObject();
            int h = seconds / 3600, m = (seconds % 3600) / 60, s = seconds % 60;
            if (h != 0) o["h"] = h;
            if (m != 0) o["m"] = m;
            if (s != 0) o["s"] = s;
            return o;
        }

        private static JSONArray PrioritiesToJson(List<PriorityBreakpoint> list)
        {
            var arr = new JSONArray();
            foreach (var b in list)
            {
                var o = new JSONObject();
                o["Time"] = TimeToJson(b.TimeSeconds);
                var pr = new JSONArray();
                foreach (var p in b.Priorities) pr.Add(p);
                o["Priorities"] = pr;
                if (!string.IsNullOrEmpty(b.Challenge)) o["Challenge"] = b.Challenge;
                foreach (var kv in b.Extras) o[kv.Key] = kv.Value;
                arr.Add(o);
            }
            return arr;
        }

        private static JSONArray StringListToJson(List<StringListBreakpoint> list, string payloadKey)
        {
            var arr = new JSONArray();
            foreach (var b in list)
            {
                var o = new JSONObject();
                o["Time"] = TimeToJson(b.TimeSeconds);
                var items = new JSONArray();
                foreach (var s in b.Items) items.Add(s);
                o[payloadKey] = items;
                if (!string.IsNullOrEmpty(b.Challenge)) o["Challenge"] = b.Challenge;
                foreach (var kv in b.Extras) o[kv.Key] = kv.Value;
                arr.Add(o);
            }
            return arr;
        }

        private static JSONArray RebirthToJson(List<RebirthEntry> list)
        {
            var arr = new JSONArray();
            foreach (var b in list)
            {
                var o = new JSONObject();
                if (!string.IsNullOrEmpty(b.Type)) o["Type"] = b.Type;
                o["Time"] = TimeToJson(b.TimeSeconds);
                if (b.Target.HasValue) o["Target"] = b.Target.Value;
                foreach (var kv in b.Extras) o[kv.Key] = kv.Value;
                arr.Add(o);
            }
            return arr;
        }

        private static JSONArray ChallengesToJson(List<string> list)
        {
            var arr = new JSONArray();
            foreach (var c in list) arr.Add(c);
            return arr;
        }

        private static JSONArray ValueToJson(List<ValueBreakpoint> list, string payloadKey)
        {
            var arr = new JSONArray();
            foreach (var b in list)
            {
                var o = new JSONObject();
                o["Time"] = TimeToJson(b.TimeSeconds);
                o[payloadKey] = b.Value;
                if (!string.IsNullOrEmpty(b.Challenge)) o["Challenge"] = b.Challenge;
                foreach (var kv in b.Extras) o[kv.Key] = kv.Value;
                arr.Add(o);
            }
            return arr;
        }

        private static JSONArray ListToJson(List<ListBreakpoint> list, string payloadKey)
        {
            var arr = new JSONArray();
            foreach (var b in list)
            {
                var o = new JSONObject();
                o["Time"] = TimeToJson(b.TimeSeconds);
                var items = new JSONArray();
                foreach (var i in b.Items) items.Add(i);
                o[payloadKey] = items;
                if (!string.IsNullOrEmpty(b.Objective)) o["Objective"] = b.Objective;
                if (b.ForceRespawn) o["TopRespawn"] = b.ForceRespawn;
                if (!string.IsNullOrEmpty(b.Challenge)) o["Challenge"] = b.Challenge;
                foreach (var kv in b.Extras) o[kv.Key] = kv.Value;
                arr.Add(o);
            }
            return arr;
        }
    }
}
