using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // Validation tool for the native gear optimizer (route C3). Dumps each equipped item's finished stat
    // map (from GameGearAdapter, now using the game's getBonusFactor for exact %s) plus raw objective
    // scores, to compare against the gear-optimizer website's per-item numbers.
    //
    // BOTH VALUATIONS ARE DUMPED, and the split is the point of the tool now. NOW = the item's current
    // boost fill, which is what the optimizer actually ranks and equips. MAXED = boosted to cap, which is
    // what the site's item DB holds -- so MAXED is the row to compare against the website, and NOW is the
    // row that explains a pick the site would not have made. The gap between the two is the boost debt
    // sitting on the loadout.
    public static class GearOptimizerDiagnostic
    {
        private static string Name(int id) => id == 0 ? "-" : $"[{id}]{Main.ItemName(id)}";

        public static void Run()
        {
            try
            {
                var inv = Main.Character.inventory;
                var ic = Main.InventoryController;

                var labels = new List<string>();
                var now = new List<GearScorer.Item>();
                var maxed = new List<GearScorer.Item>();
                void AddSlot(Equipment e, string slot, bool isWeapon)
                {
                    if (e == null || e.id == 0) return;
                    labels.Add($"{slot} [{e.id}] {Main.ItemName(e.id)} (lvl {e.level})");
                    now.Add(GameGearAdapter.BuildItem(e, isWeapon, false));
                    maxed.Add(GameGearAdapter.BuildItem(e, isWeapon, true));
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
                lines.Add("Per-item stat maps (spec %s via game getBonusFactor). NOW = current boost fill (what");
                lines.Add("the optimizer ranks); MAXED = boosted to cap (compare THIS row to the site's item stats):");
                lines.Add("");
                string Fmt(GearScorer.Item it) => it.Stats.Count == 0
                    ? "(no scored stats)"
                    : string.Join(", ", it.Stats.OrderBy(s => s.Key).Select(s => $"{s.Key}={s.Value:0.##}"));
                for (int i = 0; i < labels.Count; i++)
                {
                    lines.Add($"  {labels[i]}");
                    lines.Add($"       NOW   {Fmt(now[i])}");
                    lines.Add($"       MAXED {Fmt(maxed[i])}");
                }

                double offhand = GearOptimizer.OffhandPercent;   // live weapon2Factor()
                var cube = GameGearAdapter.BuildCubeItem();
                var nude = GameGearAdapter.BuildBaseItem();

                // Both valuations, side by side. The NOW block is the live optimizer's own behaviour; the
                // MAXED block is the site oracle, so picks that differ between the blocks are boost debt,
                // not an optimizer bug.
                void Recommend(string title, List<GearScorer.Item> worn, bool asMaxed)
                {
                    var equip = new List<GearScorer.Item>(worn) { cube, nude };
                    lines.Add("");
                    lines.Add($"=== OPTIMIZER RECOMMENDATIONS - {title} (current -> optimized) ===");
                    foreach (var obj in GearObjectives.Objectives)
                    {
                        double curScore = GearScorer.ScoreRaw(equip, obj.Stats, obj.Exponents, offhand);
                        // No pins: this is the optimizer's regression baseline, and a baseline that moves with
                        // the user's pin list cannot show whether a refactor changed the optimizer.
                        var best = GearOptimizer.Optimize(obj, false, new int[0], asMaxed);
                        double gain = curScore > 0 ? best.Score / curScore : 0;
                        lines.Add($"  {obj.Name}:  current={curScore:E4}  optimized={best.Score:E4}  (x{gain:0.###})");
                        lines.Add("      W:" + Name(best.MainWeapon) + (best.OffWeapon != 0 ? " / " + Name(best.OffWeapon) : "")
                            + "  H:" + Name(best.Head) + "  C:" + Name(best.Chest) + "  L:" + Name(best.Legs) + "  B:" + Name(best.Boots));
                        lines.Add("      Acc: " + (best.Accessories.Count == 0 ? "(none)" : string.Join(", ", best.Accessories.Select(Name))));
                    }
                }
                Recommend("NOW (live optimizer: current boost fill)", now, false);
                Recommend("MAXED (site oracle: boosted to cap)", maxed, true);

                lines.Add("");
                lines.Add("NOTE: spec %s match the site; no gear SETS in NGU; cube + nude base included; hard caps");
                lines.Add($"deferred (rarely bind). offhand = live weapon2Factor ({offhand:0.#}%). Compare the MAXED");
                lines.Add("block's picks to the site; the NOW block is what the advisor actually equips.");
                lines.Add("=== end ===");

                var path = Path.Combine(Main.GetSettingsDir(), "logs", "gearopt-diagnostic.log");
                File.WriteAllLines(path, lines);
                Main.Log($"Gear Optimizer Diagnostic written to logs\\gearopt-diagnostic.log ({labels.Count} items).");
            }
            catch (Exception e)
            {
                Main.LogDebug($"Gear diagnostic failed: {e.Message}");
                Main.LogDebug(e.StackTrace);
            }
        }
    }
}
