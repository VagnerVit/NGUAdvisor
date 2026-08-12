using System;
using System.IO;
using System.Text;

namespace NGUAdvisor.Managers
{
    // Dumps the live game state to one readable text file: levels, tiers, balances, and — the reason
    // this exists — the NAMES that only exist inside the running game.
    //
    // Perk, quirk, fruit and AT labels live in the Unity SCENE (`ItopodPerkController.perkName`,
    // `BeastQuestPerkController.quirkName`, `YggdrasilController.fruitName`), not in code and not in
    // the save file. Reading the save with an external tool therefore yields "perk 93 = 1" and no way
    // to learn what perk 93 IS. The advisor is already inside the process with `Character` live, so it
    // is the only thing that can answer, and this is it answering.
    //
    // READ-ONLY. It writes a file and touches no game state.
    //
    // MAIN THREAD ONLY. Every read here is a live Unity object, so the UI button must request an
    // export (Main.RequestStateExport) and let Main.Update() run it — the standing rule for anything
    // reaching the game from a WinForms handler.
    public static class StateExport
    {
        public const string FileName = "state-export.txt";

        public static string FilePath =>
            Path.Combine(Main.GetSettingsDir() ?? ".", FileName);

        // Returns the path written, or null on failure (already logged).
        public static string Write()
        {
            try
            {
                string text = Build();
                string path = FilePath;
                File.WriteAllText(path, text);
                Main.Log($"State exported to {path}");
                return path;
            }
            catch (Exception e)
            {
                Main.LogDebug($"StateExport failed: {e.Message}");
                return null;
            }
        }

