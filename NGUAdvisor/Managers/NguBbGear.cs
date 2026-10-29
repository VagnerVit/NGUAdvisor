using System;
using System.Collections.Generic;
using System.Linq;
using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using SimpleJSON;

namespace NGUAdvisor.Managers
{
    // Prices a respawn slot in the NGU segment by NGU levels, the thing the segment buys: a set that
    // keeps every lane the auto profile's walk funds at BB loses no level against the plain set. The
    // lanes come first in the walk, so the AT, augment and ritual tokens behind them hold energy the
    // lanes would take back. Main thread only (game getters).
    public static class NguBbGear
    {
        private static readonly GearObjectives.Objective EnergyPower = Single(GearObjectives.Stat.EnergyPower);
        private static readonly GearObjectives.Objective EnergyCap = Single(GearObjectives.Stat.EnergyCap);
        private static readonly GearObjectives.Objective MagicPower = Single(GearObjectives.Stat.MagicPower);
        private static readonly GearObjectives.Objective MagicCap = Single(GearObjectives.Stat.MagicCap);
        private static readonly GearObjectives.Objective NguSpeed = Single(GearObjectives.Stat.NGUSpeed);

        private static GearObjectives.Objective Single(string stat) => new GearObjectives.Objective(stat, new[] { stat });

        public static bool KeepsNguLevels(GearOptimizer.Result set, GearOptimizer.Result plain, double headroom)
            => PoolKept(ResourceType.Energy, set, plain, headroom) && PoolKept(ResourceType.Magic, set, plain, headroom);

        private static bool PoolKept(ResourceType type, GearOptimizer.Result set, GearOptimizer.Result plain, double headroom)
        {
            bool energy = type == ResourceType.Energy;
            GearObjectives.Objective[] stats = { energy ? EnergyPower : MagicPower, energy ? EnergyCap : MagicCap, NguSpeed };
            double[] setF = stats.Select(o => GearOptimizer.ScoreOf(set, o)).ToArray();
            double[] plainF = stats.Select(o => GearOptimizer.ScoreOf(plain, o)).ToArray();
            if (NguBbCoverage.NoWeaker(setF, plainF)) return true;

            List<ResourceBreakpoint> walk = Walk(type);
            int first = walk.FindIndex(bp => bp is NGUBP);
            if (first < 0) return true;
            List<NGUBP> lanes = walk.OfType<NGUBP>().GroupBy(l => l.LaneIndex).Select(g => g.First()).ToList();

            Character c = Main.Character;
            double[] wornF = stats.Select(GearOptimizer.CurrentScore).ToArray();
            double powerFactor = NguBbCoverage.SwapFactor(energy ? EnergyPowerBonus(c) : MagicPowerBonus(c), wornF[0], setF[0]);
            double capFactor = NguBbCoverage.SwapFactor(energy ? EnergyCapBonus(c) : MagicCapBonus(c), wornF[1], setF[1]);
            double nguFactor = NguBbCoverage.SwapFactor(Bonus(c, specType.NGU) + Bonus(c, specType.NGU2), wornF[2], setF[2]);

            double available = (energy ? c.idleEnergy : c.magic.idleMagic)
                + lanes.Sum(l => (double)l.HeldNow)
                + HeldBehind(walk, first, energy)
                + NguBbCoverage.CapChange(energy ? c.totalCapEnergy() : c.totalCapMagic(), capFactor);
            double[] bbs = lanes.Select(l => l.BbWithSpeed(powerFactor * nguFactor)).ToArray();
            bool covers = NguBbCoverage.Covers(available, bbs, headroom);
            LogOnce($"[GearDbg] NGU BB {type}: power x{powerFactor:0.###} cap x{capFactor:0.###} ngu x{nguFactor:0.###} "
                + $"available {available:0.###e0} need {bbs.Sum():0.###e0} (x{headroom}) over {lanes.Count} lane(s) -> {(covers ? "covered" : "short")}");
            return covers;
        }

        private static readonly HashSet<string> _logged = new HashSet<string>();

        private static void LogOnce(string line)
        {
            if (_logged.Count > 64) _logged.Clear();
            if (_logged.Add(line)) Main.LogDebug(line);
        }

        // One respawn decision prices up to three sets per pool; the token list behind them doesn't change in between.
        private static readonly TimeSpan WalkReuse = TimeSpan.FromSeconds(5);
        private static readonly Dictionary<ResourceType, (DateTime At, List<ResourceBreakpoint> Walk)> _walks =
            new Dictionary<ResourceType, (DateTime, List<ResourceBreakpoint>)>();

        private static List<ResourceBreakpoint> Walk(ResourceType type)
        {
            if (_walks.TryGetValue(type, out (DateTime At, List<ResourceBreakpoint> Walk) cached) && DateTime.UtcNow - cached.At < WalkReuse)
                return cached.Walk;
            List<ResourceBreakpoint> walk = ParseWalk(type);
            _walks[type] = (DateTime.UtcNow, walk);
            return walk;
        }

        private static List<ResourceBreakpoint> ParseWalk(ResourceType type)
        {
            JSONArray arr = new JSONArray();
            foreach (string t in ChallengeOverlay.AutoTokens(type)) arr.Add(t);
            return ResourceBreakpoint.ParseBreakpointArray(arr, type).Where(x => x != null && x.IsValid()).ToList();
        }

        // A system counts only when every one of its tokens sits behind the first lane: the guide's
        // pre-T7 CAPALLAT runs AHEAD of the lanes and keeps what it holds.
        private static double HeldBehind(List<ResourceBreakpoint> walk, int firstLane, bool energy)
        {
            bool Behind(Func<ResourceBreakpoint, bool> isSystem)
                => walk.Any(isSystem) && !walk.Take(firstLane).Any(isSystem);
            Character c = Main.Character;
            double held = 0;
            if (energy)
            {
                if (Behind(bp => bp is AdvancedTrainingBP))
                    held += c.advancedTraining.energy.Sum(e => (double)e);
                if (Behind(bp => bp is AugmentBP))
                    held += c.augments.augs.Sum(a => (double)a.augEnergy + a.upgradeEnergy);
            }
            else if (Behind(bp => bp is BR || bp is RitualBP))
                held += c.bloodMagic.ritual.Sum(r => (double)r.magic);
            return held;
        }

        private static double EnergyPowerBonus(Character c)
            => Bonus(c, specType.EnergyPower) + Bonus(c, specType.EnergyPower2) + Bonus(c, specType.EnergyPower3) + Bonus(c, specType.AllPower);
        private static double EnergyCapBonus(Character c)
            => Bonus(c, specType.EnergyCap) + Bonus(c, specType.EnergyCap3) + Bonus(c, specType.AllCap);
        private static double MagicPowerBonus(Character c)
            => Bonus(c, specType.MagicPower) + Bonus(c, specType.MagicPower2) + Bonus(c, specType.MagicPower3) + Bonus(c, specType.AllPower);
        private static double MagicCapBonus(Character c)
            => Bonus(c, specType.MagicCap) + Bonus(c, specType.MagicCap3) + Bonus(c, specType.AllCap);

        private static double Bonus(Character c, specType type)
            => c.inventoryController.bonuses.TryGetValue(type, out float v) ? v : 0;
    }
}
