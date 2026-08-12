using System;
using System.IO;
using System.Reflection;

namespace NGUAdvisor.Managers
{
    // Route C3 Phase 1: writes the embedded goal-loadout presets (Presets/*.json) into the runtime profiles
    // dir on startup so they appear in the profile dropdown and can be toggled. NON-DESTRUCTIVE: only creates
    // files that don't already exist — it never overwrites the user's own profiles. Fully guarded.
    public static class PresetInstaller
    {
        private const string Prefix = "NGUAdvisor.Presets.";

        // Is this profile name one WE ship? Asked by ProfileScout so a recommendation can say whether it
        // picked the user's own file or fell back to a preset. Read off the embedded resource names — the
        // same list InstallMissing writes from — rather than a second hand-kept list of names.
        public static bool IsPreset(string profileName)
        {
            if (string.IsNullOrEmpty(profileName)) return false;
            try
            {
                string res = Prefix + profileName + ".json";
                foreach (var name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                    if (string.Equals(name, res, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception e) { Main.LogDebug($"PresetInstaller.IsPreset: {e.Message}"); }
            return false;
        }

        public static void InstallMissing(string profilesDir)
        {
            try
            {
                if (string.IsNullOrEmpty(profilesDir) || !Directory.Exists(profilesDir)) return;
                var asm = Assembly.GetExecutingAssembly();
                foreach (var res in asm.GetManifestResourceNames())
                {
                    if (!res.StartsWith(Prefix, StringComparison.Ordinal) ||
                        !res.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fileName = res.Substring(Prefix.Length);        // e.g. "Goal-AdvDC.json"
                    var dest = Path.Combine(profilesDir, fileName);
                    if (File.Exists(dest)) continue;                    // never overwrite

                    using (var stream = asm.GetManifestResourceStream(res))
                    {
                        if (stream == null) continue;
                        using (var reader = new StreamReader(stream))
                        {
                            File.WriteAllText(dest, reader.ReadToEnd());
                            Main.Log($"Installed goal preset: {fileName}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Main.LogDebug($"PresetInstaller failed: {e.Message}");
            }
        }
    }
}
