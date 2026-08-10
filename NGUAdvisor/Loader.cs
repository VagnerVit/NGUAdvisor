using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NGUAdvisor
{
    public class Loader
    {
        // WHY A NAMED HOST OBJECT IS THE "ALREADY INJECTED" SIGNAL.
        //
        // A static bool cannot be one. Injecting a second time loads a SECOND COPY of this assembly,
        // whose statics start fresh, so Init() would have no idea the first Main is already running —
        // and it used to create one unconditionally. Two Mains means two update loops, two allocation
        // passes and two FileSystemWatchers over ONE save. Mono also cannot unload an assembly, so the
        // second inject may quietly resolve to the FIRST copy's code, which is worse: the operator
        // believes a new build is live when the old one still is.
        //
        // GameObject.Find matches by NAME across assemblies, which is the one signal that survives all
        // of that. (It only finds ACTIVE objects — fine: Unload deactivates and destroys the host.)
        //
        // There is deliberately no hot-reload in this project: to run a new build, restart NGU Idle and
        // inject once. See package-release.sh.
        private const string HostName = "NGUAdvisorHost";
        private const string MarkerFile = "injected.txt";

        private static GameObject _load;
        private static Main _reference;

        public static void Init()
        {
            if (GameObject.Find(HostName) != null)
            {
                Debug.LogWarning("NGUAdvisor: already injected — refusing to start a second instance. "
                    + "Restart NGU Idle before injecting a new build; there is no hot-reload.");
                return;
            }

            _load = new GameObject(HostName);
            _reference = _load.AddComponent<Main>();
            Object.DontDestroyOnLoad(_load);
            WriteMarker();
        }

        public static void Unload()
        {
            DeleteMarker();
            _reference.Unload();
            _load.SetActive(false);
            Object.Destroy(_load);
        }

        // An indicator readable from OUTSIDE the game, so "is it injected, and which build?" can be
        // answered without inspecting windows or grepping the log for a build line.
        //
        // The pid is in the file on purpose: a crash or a kill leaves the marker behind, so a reader
        // must be able to tell a live marker from a stale one. Check whether that pid is still an
        // NGUIdle process before trusting it.
        //
        // The path is derived here rather than through Main.GetSettingsDir(), which is only populated
        // once Main.Start() has run — this writes before that.
        private static string MarkerPath() => Path.Combine(
            Path.Combine(Environment.ExpandEnvironmentVariables("%userprofile%/AppData/LocalLow"), "NGUAdvisor"),
            MarkerFile);

        // Marker failures must NEVER stop an injection: it is a convenience for the operator, not part
        // of the load path.
        private static void WriteMarker()
        {
            try
            {
                var path = MarkerPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, string.Join(Environment.NewLine, new[]
                {
                    "build=" + Main.BuildTag,
                    "pid=" + System.Diagnostics.Process.GetCurrentProcess().Id,
                    "injectedUtc=" + DateTime.UtcNow.ToString("o"),
                    "host=" + HostName,
                    "note=stale if that pid is not a running NGUIdle process",
                    ""
                }));
            }
            catch (Exception e) { Debug.LogWarning("NGUAdvisor: could not write the inject marker: " + e.Message); }
        }

        private static void DeleteMarker()
        {
            try { File.Delete(MarkerPath()); }
            catch (Exception e) { Debug.LogWarning("NGUAdvisor: could not remove the inject marker: " + e.Message); }
        }
    }
}
