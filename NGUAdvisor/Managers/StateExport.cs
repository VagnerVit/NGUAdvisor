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
        public const string RequestFileName = "state-export.request";

        public static string FilePath =>
            Path.Combine(Main.GetSettingsDir() ?? ".", FileName);

        public static string RequestPath =>
            Path.Combine(Main.GetSettingsDir() ?? ".", RequestFileName);

        // The export is otherwise reachable only by clicking LOGS > Export state, so nothing outside
        // the game could ask for a fresh snapshot — every check of "what does the advisor actually see
        // right now" had to be inferred from log lines. Same shape as Loader's unload request (and the
        // same reason it has that shape): the caller drops a file, Main.Update() notices it ON THE
        // UNITY THREAD, and the file is deleted as the acknowledgement, so a waiting script can watch
        // it vanish and then read the export.
        public static bool Requested()
        {
            try
            {
                string path = RequestPath;
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (Exception e)
            {
                Main.LogDebug($"StateExport request read failed: {e.Message}");
                return false;
            }
        }

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
            Section(sb, "BEAST QUIRKS", () => Quirks(sb, c));
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
            sb.AppendLine($"  titans beaten {TitansBeaten()}");
            sb.AppendLine($"  run time      {NumberFormatter.Duration(c.rebirthTime.totalseconds / 3600.0)}");
            sb.AppendLine($"  NGU track     {c.settings.nguLevelTrack}");
        }

        // T5..T12, with the highest VERSION beaten for the versioned ones — the two reads that gate
        // the chapter and the guide's E:M ratio, and the pair a `titan{N}Version` misread once made
        // invisible (ProgressionAnalyzer.md). "T6 v1" here means v1 beaten, v2 not.
        private static string TitansBeaten()
        {
            var parts = new System.Collections.Generic.List<string>();
            for (int i = 4; i <= 11; i++)
            {
                if (ZoneHelpers.TitanKills(i) < 1) continue;
                int v = ZoneHelpers.IsVersionedTitan(i) ? ZoneHelpers.TitanVersionsBeaten(i) : 0;
                parts.Add(v > 0 ? $"T{i + 1} v{v}" : $"T{i + 1}");
            }
            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "none past T4";
        }

        // Which guide ratio the EXP buys are walking toward. It is decided from the chapter and the T6
        // version, both a step removed from anything else printed here, so without it a wrong ratio is
        // only visible as EXP going somewhere surprising.
        private static string ExpPhase()
        {
            try
            {
                var v = ExpBalancer.Analyze();
                return v.Known && !string.IsNullOrEmpty(v.Phase) ? $"  · buying toward {v.Phase}" : "";
            }
            catch { return ""; }
        }

        private static void Resources(StringBuilder sb, Character c)
        {
            sb.AppendLine($"  EXP           {NumberFormatter.Abbrev(c.realExp)}{ExpPhase()}");
            sb.AppendLine($"  AP            {NumberFormatter.Abbrev(c.arbitrary.curArbitraryPoints)}");
            sb.AppendLine($"  PP            {NumberFormatter.Abbrev(c.adventure.itopod.perkPoints)}");
            sb.AppendLine($"  QP            {NumberFormatter.Abbrev(c.beastQuest.quirkPoints)}");
            sb.AppendLine($"  seeds         {NumberFormatter.Abbrev(c.yggdrasil.seeds)}");
            sb.AppendLine($"  gold          {NumberFormatter.Abbrev(c.realGold)}");
            sb.AppendLine($"  energy cap    {NumberFormatter.Abbrev(c.totalCapEnergy())} · power {NumberFormatter.Abbrev(c.totalEnergyPower())}");
            sb.AppendLine($"  magic cap     {NumberFormatter.Abbrev(c.totalCapMagic())} · power {NumberFormatter.Abbrev(c.totalMagicPower())}");
            sb.AppendLine($"  adv power     {NumberFormatter.Abbrev(c.totalAdvAttack())} attack · {NumberFormatter.Abbrev(c.totalAdvDefense())} defense");
            sb.AppendLine($"  cube          {NumberFormatter.Abbrev(c.inventoryController.cubePower())} P / {NumberFormatter.Abbrev(c.inventoryController.cubeToughness())} T");
            sb.AppendLine($"  drop chance   {c.lootFactor() * 100:#,0}%{FarmZoneDcText()}");
        }

        // Drop chance is one half of a comparison the advisor makes constantly and nothing else here
        // showed: whether more of it still buys boosts in the zone being farmed (BoostFarmAdvisor.DcFor,
        // which also decides whether the DC digger takes the PP digger's slot).
        private static string FarmZoneDcText()
        {
            try
            {
                int zone = Main.Settings != null ? Main.Settings.SnipeZone : 1000;
                var h = BoostFarmAdvisor.DcFor(zone);
                if (!h.Known) return "";
                string where = $" in {(ZoneHelpers.ZoneList.TryGetValue(zone, out var n) ? n : $"zone {zone}")}";
                return h.Saturated
                    ? $" · boost rolls CAPPED{where} (needs {h.NeedFactor * 100:#,0}%)"
                    : $" · {h.HaveFactor * 100:#,0}% of {h.NeedFactor * 100:#,0}% to cap the boost rolls{where}";
            }
            catch { return ""; }
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

        // EVERY quirk, not just the owned ones. SpendPlanner's QuirkPlan matches the game's list BY
        // NAME, and those names exist only in the Unity scene — so an owned-only dump could confirm
        // what a step bought but never tell you what the steps you have NOT reached are called, which
        // is exactly what is needed to check a plan against the guide.
        private static void Quirks(StringBuilder sb, Character c)
        {
            var qc = c.beastQuestPerkController;
            var levels = c.beastQuest.quirkLevel;
            for (int id = 0; id < levels.Count && id < qc.quirkName.Count; id++)
            {
                long max = id < qc.maxLevel.Count ? qc.maxLevel[id] : 0;
                string cost = "";
                try { cost = $" · {NumberFormatter.Abbrev(qc.quirkCost(id))} QP"; } catch { }
                string req = "";
                try
                {
                    if (id < qc.quirkDifficultyReq.Count && qc.quirkDifficultyReq[id] > difficulty.normal)
                        req = $" · needs {qc.quirkDifficultyReq[id]}";
                }
                catch { }
                sb.AppendLine($"  [{id}] {qc.quirkName[id]?.Trim()} — L{levels[id]}{(max > 0 ? $"/{max}" : "")}{cost}{req}");
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
