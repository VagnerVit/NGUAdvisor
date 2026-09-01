using System;
using System.IO;

namespace NGUAdvisor.Managers
{
    // FAIL-CLOSED GATE ON THE GAME BUILD. Ported from upstream (their audit finding P0-3).
    //
    // The advisor reads hundreds of live NGU members by name and index — much of it through reflection
    // (ZoneHelpers.AutokillAvailable, TitanVersion, the field/method helpers) — and takes actions off
    // what it reads. Nothing checked the game build, so the first patch that renames or renumbers a
    // member would make those reads silently wrong and the advisor would keep automating on bad data.
    //
    // This records the build the advisor was last run against. When the live build later differs,
    // ACTIONS are held in observe-only until the user re-baselines. Reads, the HUD and advice are never
    // gated — only the paths that change game state.
    //
    // WHAT IT CAN AND CANNOT CATCH. The fingerprint is [DECOMP] Character.getVersion(), which in this
    // build is a compiled constant:
    //
    //     public int getVersion() { return 1260; }
    //
    // So it catches a VERSION BUMP, which is what a game update produces. It cannot catch a member
    // rename inside an unchanged version — nothing cheap can. Do not read a passing gate as "the reads
    // are still correct"; read it as "the build has not moved since you last confirmed they were".
    //
    // Fails OPEN in two cases, both deliberate: a fresh install with no baseline yet, and a version we
    // cannot read at all. The gate's job is to catch a build CHANGE during ongoing use, not to guess
    // whether an unknown build is correct — bricking a working setup over something unmeasurable would
    // be worse than the risk it removes.
    //
    // Re-baseline (acknowledge a new build): delete <settingsDir>\compat.dat and reload the advisor.
    public static class CompatibilityGate
    {
        private const string AckFileName = "compat.dat";

        private static bool _initialized;
        private static bool _evaluated;
        private static bool _actionsAllowed;   // default false => fail-closed until the build is confirmed
        private static int _liveVersion;
        private static int _baselineVersion;
        private static string _ackPath;

        // True once the live build is confirmed to match the acknowledged baseline (or no version could
        // be read to gate on). Evaluated lazily, so a caller running before Character exists simply sees
        // "not allowed" and retries on the next tick rather than latching a wrong answer.
        public static bool ActionsAllowed
        {
            get { EnsureEvaluated(); return _actionsAllowed; }
        }

        public static bool Evaluated => _evaluated;

        public static int LiveVersion => _liveVersion;

        public static int BaselineVersion => _baselineVersion;

        // Called from Main once the settings directory exists. Safe to call again on reload — it re-reads
        // the baseline and re-evaluates from scratch.
        public static void Initialize(string settingsDir)
        {
            try { _ackPath = string.IsNullOrEmpty(settingsDir) ? null : Path.Combine(settingsDir, AckFileName); }
            catch { _ackPath = null; }
            _initialized = true;
            _evaluated = false;
            EnsureEvaluated();
        }

        private static void EnsureEvaluated()
        {
            if (_evaluated) return;

            // DECIDE NOTHING BEFORE Initialize(). A lazy access can arrive first — one did, during
            // Main.Start() ahead of the Initialize call — and with _ackPath still null it read "no
            // baseline", announced that it was adopting the current build (writing nothing, because the
            // path was null) and answered ALLOWED. Fail-open by accident is exactly what this class
            // exists to prevent, and the log line was a plain falsehood. Staying unevaluated here is
            // safe: the property is fail-closed until asked again, and Main.Start() runs before any tick.
            if (!_initialized) return;

            Character c = Main.Character;
            if (c == null) return;   // cannot decide yet: stay fail-closed and retry on the next access

            int live;
            try { live = c.getVersion(); }
            catch { live = 0; }
            _liveVersion = live;

            if (live <= 0)
            {
                _actionsAllowed = true;
                _evaluated = true;
                Main.Log("Compatibility gate: could not read the game build; proceeding without a version check.");
                return;
            }

            _baselineVersion = ReadBaseline();

            // First run, or a deliberate re-baseline. There is no oracle for "correct", so a fresh
            // install trusts the build it is installed on.
            if (_baselineVersion <= 0)
            {
                WriteBaseline(live);
                _baselineVersion = live;
                _actionsAllowed = true;
                _evaluated = true;
                Main.Log($"Compatibility gate: baselined to game build {VersionText(live)}. Automation enabled.");
                return;
            }

            if (_baselineVersion == live)
            {
                _actionsAllowed = true;
                _evaluated = true;
                // Healthy is silent in the main log, but debug.log should be able to answer "is the gate
                // even running" without reproducing a build change to find out.
                Main.LogDebug($"Compatibility gate: build {VersionText(live)} matches baseline; automation enabled.");
                return;
            }

            _actionsAllowed = false;
            _evaluated = true;
            Main.Log("==============================================================");
            Main.Log($"COMPATIBILITY HOLD: game build changed ({VersionText(_baselineVersion)} -> {VersionText(live)}).");
            Main.Log("Automation is paused (OBSERVE-ONLY): a game update can move or rename the values the");
            Main.Log("advisor reads, which would make it act on wrong data.");
            Main.Log($"If you have confirmed the advisor still behaves correctly on build {VersionText(live)},");
            Main.Log($"delete '{_ackPath}' and reload the advisor to resume automation.");
            Main.Log("==============================================================");
        }

        private static int ReadBaseline()
        {
            try
            {
                if (_ackPath == null || !File.Exists(_ackPath)) return 0;
                string txt = (File.ReadAllText(_ackPath) ?? "").Trim();
                int v;
                return int.TryParse(txt, out v) ? v : 0;
            }
            catch { return 0; }
        }

        private static void WriteBaseline(int version)
        {
            try { if (_ackPath != null) File.WriteAllText(_ackPath, version.ToString()); }
            catch (Exception e) { Main.LogDebug("Compatibility gate: could not write baseline: " + e.Message); }
        }

        // Short status for the overlay; null when healthy, so callers can append it unconditionally.
        public static string StatusLine()
        {
            if (!_evaluated) return "checking game build...";
            if (_actionsAllowed) return null;
            return $"OBSERVE-ONLY - game build changed to {VersionText(_liveVersion)} (delete compat.dat + reload to resume)";
        }

        // getVersion() is an int like 1260 meaning "1.260" — mirrors Character.getVersionAsString(int).
        private static string VersionText(int v)
        {
            try { return (v / 1000) + "." + (v % 1000).ToString("000"); }
            catch { return v.ToString(); }
        }
    }
}
