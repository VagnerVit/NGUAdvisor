using System;
using System.Diagnostics;
using System.IO;

namespace NGUAdvisor.Managers
{
    // "Is there a newer release?", answered without a single socket inside the game process.
    //
    // WHY NOT JUST CALL GITHUB. This assembly runs in NGU Idle's Unity 2019.4 Mono domain, whose TLS
    // stack and root certificate store cannot be relied on to complete an HTTPS handshake with
    // api.github.com. So the advisor never talks to the network: it starts injector\update.ps1 (see
    // build/update.ps1), which runs in Windows PowerShell with the host's real trust store, and reads
    // the answer back out of a file. Same shape as every other outside-the-game handshake here —
    // Loader's unload.request, StateExport's request file: one side drops a file, the other polls it.
    //
    // THE ADVISOR OWNS THE VERSION. Main.Version is the only trustworthy SemVer in the build (the
    // AssemblyInfo attributes drifted to 1.2.2 and the assembly name carries a build timestamp), so
    // it is passed to the script on every check rather than sniffed off the DLL. The GAME's version
    // is never consulted: NGU Idle is done shipping updates and nothing here is gated on it.
    //
    // MAIN THREAD ONLY, and that is free: Process.Start does not block and touches no Unity object,
    // so Tick() runs inside Main.Update()'s existing once-a-second budget.
    public static class UpdateChecker
    {
        public const string StateFileName = "update.state";
        // Written by "Run NGU Advisor.bat" before it injects. It is the ONLY way this code can learn
        // where the release folder is: smi.exe loads the assembly from BYTES, so Assembly.Location is
        // empty and the advisor genuinely does not know which folder it came from.
        public const string InstallFileName = "install.txt";

        // Matches the throttle in update.ps1. Re-asking sooner would only burn the unauthenticated
        // API's 60 requests/hour; an idle game is left running for days, so a new release is noticed
        // within hours either way.
        private static readonly TimeSpan SpawnInterval = TimeSpan.FromHours(6);

        // A failed check writes no checkedUtc, so the script's own throttle lets a retry straight through;
        // without this the next attempt after a DNS miss (PC just woken) is a full SpawnInterval away.
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(60);

        private static DateTime _lastSpawn = DateTime.MinValue;
        private static DateTime _lastStateStamp = DateTime.MinValue;
        private static DateTime _retryAt = DateTime.MinValue;
        private static bool _retryUsed;
        private static bool _installWarned;

        // Null until a check has actually reported something newer.
        public static string LatestVersion { get; private set; }

        public static bool Available => !string.IsNullOrEmpty(LatestVersion);

        // True while the last check the script ran failed (update.state carries failedUtc until a check succeeds).
        public static bool CheckFailing { get; private set; }

        private static string DataDir => Main.GetSettingsDir() ?? ".";

        private static string StatePath => Path.Combine(DataDir, StateFileName);

        // Polled once a second from Main.Update(). Deliberately NOT in AutomationRoutine: that returns
        // early while the advisor is paused, and a paused advisor should still notice an update.
        public static void Tick()
        {
            try
            {
                if (Main.Settings != null && Main.Settings.DisableUpdateCheck) return;
                ReadState();
                Spawn();
            }
            catch (Exception e)
            {
                Main.LogDebug($"Update check tick failed: {e.Message}");
            }
        }

        private static void ReadState()
        {
            var path = StatePath;
            if (!File.Exists(path)) return;
            var stamp = File.GetLastWriteTimeUtc(path);
            if (stamp == _lastStateStamp) return;
            _lastStateStamp = stamp;

            string latest = null;
            bool available = false;
            bool failed = false;
            bool networkFailure = false;
            foreach (var line in File.ReadAllLines(path))
            {
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                var key = line.Substring(0, i);
                var value = line.Substring(i + 1).Trim();
                if (key == "latest") latest = value;
                else if (key == "available") available = value == "1";
                else if (key == "failedUtc") failed = value.Length > 0;
                else if (key == "failedKind") networkFailure = value == "network";
            }

            var wasAvailable = Available;
            LatestVersion = available ? latest : null;
            CheckFailing = failed;
            if (Available && !wasAvailable)
                Main.Log($"Update available: v{LatestVersion} — it installs the next time you run the launcher");

            if (failed && networkFailure && !_retryUsed)
            {
                _retryUsed = true;
                _retryAt = DateTime.UtcNow + RetryDelay;
                Main.LogDebug($"Update check hit a network failure; retrying once in {RetryDelay.TotalSeconds:0}s");
            }
        }

        private static void Spawn()
        {
            bool retryDue = _retryAt != DateTime.MinValue && DateTime.UtcNow >= _retryAt;
            if (!retryDue && DateTime.UtcNow - _lastSpawn < SpawnInterval) return;
            _retryAt = DateTime.MinValue;
            // The retry budget is per scheduled check: only a scheduled spawn re-arms it, so a retry that
            // fails again waits out the interval instead of looping.
            if (!retryDue) _retryUsed = false;
            _lastSpawn = DateTime.UtcNow;

            var script = ScriptPath();
            if (script == null) return;

            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Check -CurrentVersion {Main.Version}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // Fire and forget: the answer arrives as a file, and waiting for the process would block
            // the Unity thread on a network round trip.
            Process.Start(psi);
        }

        // Null (logged once) when the launcher never recorded an install path — someone injected by
        // hand, which is normal on a dev machine and simply means no update checks.
        private static string ScriptPath()
        {
            try
            {
                var marker = Path.Combine(DataDir, InstallFileName);
                if (!File.Exists(marker))
                {
                    if (!_installWarned)
                    {
                        _installWarned = true;
                        Main.LogDebug($"Update check idle: no {InstallFileName} — not started from the launcher");
                    }
                    return null;
                }

                var root = File.ReadAllText(marker).Trim();
                if (root.Length == 0) return null;

                var script = Path.Combine(Path.Combine(root, "injector"), "update.ps1");
                return File.Exists(script) ? script : null;
            }
            catch (Exception e)
            {
                Main.LogDebug($"Update script lookup failed: {e.Message}");
                return null;
            }
        }
    }
}
