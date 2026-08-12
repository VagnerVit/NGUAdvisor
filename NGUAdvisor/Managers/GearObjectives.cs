using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // Phase 1b of the native gear optimizer: the scoring vocabulary.
    //  - Stat: the named stats an objective can target (matches the gear-optimizer's Stat set).
    //  - SpecTypeToStats: the game's `specType` enum value (extracted from Assembly-CSharp) -> stat name(s)
    //    that spec contributes to. (Item Power/Toughness come from capAttack/capDefense, handled by the adapter.)
    //  - Objectives: the selectable presets (their single_factors + multiple_factors), name + stats + exponents.
    public static class GearObjectives
    {
        public static class Stat
        {
            public const string Power = "Power", Toughness = "Toughness", MoveCooldown = "Move Cooldown",
                Respawn = "Respawn", Daycare = "Daycare Speed", GoldDrops = "Gold Drops", DropChance = "Drop Chance",
                QuestDrops = "Quest Drops", SeedGain = "Seed Gain", YggYield = "Yggdrasil Yield",
                EnergyBars = "Energy Bars", EnergyCap = "Energy Cap", EnergyPower = "Energy Power", EnergySpeed = "Energy Speed",
                MagicBars = "Magic Bars", MagicCap = "Magic Cap", MagicPower = "Magic Power", MagicSpeed = "Magic Speed",
                Res3Bars = "Resource 3 Bars", Res3Cap = "Resource 3 Cap", Res3Power = "Resource 3 Power",
                ATSpeed = "Raw AT Speed", AugSpeed = "Raw Augment Speed", BeardSpeed = "Raw Beard Speed",
                HackSpeed = "Raw Hack Speed", NGUSpeed = "Raw NGU Speed", WandoosSpeed = "Raw Wandoos Speed",
                WishSpeed = "Raw Wish Speed", AP = "AP", Experience = "Experience", Cooking = "Cooking";
        }

        // Game specType enum value -> the stat name(s) it feeds. Tiered specs (…2, …3) map to the same stat.
        public static readonly IReadOnlyDictionary<int, string[]> SpecTypeToStats = new Dictionary<int, string[]>
        {
            { 1,  new[]{ Stat.EnergyPower } }, { 16, new[]{ Stat.EnergyPower } }, { 33, new[]{ Stat.EnergyPower } },
            { 2,  new[]{ Stat.EnergySpeed } },
            { 3,  new[]{ Stat.MagicPower } },  { 17, new[]{ Stat.MagicPower } },  { 34, new[]{ Stat.MagicPower } },
            { 4,  new[]{ Stat.MagicSpeed } },
            { 5,  new[]{ Stat.DropChance } },  { 41, new[]{ Stat.DropChance } },
            { 6,  new[]{ Stat.GoldDrops } },   { 7,  new[]{ Stat.GoldDrops } },   { 40, new[]{ Stat.GoldDrops } },
            { 8,  new[]{ Stat.EnergyBars } },  { 18, new[]{ Stat.EnergyBars } },  { 35, new[]{ Stat.EnergyBars } },
            { 9,  new[]{ Stat.MagicBars } },   { 19, new[]{ Stat.MagicBars } },   { 36, new[]{ Stat.MagicBars } },
            { 11, new[]{ Stat.Cooking } },
            { 12, new[]{ Stat.WandoosSpeed } }, { 31, new[]{ Stat.WandoosSpeed } },
            { 13, new[]{ Stat.ATSpeed } },     { 32, new[]{ Stat.ATSpeed } },
            { 14, new[]{ Stat.MoveCooldown } },
            { 15, new[]{ Stat.SeedGain } },
            { 20, new[]{ Stat.EnergyCap } },   { 37, new[]{ Stat.EnergyCap } },
            { 21, new[]{ Stat.MagicCap } },    { 38, new[]{ Stat.MagicCap } },
            { 22, new[]{ Stat.NGUSpeed } },    { 39, new[]{ Stat.NGUSpeed } },
            { 23, new[]{ Stat.Respawn } },
            { 24, new[]{ Stat.Experience } },
            { 25, new[]{ Stat.AP } },
            { 26, new[]{ Stat.BeardSpeed } },  { 42, new[]{ Stat.BeardSpeed } },
            { 27, new[]{ Stat.EnergyPower, Stat.MagicPower, Stat.Res3Power } },   // AllPower
            { 28, new[]{ Stat.EnergyBars, Stat.MagicBars, Stat.Res3Bars } },      // AllPerBar
            { 29, new[]{ Stat.EnergyCap, Stat.MagicCap, Stat.Res3Cap } },         // AllCap
            { 30, new[]{ Stat.AugSpeed } },
            { 43, new[]{ Stat.YggYield } },
            { 44, new[]{ Stat.Daycare } },
            { 45, new[]{ Stat.QuestDrops } },
            { 47, new[]{ Stat.Res3Power } },
            { 48, new[]{ Stat.Res3Bars } },
            { 49, new[]{ Stat.Res3Cap } },
            { 50, new[]{ Stat.HackSpeed } },
            { 51, new[]{ Stat.WishSpeed } },
            // 0 None, 10 BoostRecycle, 46 Blood: not scored.
        };

        public class Objective
        {
            public readonly string Name;
            public readonly string[] Stats;
            public readonly double[] Exponents;   // null => all weight 1
            public Objective(string name, string[] stats, double[] exponents = null) { Name = name; Stats = stats; Exponents = exponents; }
        }

        // Selectable presets (the gear-optimizer's single_factors + multiple_factors, curated).
        public static readonly IReadOnlyList<Objective> Objectives = new List<Objective>
        {
            new Objective("Respawn", new[]{ Stat.Respawn }),
            new Objective("Power", new[]{ Stat.Power }),
            new Objective("Toughness", new[]{ Stat.Toughness }),
            new Objective("Gold Drops", new[]{ Stat.GoldDrops }),
            new Objective("Drop Chance", new[]{ Stat.DropChance }),
            new Objective("Quest Drops", new[]{ Stat.QuestDrops }),
            new Objective("Daycare", new[]{ Stat.Daycare }),
            new Objective("Move Cooldown", new[]{ Stat.MoveCooldown }),
            new Objective("Energy NGU", new[]{ Stat.EnergyCap, Stat.EnergyPower, Stat.NGUSpeed }),
            new Objective("Magic NGU", new[]{ Stat.MagicCap, Stat.MagicPower, Stat.NGUSpeed }),
            new Objective("NGUs", new[]{ Stat.EnergyCap, Stat.EnergyPower, Stat.MagicCap, Stat.MagicPower, Stat.NGUSpeed },
                new[]{ 0.5, 0.5, 0.5, 0.5, 1.0 }),
            new Objective("Hacks", new[]{ Stat.Res3Cap, Stat.Res3Power, Stat.HackSpeed }),
            new Objective("Wishes", new[]{ Stat.EnergyCap, Stat.EnergyPower, Stat.MagicCap, Stat.MagicPower, Stat.Res3Cap, Stat.Res3Power, Stat.WishSpeed },
                new[]{ 0.17, 0.17, 0.17, 0.17, 0.17, 0.17, 1.0 }),
            new Objective("Energy Time Machine", new[]{ Stat.EnergyCap, Stat.EnergyPower }),
            new Objective("Magic Time Machine", new[]{ Stat.MagicCap, Stat.MagicPower }),
            new Objective("Time Machine", new[]{ Stat.EnergyCap, Stat.EnergyPower, Stat.MagicCap, Stat.MagicPower },
                new[]{ 0.5, 0.5, 0.5, 0.5 }),
            new Objective("Blood Rituals", new[]{ Stat.MagicCap, Stat.MagicPower }),
            new Objective("Energy Wandoos", new[]{ Stat.EnergyCap, Stat.WandoosSpeed }),
            new Objective("Magic Wandoos", new[]{ Stat.MagicCap, Stat.WandoosSpeed }),
            // Adventure scores Power/Toughness ONLY. Respawn is deliberately NOT in the product: as a
            // base-zero stat it explodes the score at low totals (16->36 respawn would "double" the score),
            // making the optimizer stack respawn items that are mostly wasted in-game. Respawn coverage is
            // the TopRespawn pin's job (exactly one, best-scoring item), not the objective's.
            // Power carries TWICE the weight of Toughness, because the game does not value them
            // equally: damage is (attack - enemyDefense/2) x multiplier, so kill rate - and with it
            // every drop, every gold pickup, every boss push - is linear in Power while Toughness
            // contributes nothing to it. Toughness only has to clear a survival threshold, and the
            // game's own bars sit near 2:1 in Power's favour (autokill gates: UUG 800K/400K,
            // Walderp 13M/7M; the Beardverse manual gate 1.3M/550K is 2.36:1). An equal-exponent
            // product says "+1% Power == +1% Toughness", which at a typical 2.5:1 stat spread prices
            // a POINT of Toughness ~2.5x above a point of Power - backwards. The honest model is a
            // threshold (Toughness to the zone gate, rest to Power), which a product cannot express;
            // 0.5 is the closest this form gets to the ratio the game actually asks for.
            new Objective("Adventure", new[]{ Stat.Power, Stat.Toughness },
                new[]{ 1.0, 0.5 }),

            // Single-priority production sets (the guide's GO advice: run a loadout per priority + a couple
            // respawn items). Each targets one raw-speed spec so the optimizer packs the best items for it.
            // AT progress = (1 + ATSpeedBonus) × sqrt(EnergyPower) × ... (game formula), so Energy Power
            // rides along at half weight. The GO site's AT factor ALSO carries EnergyCap^1; omitted here
            // deliberately — the advisor's allocator BBs AT (full bars), where extra cap adds no speed.
            // See docs/modules/gear-optimizer-comparison.md.
            new Objective("Advanced Training", new[]{ Stat.ATSpeed, Stat.EnergyPower },
                new[]{ 1.0, 0.5 }),
            new Objective("Augments", new[]{ Stat.AugSpeed }),
            new Objective("Beards", new[]{ Stat.BeardSpeed }),
            new Objective("Wandoos", new[]{ Stat.WandoosSpeed }),
            new Objective("Experience", new[]{ Stat.Experience }),
            new Objective("Cooking", new[]{ Stat.Cooking }),

            // Yggdrasil harvest set: priority Seeds > EXP > Gold > (PP) > AP. Seeds = Seed Gain + Yggdrasil
            // Yield (both boost the harvest). PP has NO gear bonus spec in NGU (perk points come from
            // rebirths, not gear) so it cannot be targeted and is omitted. Priority is expressed as
            // descending exponents = weights in the product score (strong soft priority, not strict).
            new Objective("Yggdrasil", new[]{ Stat.SeedGain, Stat.YggYield, Stat.Experience, Stat.GoldDrops, Stat.AP },
                new[]{ 4.0, 4.0, 3.0, 2.0, 1.0 }),
        };
    }
}
