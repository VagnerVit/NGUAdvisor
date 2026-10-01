using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

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
            // The dump is diffed and read by scripts, so it must not follow the player's locale
            // ("1553879,38", "1,735E+011"). Main thread only, restored on the way out.
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            try { return BuildInvariant(); }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }

        private static string BuildInvariant()
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
            Section(sb, "BLOOD BUDGET", () => BloodBudget(sb, c));
            Section(sb, "DROP CHANCE — WHERE IT COMES FROM", () => DropChanceBreakdown(sb, c));
            Section(sb, "GEAR", () => Gear(sb, c));
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

        // EVERY FACTOR IN `Character.lootFactor`, TERM BY TERM, IN THE GAME'S OWN ORDER.
        //
        // Drop chance is a product, so "where do I get more" is not answerable from the total — a term
        // sitting at 1.00 is either locked, unbought, or capped, and the three want completely different
        // actions. Several of them are hard-gated on difficulty (the hacks term and the ITOPOD `drop2`
        // half return 1 below Evil no matter what you own), which is invisible in any aggregate.
        //
        // Read straight off the live controllers rather than re-derived, so this cannot drift from what
        // the game bills: if the listed factors stop multiplying out to the total, the decompiled
        // formula this mirrors has changed.
        private static void DropChanceBreakdown(StringBuilder sb, Character c)
        {
            var ic = c.inventoryController;
            bool preEvil = c.settings.rebirthDifficulty < difficulty.evil;

            void Row(string name, double factor, string note = "")
                => sb.AppendLine($"  {name,-14}{factor,10:0.####}x{(note.Length > 0 ? "   " + note : "")}");

            double gear = 1.0 + ic.bonuses[specType.Looting] + ic.bonuses[specType.Looting2] + ic.cubeLootBonus();

            Row("ITOPOD", c.adventureController.itopod.totalDropChanceBonus(),
                preEvil ? "newbie perk only — the drop2 half needs Evil" : "");
            Row("macguffin", c.inventory.macguffinBonuses[10]);
            Row("gear", gear, "Looting + Looting2 + cube — what the optimizer's Drop Chance score measures");
            Row("blood", c.bloodMagicController.lootBonus());
            Row("yggdrasil", c.yggdrasilController.luckBonus());
            Row("NGU", c.NGUController.lootBonus());
            Row("beards", c.allBeards.lootBonus());
            // Same shape as the cards line: the game's own getter returns a flat 1 while the Drop Chance
            // digger sits inactive, and `skipCheck` is the overload that answers what it WOULD supply at
            // the level it is already sitting on. A digger benched by the profile's pool is a factor that
            // can be switched on today, which a bare 1.00 hides completely.
            bool dcDiggerOn = c.diggers.diggers[0].active;
            Row("diggers", c.allDiggers.totalDropChanceBonus(),
                dcDiggerOn ? "" : $"Drop Chance digger is NOT active — at L{c.diggers.diggers[0].curLevel} it would give "
                                + $"{c.allDiggers.totalDropChanceBonus(0, true):0.####}x");
            Row("hacks", c.hacksController.totalDropChanceBonus(), preEvil ? "LOCKED — returns 1 below Evil" : "");
            // getBonus returns a flat 1 while the cards menu is off, which reads identically to "you own
            // nothing" — so when it IS off, report the bonus the equipped cards would be supplying.
            // That is the difference between a term with no headroom and a term switched off.
            double cardsRaw = c.cards.bonuses[(int)cardBonus.dropChance];
            Row("cards", c.cardsController.getBonus(cardBonus.dropChance),
                c.cards.cardsOn ? "" : $"cards are OFF — the equipped set would give {cardsRaw:0.####}x");
            // Set bonuses are all-or-nothing, so an incomplete one is worth naming: it is a factor
            // sitting at exactly 1.00 that a finished collection would switch on whole.
            Row("2D set", c.inventory.itemList.twoDComplete ? 1.0743 : 1.0,
                c.inventory.itemList.twoDComplete ? "" : "incomplete — worth 1.0743x when finished");
            Row("bonus acc set", c.inventory.itemList.normalBonusAccComplete ? 1.25 : 1.0,
                c.inventory.itemList.normalBonusAccComplete ? "" : "incomplete — worth 1.25x when finished");
            if (c.arbitrary.lootcharm1Time.totalseconds > 0.0)
                Row("loot potion", c.allArbitrary.potionModifier(), "ACTIVE — expires, so the total is temporarily inflated");

            sb.AppendLine($"  {"TOTAL",-14}{c.lootFactor(),10:0.####}x");
        }

        // Drop chance is one half of a comparison the advisor makes constantly and nothing else here
        // showed: whether more of it still buys boosts in the zone being farmed (BoostFarmAdvisor.DcFor,
        // which also decides whether the DC digger takes the PP digger's slot).
        private static string FarmZoneDcText()
        {
            try
            {
                int zone = Main.Settings != null ? Main.ResolveIntentZone(out _) : QuestStandDown.ItopodZone;
                if (zone >= QuestStandDown.ItopodZone)
                    return " · farming ITOPOD: boost rolls are a flat 14 %, drop chance does not apply there";
                var h = BoostFarmAdvisor.DcFor(zone);
                if (!h.Known) return "";
                string where = $" in {(ZoneHelpers.ZoneList.TryGetValue(zone, out var n) ? n : $"zone {zone}")}";
                // The gear half of the same comparison: what the worn accessories supply against what
                // they would have to supply for the zone to cap. This is the number the optimizer's
                // Drop Chance trim acts on, so it belongs next to the headroom it comes from.
                string gear = "";
                var g = BoostFarmAdvisor.GearLootFor(zone);
                if (g.Known)
                    gear = $" · gear {g.Current:0.##}x of {g.Target:0.##}x needed";

                return h.Saturated
                    ? $" · boost rolls CAPPED{where} (needs {h.NeedFactor * 100:#,0}%){gear}"
                    : $" · {h.HaveFactor * 100:#,0}% of {h.NeedFactor * 100:#,0}% to cap the boost rolls{where}{gear}";
            }
            catch { return ""; }
        }

        // What the gear pass last settled on and what is actually worn, so "why is this on my back"
        // is answerable without replaying debug.log. The chain and verdict are AdvisorApply's own
        // last values (empty until the first pass after a load); the items are read live.
        private static void Gear(StringBuilder sb, Character c)
        {
            string chain = AdvisorApply.LastGearChain;
            sb.AppendLine($"  chain         {(string.IsNullOrEmpty(chain) ? "(none applied since load)" : chain)}");
            string verdict = AdvisorApply.LastGearVerdict;
            if (!string.IsNullOrEmpty(verdict))
                sb.AppendLine($"  last verdict  {verdict}");

            string setBy;
            int zone = Main.ResolveIntentZone(out setBy);
            string venue = zone >= QuestStandDown.ItopodZone ? "ITOPOD"
                : ZoneHelpers.ZoneList.TryGetValue(zone, out var zoneName) ? zoneName : $"zone {zone}";
            sb.AppendLine($"  venue         {venue} · {(setBy != null ? "set by " + setBy : "SnipeZone")}"
                        + $" · farm mode {FarmMode.Caption(FarmMode.Current())} · standing in zone {c.adventure.zone}");

            var ic = c.inventoryController;
            sb.AppendLine($"  cube          Power {c.inventory.cubePower:0.##} / softcap {ic.cubePowerSoftcap():0.##}"
                        + $" · Toughness {c.inventory.cubeToughness:0.##} / softcap {ic.cubeToughnessSoftcap():0.##}");

            foreach (ih item in c.inventory.GetConvertedEquips())
            {
                BoostsNeeded need = item.equipment.GetNeededBoosts();
                string fill = need.Total() <= 0f
                    ? "boosts full"
                    : $"boosts to green: P {need.power:0.##} T {need.toughness:0.##} S {need.special:0.##}";
                sb.AppendLine($"  {GearSlotName(item.slot),-10} {Main.ItemNameNice(item.id)} (#{item.id}) L{item.level} · {fill}");
            }
        }

        // Negative slots are the fixed equipment slots (Extensions.GetConvertedEquips); 10000+ are accessories.
        private static string GearSlotName(int slot)
        {
            switch (slot)
            {
                case -1: return "head";
                case -2: return "chest";
                case -3: return "legs";
                case -4: return "boots";
                case -5: return "weapon";
                case -6: return "weapon 2";
                default: return "accessory";
            }
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

        // The plan the blood routing follows, plus the live inputs that shaped it.
        private static void BloodBudget(StringBuilder sb, Character c)
        {
            BloodPlanner.Plan p = BloodPlanner.Analyze();
            BloodPlanner.FillRouting(ref p);
            if (!p.RouteKnown) return;
            BudgetPlan b = p.Budget;
            sb.AppendLine($"  bps {c.bloodMagicController.totalBloodGainedPerSecond():E3}  on hand {c.bloodMagic.bloodPoints:E3}  future {b.FutureBlood:E3}  total {b.TotalBlood:E3}  spells {b.Spells}  share {b.Share:E3}");
            sb.AppendLine($"  Counterfeit {b.Gold.Mode,-3} in {b.Gold.Invested:E3} ({b.Gold.NowPct}%)  plan {b.Gold.TargetBlood:E3} ({b.Gold.TargetPct}%)");
            sb.AppendLine($"  Spaghetti   {b.Loot.Mode,-3} in {b.Loot.Invested:E3} ({b.Loot.NowPct}%)  plan {b.Loot.TargetBlood:E3} ({b.Loot.TargetPct}%)");
            sb.AppendLine($"  NUMBER          in {b.NumberInvested:E3}  plan {b.NumberTarget:E3}");
            sb.AppendLine($"  route {b.Route}{(p.PoolForPill ? " (pooling for pill)" : "")} — {p.RouteReason}");
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
