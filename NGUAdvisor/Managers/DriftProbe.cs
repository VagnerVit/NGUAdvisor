using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace NGUAdvisor.Managers
{
    // Once a minute, compares the advisor's own ports of game formulas with the game's methods on the live
    // character and logs every disagreement to debug.log as "DRIFT [name]: ours X vs game Y". A game patch
    // or a porting slip shows up here instead of as a quietly wrong recommendation. Read-only; main thread.
    public static class DriftProbe
    {
        private const double RelativeTolerance = 1e-4;
        private const double IntervalSeconds = 60.0;

        private static DateTime _lastRun = DateTime.MinValue;
        private static string _lastSignature;
        private static bool _zonePending;

        private static readonly List<string> Findings = new List<string>();

        public static void Tick()
        {
            try
            {
                if ((DateTime.UtcNow - _lastRun).TotalSeconds < IntervalSeconds) return;
                _lastRun = DateTime.UtcNow;
                Character c = Main.Character;
                if (c == null) return;

                Findings.Clear();
                Probe("NGU", () => ProbeNgu(c));
                Probe("Wandoos", () => ProbeWandoos(c));
                Probe("Hacks", () => ProbeHacks(c));
                Probe("AT", () => ProbeAt(c));
                Probe("Cube", () => ProbeCube(c));
                Probe("Blood", () => ProbeBlood(c));
                Probe("Zone", () => ProbeZone(c));

                // The same finding set as last pass is not news; a clean pass is logged once, like UI AUDIT.
                string signature = string.Join("|", Findings);
                if (signature == _lastSignature) return;
                _lastSignature = signature;
                if (Findings.Count == 0) Main.LogDebug("DRIFT: clean");
                else foreach (string line in Findings) Main.LogDebug(line);
            }
            catch (Exception e) { Main.LogDebug($"DRIFT probe failed: {e.Message}"); }
        }

        private static void Probe(string group, Action body)
        {
            try { body(); }
            catch (Exception e) { Findings.Add($"DRIFT [{group}]: probe read failed: {e.Message}"); }
        }

        private static void Check(string name, double ours, double game)
        {
            if (double.IsNaN(ours) || double.IsNaN(game)) { Report(name, ours, game); return; }
            if (double.IsInfinity(ours) || double.IsInfinity(game)) { if (ours != game) Report(name, ours, game); return; }
            double scale = Math.Max(Math.Max(Math.Abs(ours), Math.Abs(game)), 1e-9);
            if (Math.Abs(ours - game) > RelativeTolerance * scale) Report(name, ours, game);
        }

        private static void Report(string name, double ours, double game)
            => Findings.Add($"DRIFT [{name}]: ours {ours.ToString("G6", CultureInfo.InvariantCulture)} vs game {game.ToString("G6", CultureInfo.InvariantCulture)}");

        // The game's xBonus() multiplies a Normal, an Evil and a Sadistic term, the last two only once the
        // rebirth difficulty reaches them, each on its own level and boost factor.
        // Not compared: Respawn (NGUAdvisors.RespawnRatio is a ratio on one track, no absolute to check),
        // Number (timeMulti folded into the factor), Augments / Power / Time Machine (double-valued getters).
        private static void ProbeNgu(Character c)
        {
            if (c.NGU == null || c.NGU.disabled) return;
            int difficulty = (int)c.settings.rebirthDifficulty;
            AllNGUController g = c.NGUController;

            NguPair(c, false, 1, "Wandoos", difficulty, g.wandoosBonus());
            NguPair(c, false, 3, "Gold", difficulty, g.goldBonus());
            NguPair(c, false, 4, "Adventure alpha", difficulty, g.adventureBonus());
            NguPair(c, false, 6, "Drop chance", difficulty, g.lootBonus());
            NguPair(c, false, 7, "Magic NGU", difficulty, g.magicNGUBonus());
            NguPair(c, false, 8, "PP", difficulty, g.PPBonus());
            NguPair(c, true, 0, "Yggdrasil", difficulty, g.yggdrasilBonus());
            NguPair(c, true, 1, "EXP", difficulty, g.expBonus());
            NguPair(c, true, 5, "Energy NGU", difficulty, g.energyNGUBonus());
        }

        private static void NguPair(Character c, bool magic, int id, string label, int difficulty, double game)
        {
            NGU skill = magic ? c.NGU.magicSkills[id] : c.NGU.skills[id];
            AllNGUController g = c.NGUController;
            double ours = 1.0;
            for (int track = 0; track <= difficulty && track <= 2; track++)
            {
                double level = track == 0 ? skill.level : track == 1 ? skill.evilLevel : skill.sadisticLevel;
                double factor = track == 0 ? (magic ? g.normalMagicBoostFactor[id] : g.normalEnergyBoostFactor[id])
                              : track == 1 ? (magic ? g.evilMagicBoostFactor[id] : g.evilEnergyBoostFactor[id])
                              : (magic ? g.sadisticMagicBoostFactor[id] : g.sadisticEnergyBoostFactor[id]);
                ours *= NguGain.Bonus(magic, id, track, level, factor);
            }
            if (!magic && id == 3) ours = Math.Max(ours, 1.0);   // the game floors the gold product at 1
            Check($"NGU {label}", ours, game);
        }

        // WandoosAdvisor.BonusFor is private; reached by reflection so the probe adds nothing to that file.
        private static void ProbeWandoos(Character c)
        {
            if (c.wandoos98 == null) return;
            double game = c.wandoos98Controller.wandoosBonus();
            double ours = 1.0;
            if (!c.wandoos98.disabled && (c.wandoos98.energyLevel != 0 || c.wandoos98.magicLevel != 0))
            {
                MethodInfo bonusFor = typeof(WandoosAdvisor).GetMethod("BonusFor", BindingFlags.NonPublic | BindingFlags.Static);
                if (bonusFor == null) { Findings.Add("DRIFT [Wandoos]: WandoosAdvisor.BonusFor not found"); return; }
                int os = (int)c.wandoos98.os;
                ours = (double)bonusFor.Invoke(null, new object[] { os, (double)c.wandoos98.energyLevel, (double)c.wandoos98.magicLevel });
                if (ours < 1.0) ours = 1.0;
            }
            Check("Wandoos bonus", ours, game);
        }

        private static void ProbeHacks(Character c)
        {
            if (c.hacks == null || c.hacks.hacks == null) return;
            for (int id = 0; id < c.hacks.hacks.Count; id++)
                Check($"Hack {id} levelDivider", HackMath.LevelDivider(c.hacks.hacks[id].level), c.hacksController.levelDivider(id));
        }

        private static void ProbeAt(Character c)
        {
            if (c.advancedTraining == null) return;
            Check("AT attack multiplier", AtMath.StatMultiplier(c.advancedTraining.level[1]), 1.0 + c.advancedTrainingController.adventurePowerBonus(0));
            Check("AT defense multiplier", AtMath.StatMultiplier(c.advancedTraining.level[0]), 1.0 + c.advancedTrainingController.adventureToughnessBonus(0));
        }

        private static void ProbeCube(Character c)
        {
            InventoryController ic = Main.InventoryController;
            if (ic == null || c.inventory == null) return;
            // The game sums the two cube stats as floats, so the probe does too.
            int ours = BoostValueMath.CubeTier((double)(c.inventory.cubePower + c.inventory.cubeToughness));
            int tier = ic.infinityCubeTier();
            Check("Cube tier", ours, tier);
            Check("Cube loot bonus", BoostValueMath.CubeLootBonus(tier), ic.cubeLootBonus());
            Check("Cube gold bonus", BoostValueMath.CubeGoldBonus(tier), ic.cubeGoldBonus());
            Check("Cube hack bonus", BoostValueMath.CubeHackBonus(tier), ic.cubeHackBonus());
            Check("Cube wish bonus", BoostValueMath.CubeWishBonus(tier), ic.cubeWishBonus());
        }

        private static void ProbeBlood(Character c)
        {
            if (c.bloodMagic == null || c.bloodMagicController == null) return;
            RebirthPowerSpell spells = c.bloodMagicController.spells;
            Check("Blood gold %", BloodRouter.GoldPct(c.bloodMagic.goldSpellBlood, spells.minGoldBlood()),
                  (c.bloodMagicController.goldBonus() - 1.0) * 100.0);
            Check("Blood loot %", BloodRouter.LootPct(c.bloodMagic.lootSpellBlood, spells.minLootBlood()),
                  (c.bloodMagicController.lootBonus() - 1.0) * 100.0);
        }

        // ZoneUnlocks is a boss table; the game derives the same answer from the dropdown it builds, and
        // goToMaxZone then steps off a titan zone (ZoneForwardClick.goToMaxZone). The dropdown refreshes
        // lazily after a boss kill, so a disagreement counts only when it persists to the next pass.
        private static void ProbeZone(Character c)
        {
            if (c.adventureController == null || c.adventureController.zoneDropdown == null) return;
            int game = c.adventureController.zoneDropdown.options.Count - 2;
            if (ZoneHelpers.ZoneIsTitan(game)) game--;
            if (game == 45) game -= 2;
            int ours = ZoneHelpers.GetMaxReachableZone(false);
            if (ours == game) { _zonePending = false; return; }
            if (!_zonePending) { _zonePending = true; return; }
            Report("Zone max reachable", ours, game);
        }
    }
}
