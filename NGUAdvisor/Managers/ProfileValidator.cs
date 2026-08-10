using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NGUAdvisor.Managers
{
    // Self-contained strict JSON validator for allocation profiles.
    //
    // The advisor parses profiles with SimpleJSON, which is extremely lenient: it treats } and ]
    // as interchangeable and silently ignores stray/missing commas, so a malformed profile does not
    // crash - it misparses silently and the bot misbehaves with no feedback. This validator does a
    // near-RFC-8259 parse (with intentional tolerances: trailing commas, plus number lexing that
    // accepts a few non-canonical-but-unambiguous forms such as leading zeros and a bare leading
    // decimal point like -.5, which the SimpleJSON parse path also accepts) and reports the first
    // structural problem with a line/column, so the user can fix
    // it instead of guessing. Zero game/Unity dependencies so it is unit-testable in isolation.
    public static class ProfileValidator
    {
        public struct Result
        {
            public bool Ok;
            public int Line;    // 1-based; 0 when Ok
            public int Col;     // 1-based; 0 when Ok
            public string Message;

            public static Result Success => new Result { Ok = true };
        }

        public static Result Validate(string json)
        {
            if (json == null)
                return Fail(json, 0, "Profile is empty.");

            var p = new Parser(json);
            p.SkipWhitespace();
            if (p.Eof)
                return Fail(json, p.Pos, "Profile is empty.");

            if (!p.ParseValue(out var err))
                return err;

            p.SkipWhitespace();
            if (!p.Eof)
                return Fail(json, p.Pos, "Unexpected extra content after the top-level value.");

            return Result.Success;
        }

        private static readonly string[] AugmentNames =
        {
            "Safety Scissors", "Milk Infusion", "Cannon Implant", "Shoulder Mounted Minigun",
            "Energy Buster", "Advanced Exoskeleton", "Laser Sword"
        };

        // Advice, never a failure: a breakpoint that funds more than one augment is legal JSON that
        // throws energy away. Augment boosts stack ADDITIVELY and each one is convex in the energy it
        // receives, so a shared budget always pays more concentrated in a single augment (the maths is
        // in docs/AUGMENTS.md). Callers surface these alongside the parse result and never block on them.
        //
        // Note the token indexing this has to respect: AUG-<n> runs over a FLAT 0-13, even = an augment,
        // odd = that augment's upgrade. `AUG-8, AUG-9` is therefore ONE augment's two halves, not two
        // augments, and must not warn — which is exactly the reading that makes a correct profile look
        // wrong at a glance.
        public static List<string> Warnings(string json)
        {
            var warnings = new List<string>();
            if (string.IsNullOrEmpty(json))
                return warnings;

            ProfileModel model = null;
            try { model = ProfileModel.Load(json); } catch { }
            if (model == null)
                return warnings;

            foreach (var bp in model.Energy)
            {
                var funded = new List<string>();
                foreach (var token in bp.Priorities)
                {
                    string name = FundedAugment(token);
                    if (name != null && !funded.Contains(name))
                        funded.Add(name);
                }

                if (funded.Count > 1)
                    warnings.Add($"Energy breakpoint at {FormatTime(bp.TimeSeconds)} funds {funded.Count} " +
                        $"augments ({string.Join(", ", funded.ToArray())}). Augment boosts stack additively, so " +
                        "splitting energy between them is close to pure waste — fund one augment (both halves of " +
                        "its pair) and spend the rest elsewhere. BESTAUG picks that augment for you.");
            }

            // Gear priority chains: advice only. A step whose objective never resolves is SKIPPED by
            // GearBreakpoints rather than mis-applied (same refuse-don't-guess rule SpendPlanner uses
            // for perk names), so a silent skip would look like "the chain ran" while a slot budget
            // quietly vanished. Say so here instead.
            foreach (var bp in model.Gear)
            {
                if (bp.Priorities.Count == 0)
                    continue;

                if (bp.Priorities.Count > GearChain.MaxPriorities)
                    warnings.Add($"A gear priority chain at {FormatTime(bp.TimeSeconds)} has {bp.Priorities.Count} " +
                        $"steps; only the first {GearChain.MaxPriorities} are used.");

                for (var i = 0; i < bp.Priorities.Count; i++)
                {
                    var step = bp.Priorities[i];
                    var name = step.Objective ?? "";
                    var stepLabel = string.IsNullOrEmpty(name) ? $"step {i + 1}" : $"\"{name}\"";

                    if (string.IsNullOrEmpty(name))
                        warnings.Add($"A gear priority step at {FormatTime(bp.TimeSeconds)} has no Objective and will be skipped.");
                    else if (GearChain.FindObjective(name) == null)
                        warnings.Add($"Gear priority objective \"{name}\" at {FormatTime(bp.TimeSeconds)} is not recognized; that step will be skipped.");

                    if (step.Slots < 0)
                        warnings.Add($"Gear priority {stepLabel} at {FormatTime(bp.TimeSeconds)} has negative Slots; it will claim no accessory slots.");
                }
            }

            return warnings;
        }

        // The augment a single priority token funds out of the SHARED energy pool, or null when it funds
        // none (or names one we cannot resolve, e.g. a typo'd index — that is the structural validator's
        // business, not this).
        //
        // CAP tokens are deliberately not counted: a CAPAUG takes min(need, idle) and stays out of the
        // equal-share divisor, so it is a bounded reservation rather than a split of the budget, and it is
        // how a profile legitimately forces a specific augment — CBlock2.0-LSC pairs `CAPAUG-12:80` (the
        // Laser Sword the challenge requires) with `BESTAUG` for everything left over. Warning there would
        // be nagging about the only way to write that run.
        private static string FundedAugment(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            string t = token.ToUpperInvariant();
            int colon = t.IndexOf(':');
            if (colon >= 0)
                t = t.Substring(0, colon);

            string index = null;
            int dash = t.IndexOf('-');
            if (dash >= 0)
            {
                index = t.Substring(dash + 1);
                t = t.Substring(0, dash);
            }

            // BESTAUG is checked first: it is its own token family and funds an augment it chooses live.
            if (t == "BESTAUG")
                return "BESTAUG";
            if (t != "AUG")
                return null;

            int flat = 0;
            if (index != null && !int.TryParse(index, out flat))
                return null;
            if (flat < 0 || flat / 2 >= AugmentNames.Length)
                return null;

            return AugmentNames[flat / 2];
        }

        private static string FormatTime(int seconds)
        {
            if (seconds < 0)
                seconds = 0;
            int h = seconds / 3600;
            int m = seconds % 3600 / 60;
            int s = seconds % 60;
            return s > 0
                ? $"{h}:{m:00}:{s:00}"
                : $"{h}:{m:00}";
        }

        private static Result Fail(string json, int pos, string message)
        {
            LineCol(json, pos, out var line, out var col);
            return new Result { Ok = false, Line = line, Col = col, Message = message };
        }

        private static void LineCol(string json, int pos, out int line, out int col)
        {
            line = 1;
            col = 1;
            if (json == null)
                return;
            if (pos > json.Length)
                pos = json.Length;
            for (int i = 0; i < pos; i++)
            {
                if (json[i] == '\n')
                {
                    line++;
                    col = 1;
                }
                else
                {
                    col++;
                }
            }
        }

        private class Parser
        {
            private readonly string _s;
            public int Pos;

            public Parser(string s)
            {
                _s = s;
                Pos = 0;
            }

            public bool Eof => Pos >= _s.Length;
            private char Cur => _s[Pos];

            public void SkipWhitespace()
            {
                while (Pos < _s.Length)
                {
                    var c = _s[Pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                        Pos++;
                    else
                        break;
                }
            }

            private Result Err(string message) => Fail(_s, Pos, message);
            private Result ErrAt(int pos, string message) => Fail(_s, pos, message);

            public bool ParseValue(out Result err)
            {
                err = Result.Success;
                SkipWhitespace();
                if (Eof)
                {
                    err = Err("Expected a value but reached the end of the profile.");
                    return false;
                }

                var c = Cur;
                switch (c)
                {
                    case '{': return ParseObject(out err);
                    case '[': return ParseArray(out err);
                    case '"': return ParseString(out err);
                    case 't':
                    case 'f': return ParseKeyword(out err);
                    case 'n': return ParseKeyword(out err);
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ParseNumber(out err);
                        err = Err($"Unexpected character '{Describe(c)}' where a value was expected.");
                        return false;
                }
            }

            private bool ParseObject(out Result err)
            {
                err = Result.Success;
                Pos++; // consume {
                SkipWhitespace();
                if (!Eof && Cur == '}') { Pos++; return true; }

                var seen = new HashSet<string>();
                while (true)
                {
                    SkipWhitespace();
                    if (Eof)
                    {
                        err = Err("Unterminated object - missing '}'.");
                        return false;
                    }
                    if (Cur != '"')
                    {
                        err = Err($"Expected a property name in double quotes, found '{Describe(Cur)}'.");
                        return false;
                    }
                    if (!ParseString(out err, out var key)) return false;
                    if (!seen.Add(key))
                    {
                        err = Err($"Duplicate property name '{key}' in object.");
                        return false;
                    }

                    SkipWhitespace();
                    if (Eof || Cur != ':')
                    {
                        err = Err("Expected ':' after the property name.");
                        return false;
                    }
                    Pos++; // consume :

                    if (!ParseValue(out err)) return false;

                    SkipWhitespace();
                    if (Eof)
                    {
                        err = Err("Unterminated object - missing '}'.");
                        return false;
                    }
                    if (Cur == ',')
                    {
                        Pos++;
                        SkipWhitespace();
                        // Tolerate a trailing comma before '}'
                        if (!Eof && Cur == '}') { Pos++; return true; }
                        continue;
                    }
                    if (Cur == '}') { Pos++; return true; }
                    if (Cur == ']')
                    {
                        err = Err("Object closed with ']' instead of '}'.");
                        return false;
                    }
                    err = Err($"Expected ',' or '}}' after a property value, found '{Describe(Cur)}' (a missing comma is the usual cause).");
                    return false;
                }
            }

            private bool ParseArray(out Result err)
            {
                err = Result.Success;
                Pos++; // consume [
                SkipWhitespace();
                if (!Eof && Cur == ']') { Pos++; return true; }

                while (true)
                {
                    if (!ParseValue(out err)) return false;

                    SkipWhitespace();
                    if (Eof)
                    {
                        err = Err("Unterminated array - missing ']'.");
                        return false;
                    }
                    if (Cur == ',')
                    {
                        Pos++;
                        SkipWhitespace();
                        // Tolerate a trailing comma before ']'
                        if (!Eof && Cur == ']') { Pos++; return true; }
                        continue;
                    }
                    if (Cur == ']') { Pos++; return true; }
                    if (Cur == '}')
                    {
                        err = Err("Array closed with '}' instead of ']'.");
                        return false;
                    }
                    err = Err($"Expected ',' or ']' after an array element, found '{Describe(Cur)}' (a missing comma is the usual cause).");
                    return false;
                }
            }

            private bool ParseString(out Result err)
            {
                return ParseString(out err, out _);
            }

            private bool ParseString(out Result err, out string value)
            {
                err = Result.Success;
                value = null;
                int start = Pos;
                Pos++; // consume opening quote
                while (Pos < _s.Length)
                {
                    var c = _s[Pos];
                    if (c == '"')
                    {
                        Pos++;
                        // Decode escapes so that e.g. "a" and "a" collide the same way
                        // SimpleJson's dictionary-based parsing would treat them as duplicates.
                        value = DecodeStringLiteral(_s.Substring(start, Pos - start));
                        return true;
                    }
                    if (c == '\\')
                    {
                        Pos++;
                        if (Pos >= _s.Length) break;
                        var e = _s[Pos];
                        if (e == '"' || e == '\\' || e == '/' || e == 'b' || e == 'f' || e == 'n' || e == 'r' || e == 't')
                        {
                            Pos++;
                        }
                        else if (e == 'u')
                        {
                            Pos++;
                            for (int k = 0; k < 4; k++)
                            {
                                if (Pos >= _s.Length || !IsHex(_s[Pos]))
                                {
                                    err = ErrAt(Pos, "Invalid \\u escape in string (expected 4 hex digits).");
                                    return false;
                                }
                                Pos++;
                            }
                        }
                        else
                        {
                            err = ErrAt(Pos, $"Invalid escape '\\{Describe(e)}' in string.");
                            return false;
                        }
                    }
                    else if (c == '\n' || c == '\r')
                    {
                        err = ErrAt(Pos, "Unterminated string (line break before closing quote).");
                        return false;
                    }
                    else
                    {
                        Pos++;
                    }
                }
                err = ErrAt(start, "Unterminated string - missing closing quote.");
                return false;
            }

            // Decodes a raw quoted JSON string literal (including the surrounding quotes) into its
            // logical value, so that key-comparison for duplicate detection matches what a real JSON
            // parser (e.g. SimpleJSON building a Dictionary) would treat as identical keys. Assumes
            // the literal was already validated by ParseString (well-formed escapes, no stray control
            // characters), so it does not re-validate here.
            private static string DecodeStringLiteral(string literal)
            {
                var sb = new StringBuilder(literal.Length);
                for (int i = 1; i < literal.Length - 1; i++)
                {
                    var c = literal[i];
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    i++;
                    var e = literal[i];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            var hex = literal.Substring(i + 1, 4);
                            sb.Append((char)int.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                    }
                }
                return sb.ToString();
            }

            private bool ParseNumber(out Result err)
            {
                err = Result.Success;
                int start = Pos;
                if (!Eof && Cur == '-') Pos++;
                while (!Eof && ((Cur >= '0' && Cur <= '9') || Cur == '.' || Cur == 'e' || Cur == 'E' || Cur == '+' || Cur == '-'))
                    Pos++;
                var slice = _s.Substring(start, Pos - start);
                if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    err = ErrAt(start, $"Invalid number '{slice}'.");
                    return false;
                }
                return true;
            }

            private bool ParseKeyword(out Result err)
            {
                err = Result.Success;
                if (Match("true") || Match("false") || Match("null"))
                    return true;
                err = Err("Expected true, false, or null.");
                return false;
            }

            private bool Match(string word)
            {
                if (Pos + word.Length > _s.Length) return false;
                for (int k = 0; k < word.Length; k++)
                    if (_s[Pos + k] != word[k]) return false;
                Pos += word.Length;
                return true;
            }

            private static bool IsHex(char c) =>
                (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

            private static string Describe(char c)
            {
                if (c == '\n') return "\\n";
                if (c == '\r') return "\\r";
                if (c == '\t') return "\\t";
                return c.ToString();
            }
        }
    }
}
