using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // Validation tool for the native gear optimizer (route C3). Dumps each equipped item's finished stat
    // map (from GameGearAdapter, now using the game's getBonusFactor for exact %s) plus raw objective
    // scores, to compare against the gear-optimizer website's per-item numbers.
    public static class GearOptimizerDiagnostic
    {
        private static string Name(int id) => id == 0 ? "-" : $"[{id}]{Main.ItemName(id)}";

        public static void Run()
        {
            try
            {
                var inv = Main.Character.inventory;
                var ic = Main.InventoryController;

                var entries = new List<KeyValuePair<string, GearScorer.Item>>();
                void AddSlot(Equipment e, string slot, bool isWeapon)
                {
                    if (e == null || e.id == 0) return;
                    entries.Add(new KeyValuePair<string, GearScorer.Item>(
                        $"{slot} [{e.id}] {Main.ItemName(e.id)} (lvl {e.level})",
                        GameGearAdapter.BuildItem(e, isWeapon)));
                }

                AddSlot(inv.weapon, "Weapon", true);
                if (ic.weapon2Unlocked()) AddSlot(inv.weapon2, "Weapon2(off)", true);
                AddSlot(inv.head, "Head", false);
                AddSlot(inv.chest, "Chest", false);
                AddSlot(inv.legs, "Legs", false);
                AddSlot(inv.boots, "Boots", false);
                if (inv.accs != null)
                    for (int i = 0; i < inv.accs.Count; i++) AddSlot(inv.accs[i], $"Acc{i}", false);

                var lines = new List<string>();
                lines.Add("=== NGUAdvisor Gear Optimizer Diagnostic ===");
                lines.Add($"Time: {DateTime.Now}");
                lines.Add("Per-item stat maps (spec %s via game getBonusFactor; compare to the site's item stats):");
                lines.Add("");
                foreach (var kv in entries)
                {
                    var stats = kv.Value.Stats.Count == 0
                        ? "(no scored stats)"
                        : string.Join(", ", kv.Value.Stats.OrderBy(s => s.Key).Select(s => $"{s.Key}={s.Value:0.##}"));
                    lines.Add($"  {kv.Key}");
                    lines.Add($"       {stats}");
                }

                // Current equipped loadout score (cube + nude base included) per objective.
                var equip = entries.Select(x => x.Value).ToList();
                equip.Add(GameGearAdapter.BuildCubeItem());
                equip.Add(GameGearAdapter.BuildBaseItem());
                double offhand = GearOptimizer.OffhandPercent;   // live weapon2Factor()

                lines.Add("");
                lines.Add("=== OPTIMIZER RECOMMENDATIONS (current -> optimized; compare picks to the website) ===");
                foreach (var obj in GearObjectives.Objectives)
                {
                    double curScore = GearScorer.ScoreRaw(equip, obj.Stats, obj.Exponents, offhand);
                    var best = GearOptimizer.Optimize(obj);
                    double gain = curScore > 0 ? best.Score / curScore : 0;
                    lines.Add($"  {obj.Name}:  current={curScore:E4}  optimized={best.Score:E4}  (x{gain:0.###})");
                    lines.Add("      W:" + Name(best.MainWeapon) + (best.OffWeapon != 0 ? " / " + Name(best.OffWeapon) : "")
                        + "  H:" + Name(best.Head) + "  C:" + Name(best.Chest) + "  L:" + Name(best.Legs) + "  B:" + Name(best.Boots));
                    lines.Add("      Acc: " + (best.Accessories.Count == 0 ? "(none)" : string.Join(", ", best.Accessories.Select(Name))));
                }
                lines.Add("");
                lines.Add("NOTE: spec %s match the site; no gear SETS in NGU; cube + nude base included; hard caps");
                lines.Add($"deferred (rarely bind). offhand = live weapon2Factor ({offhand:0.#}%). Compare picks to the site.");
                lines.Add("=== end ===");

                var path = Path.Combine(Main.GetSettingsDir(), "logs", "gearopt-diagnostic.log");
                File.WriteAllLines(path, lines);
                Main.Log($"Gear Optimizer Diagnostic written to logs\\gearopt-diagnostic.log ({entries.Count} items).");
            }
            catch (Exception e)
            {
                Main.LogDebug($"Gear diagnostic failed: {e.Message}");
                Main.LogDebug(e.StackTrace);
            }
        }
    }
}