        // Every section is individually guarded. A state dump that stops at the first unreadable
        // system is worth much less than one that says "(unavailable)" for that system and carries
        // everything else — the whole point is to have the numbers in hand.
        public static string Build()
        {
            var sb = new StringBuilder();
            var c = Main.Character;

            sb.AppendLine("NGU ADVISOR — STATE EXPORT");
            sb.AppendLine($"build {Main.BuildTag} · {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
            sb.AppendLine();
            if (c == null)
            {
                sb.AppendLine("Character is not available — the game was not ready when this ran.");
                return sb.ToString();
            }

            Section(sb, "PROGRESSION", () => Progression(sb, c));
            Section(sb, "RESOURCES", () => Resources(sb, c));
            Section(sb, "NGU — ENERGY", () => Ngus(sb, c, false));
            Section(sb, "NGU — MAGIC", () => Ngus(sb, c, true));
            Section(sb, "ADVANCED TRAINING", () => AdvancedTraining(sb, c));
            Section(sb, "AUGMENTS", () => Augments(sb, c));
            Section(sb, "DIGGERS", () => Diggers(sb, c));
            Section(sb, "BEARDS", () => Beards(sb, c));
            Section(sb, "ITOPOD PERKS (owned)", () => Perks(sb, c));
            Section(sb, "BEAST QUIRKS (owned)", () => Quirks(sb, c));
            Section(sb, "YGGDRASIL FRUITS", () => Fruits(sb, c));
            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string title, Action body)
        {
            sb.AppendLine(title);
            int before = sb.Length;
            try { body(); }
            catch (Exception e)
            {
                sb.Length = before;
                sb.AppendLine($"  (unavailable — {e.Message})");
            }
            if (sb.Length == before) sb.AppendLine("  (nothing to report)");
            sb.AppendLine();
        }

        private static void Progression(StringBuilder sb, Character c)
        {
            var p = ProgressionAnalyzer.Detect();
            if (p.Known)
            {
                sb.AppendLine($"  {p.Label} · {p.Difficulty}");
                sb.AppendLine($"  activity      {p.Activity}");
                sb.AppendLine($"  next goal     {p.NextGoal}");
                sb.AppendLine($"  recommended   {p.RecommendedProfile} — {p.RecommendReason}");
            }
            sb.AppendLine($"  profile       {Main.Settings?.AllocationFile ?? "-"}");
            // CurrentHighestBoss is the progression read (the repo's standing rule); the raw stat is
            // printed beside it because they diverge on Evil and a state dump should show both.
            sb.AppendLine($"  boss          {ZoneHelpers.CurrentHighestBoss(c)} (raw stats.highestBoss {c.stats.highestBoss})");
            sb.AppendLine($"  ITOPOD floor  {c.adventure.highestItopodLevel} reached");
            sb.AppendLine($"  run time      {NumberFormatter.Duration(c.rebirthTime.totalseconds / 3600.0)}");
            sb.AppendLine($"  NGU track     {c.settings.nguLevelTrack}");
        }

        private static void Resources(StringBuilder sb, Character c)
        {
            sb.AppendLine($"  EXP           {NumberFormatter.Abbrev(c.realExp)}");
            sb.AppendLine($"  AP            {NumberFormatter.Abbrev(c.arbitrary.curArbitraryPoints)}");
            sb.AppendLine($"  PP            {NumberFormatter.Abbrev(c.adventure.itopod.perkPoints)}");
            sb.AppendLine($"  QP            {NumberFormatter.Abbrev(c.beastQuest.quirkPoints)}");
            sb.AppendLine($"  seeds         {NumberFormatter.Abbrev(c.yggdrasil.seeds)}");
            sb.AppendLine($"  gold          {NumberFormatter.Abbrev(c.realGold)}");
            sb.AppendLine($"  energy cap    {NumberFormatter.Abbrev(c.totalCapEnergy())} · power {NumberFormatter.Abbrev(c.totalEnergyPower())}");
            sb.AppendLine($"  magic cap     {NumberFormatter.Abbrev(c.totalCapMagic())} · power {NumberFormatter.Abbrev(c.totalMagicPower())}");
            sb.AppendLine($"  adv power     {NumberFormatter.Abbrev(c.totalAdvAttack())} attack · {NumberFormatter.Abbrev(c.totalAdvDefense())} defense");
            sb.AppendLine($"  cube          {NumberFormatter.Abbrev(c.inventoryController.cubePower())} P / {NumberFormatter.Abbrev(c.inventoryController.cubeToughness())} T");
        }

        // Levels come through NGUAdvisors' track rule, so an Evil run reports the levels it is actually
        // climbing rather than a frozen Normal column. `energy`/`magic` is the ALLOCATION each lane
        // holds — a zero there is why a lane is not moving (see NGUAdvisors.Diagnose).
        private static void Ngus(StringBuilder sb, Character c, bool magic)
        {
            var names = magic ? NGUAdvisors.MNames : NGUAdvisors.ENames;
            int count = magic ? c.NGU.magicSkills.Count : c.NGU.skills.Count;
            for (int id = 0; id < count && id < names.Length; id++)
            {
                var skill = magic ? c.NGU.magicSkills[id] : c.NGU.skills[id];
                long level = c.settings.nguLevelTrack == difficulty.evil ? skill.evilLevel
                           : c.settings.nguLevelTrack == difficulty.sadistic ? skill.sadisticLevel
                           : skill.level;
                long target = c.settings.nguLevelTrack == difficulty.evil ? skill.evilTarget
                            : c.settings.nguLevelTrack == difficulty.sadistic ? skill.sadisticTarget
                            : skill.target;
                long held = magic ? skill.magic : skill.energy;
                sb.AppendLine($"  {names[id],-10} L{level,-12} allocated {NumberFormatter.Abbrev(held),-10}"
                            + (target != 0 ? $" target {target}" : ""));
            }
        }

        private static void AdvancedTraining(StringBuilder sb, Character c)
        {
            string[] slots = { "Toughness", "Power", "Block", "Wandoos Energy", "Wandoos Magic" };
            for (int id = 0; id < slots.Length && id < c.advancedTraining.level.Length; id++)
                sb.AppendLine($"  {slots[id],-16} L{c.advancedTraining.level[id],-10}"
                            + $" allocated {NumberFormatter.Abbrev(c.advancedTraining.energy[id])}");
        }

        private static void Augments(StringBuilder sb, Character c)
        {
            string[] names = { "Safety Scissors", "Milk Infusion", "Cannon Implant", "Shoulder Mounted Minigun",
                               "Energy Buster", "Advanced Exoskeleton", "Laser Sword" };
            for (int id = 0; id < names.Length && id < c.augments.augs.Length; id++)
            {
                var a = c.augments.augs[id];
                sb.AppendLine($"  {names[id],-26} aug L{a.augLevel,-10} upgrade L{a.upgradeLevel}");
            }
        }

        private static void Diggers(StringBuilder sb, Character c)
        {
            var active = c.diggers.activeDiggers;
            for (int id = 0; id < c.diggers.diggers.Count && id < OptimizationAdvisor.DiggerNames.Length; id++)
            {
                var d = c.diggers.diggers[id];
                if (d.maxLevel <= 0) continue;   // never unlocked — noise in a state dump
                sb.AppendLine($"  {OptimizationAdvisor.DiggerNames[id],-8} L{d.curLevel}/{d.maxLevel}"
                            + (active != null && active.Contains(id) ? "  [ACTIVE]" : ""));
            }
            sb.AppendLine($"  slots in use  {(active != null ? active.Count : 0)}");
        }

        // Beards carry THREE level numbers (decomp `Beard`): the live `beardLevel`, `permLevel` that
        // survives rebirth, and `bankedLevel` waiting to be claimed. Printing only one would misread
        // as "the beard is low" when the growth is simply banked.
        private static void Beards(StringBuilder sb, Character c)
        {
            var active = c.beards.activeBeards;
            for (int id = 0; id < c.beards.beards.Count && id < OptimizationAdvisor.BeardNames.Length; id++)
            {
                var b = c.beards.beards[id];
                sb.AppendLine($"  {OptimizationAdvisor.BeardNames[id],-10} L{b.beardLevel,-8}"
                            + $" perm {b.permLevel,-8} banked {b.bankedLevel,-8}"
                            + (active != null && active.Contains(id) ? "  [ACTIVE]" : ""));
            }
        }

        // THE NAMES ARE THE POINT. perkName lives in the scene; a save reader can only ever print ids.
        private static void Perks(StringBuilder sb, Character c)
        {
            var ipc = c.adventureController.itopod;
            var levels = c.adventure.itopod.perkLevel;
            for (int id = 0; id < levels.Count && id < ipc.perkName.Count; id++)
            {
                if (levels[id] <= 0) continue;
                long max = id < ipc.maxLevel.Count ? ipc.maxLevel[id] : 0;
                sb.AppendLine($"  {ipc.perkName[id]?.Trim()} — L{levels[id]}{(max > 0 ? $"/{max}" : "")}");
            }
        }

        private static void Quirks(StringBuilder sb, Character c)
        {
            var qc = c.beastQuestPerkController;
            var levels = c.beastQuest.quirkLevel;
            for (int id = 0; id < levels.Count && id < qc.quirkName.Count; id++)
            {
                if (levels[id] <= 0) continue;
                long max = id < qc.maxLevel.Count ? qc.maxLevel[id] : 0;
                sb.AppendLine($"  {qc.quirkName[id]?.Trim()} — L{levels[id]}{(max > 0 ? $"/{max}" : "")}");
            }
        }

        private static void Fruits(StringBuilder sb, Character c)
        {
            var ycon = c.yggdrasilController;
            var fruits = c.yggdrasil.fruits;
            int cap = ycon.capTier();
            for (int id = 0; id < fruits.Count && id < ycon.fruitName.Count; id++)
            {
                if (fruits[id].maxTier <= 0) continue;
                sb.AppendLine($"  {ycon.fruitName[id]?.Trim()} — tier {fruits[id].maxTier}");
            }
            sb.AppendLine($"  tier cap      {cap}");
        }
    }
}
