using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // NGU value calculation — REVISED (user field report 2026-07-11): the advisor was funding NGUs
    // the Gear Optimizer site scored ~1.04 while a 1.95-rated NGU idled, and E7 Magic / E8 PP /
    // M5 Energy / M6 Adventure-β never ran because the old chapter candidate lists excluded them.
    // Now EVERY unlocked NGU is a candidate and the ranking uses the game's exact math:
    //
    //   levels/hr = power / speedDivider(id) x allocation x multiplierStack / (level+1) x 50 x 3600
    //               (decomp NGUController.progressPerTick — the stack here matches it term for term)
    //   value     = every NGU bonus is 1 + level x boostFactor on the current track (decomp
    //               AllNGUController), so the x/hr score = (1 + f(L+Δ)) / (1 + fL) — the same
    //               per-NGU rating the GO site shows. Respawn (E2) is the one nonlinear curve
    //               (lower is better, hard floors) and is valued by its own curve, so it naturally
    //               drops out at the floor.
    //
    // Selection: iterative equal-share prune. Split the pool over the kept set, drop NGUs whose
    // ratio at their ACTUAL share is under 1.05x/hr, re-split (survivors' shares grow), repeat.
    // The survivors are the lanes worth running; nothing hot -> deepen the top two by rating.
    public static class NGUAdvisors
    {
        public class Entry
        {
            public int Id;
            public string Name;
            public long Level;
            public double Rating;     // x/hr with the FULL pool — the GO-site-comparable score
            public double Ratio;      // x/hr at the equal share it actually gets when running
            public double Lph;        // levels/hr at that share (GrowthPanel's predicted rate)
            public double LphPerUnit; // levels/hr per allocated unit (internal to the prune loop)
        }

        public class Plan
        {
            public bool Known;
            public List<Entry> Energy = new List<Entry>();
            public List<Entry> Magic = new List<Entry>();
            public int[] EnergyTargets = new int[0];
            public int[] MagicTargets = new int[0];
            // Positive-value NGUs that didn't make the hot set, by rating — the surplus-energy
            // lanes (the game hard-caps every NGU at ONE level per tick, so a hot lane can't
            // drink more than its cap amount; leftovers belong in additional lanes, not deeper).
            public int[] EnergySurplus = new int[0];
            public int[] MagicSurplus = new int[0];
            public string Summary = "";
        }

        public static readonly string[] ENames = { "Augs", "Wandoos", "Respawn", "Gold", "Adv-α", "Power-α", "DropCh", "Magic", "PP" };
        public static readonly string[] MNames = { "Ygg", "EXP", "Power-β", "Number", "TM", "Energy", "Adv-β" };

        private static Plan _cache;
        private static DateTime _cacheAt = DateTime.MinValue;

        private static double Mul(Func<double> f)
        {
            try { var v = f(); return v > 0 ? v : 1; } catch { return 1; }
        }

        // The full speed-multiplier stack from the game's progressPerTick (everything independent
        // of the specific NGU): itopod, macguffin, NGU-speed NGUs, diggers, hacks, beast quirks,
        // wishes, cards, troll-challenge x3, sadistic divider. The old version missed the last six.
        private static double SpeedMult(Character c, bool magic)
        {
            double m;
            if (magic)
            {
                m = Mul(() => c.totalNGUSpeedBonus())
                    * Mul(() => c.adventureController.itopod.totalMagicNGUBonus())
                    * Mul(() => c.inventory.macguffinBonuses[5])
                    * Mul(() => c.NGUController.magicNGUBonus())
                    * Mul(() => c.allDiggers.totalMagicNGUBonus())
                    * Mul(() => c.hacksController.totalMagicNGUBonus())
                    * Mul(() => c.beastQuestPerkController.totalMagicNGUSpeed())
                    * Mul(() => c.wishesController.totalMagicNGUSpeed())
                    * Mul(() => c.cardsController.getBonus(cardBonus.magicNGUSpeed));
                try { if (c.allChallenges.trollChallenge.completions() >= 1) m *= 3.0; } catch { }
            }
            else
            {
                m = Mul(() => c.totalNGUSpeedBonus())
                    * Mul(() => c.adventureController.itopod.totalEnergyNGUBonus())
                    * Mul(() => c.inventory.macguffinBonuses[4])
                    * Mul(() => c.NGUController.energyNGUBonus())
                    * Mul(() => c.allDiggers.totalEnergyNGUBonus())
                    * Mul(() => c.hacksController.totalEnergyNGUBonus())
                    * Mul(() => c.beastQuestPerkController.totalEnergyNGUSpeed())
                    * Mul(() => c.wishesController.totalEnergyNGUSpeed())
                    * Mul(() => c.cardsController.getBonus(cardBonus.energyNGUSpeed));
                try { if (c.allChallenges.trollChallenge.sadisticCompletions() >= 1) m *= 3.0; } catch { }
            }
            try
            {
                if (c.settings.nguLevelTrack >= difficulty.sadistic)
                    m /= magic ? c.NGUController.NGUMagic[0].sadisticDivider() : c.NGUController.NGU[0].sadisticDivider();
            }
            catch { }
            return m;
        }

        // Level on the track currently being leveled.
        private static long Level(Character c, bool magic, int id)
        {
            var s = magic ? c.NGU.magicSkills[id] : c.NGU.skills[id];
            switch (c.settings.nguLevelTrack)
            {
                case difficulty.evil: return s.evilLevel;
                case difficulty.sadistic: return s.sadisticLevel;
                default: return s.level;
            }
        }

        // boostFactor for the track being leveled (0 when unreadable -> level-ratio fallback).
        private static double Factor(Character c, bool magic, int id)
        {
            try
            {
                switch (c.settings.nguLevelTrack)
                {
                    case difficulty.evil:
                        return magic ? c.NGUController.evilMagicBoostFactor[id] : c.NGUController.evilEnergyBoostFactor[id];
                    case difficulty.sadistic:
                        return magic ? c.NGUController.sadisticMagicBoostFactor[id] : c.NGUController.sadisticEnergyBoostFactor[id];
                    default:
                        return magic ? c.NGUController.normalMagicBoostFactor[id] : c.NGUController.normalEnergyBoostFactor[id];
                }
            }
            catch { return 0; }
        }

        // Bonus-multiplier ratio for dL more levels: (1 + f(L+dL)) / (1 + fL) — exact for every
        // NGU except Respawn, which has its own capped time-reduction curve.
        private static double ValueRatio(Character c, bool magic, int id, double level, double dL)
        {
            if (dL <= 0) return 1.0;
            if (!magic && id == 2) return RespawnRatio(c, level, dL);
            double f = Factor(c, magic, id);
            if (f > 0) return (1.0 + f * (level + dL)) / (1.0 + f * level);
            return (level + dL + 1.0) / (level + 1.0);
        }

        // Respawn value = respawnTime(old)/respawnTime(new), from the game's exact curve
        // (decomp AllNGUController.respawnBonusNormal/Evil): Normal <=400 linear floored at 0.8,
        // then an asymptote to 0.6; Evil/Sadistic tracks <=10000 floored at 0.925, then to 0.9.
        // At a floor the ratio is 1.0 — a capped Respawn never earns a lane.
        private static double RespawnRatio(Character c, double level, double dL)
        {
            double f = Factor(c, false, 2);
            bool normalTrack = true;
            try { normalTrack = c.settings.nguLevelTrack == difficulty.normal; } catch { }
            double RF(double lvl)
            {
                if (normalTrack)
                {
                    if (lvl <= 400) return Math.Max(0.8, 1.0 - f * lvl);
                    return Math.Max(0.6, 1.0 - (lvl / (lvl * 5.0 + 200000.0) + 0.2));
                }
                if (lvl <= 10000) return Math.Max(0.925, 1.0 - f * lvl);
                return Math.Max(0.9, 1.0 - (lvl / (lvl * 20.0 + 200000.0) + 0.05));
            }
            double now = RF(level), after = RF(level + dL);
            return after > 0 && now > after ? now / after : 1.0;
        }

        public static Plan Compute(int[] energyCandidates, int[] magicCandidates)
        {
            if (_cache != null && (DateTime.UtcNow - _cacheAt).TotalSeconds < 30) return _cache;
            var p = new Plan();
            try
            {
                var c = Main.Character;
                if (c == null || c.NGU == null) { _cache = p; return p; }

                double ePool = Math.Max(1, c.curEnergy);
                double mPool = Math.Max(1, c.magic.curMagic);
                Build(c, energyCandidates, false, ePool, p.Energy);
                Build(c, magicCandidates, true, mPool, p.Magic);

                p.EnergyTargets = Pick(c, p.Energy, false, ePool);
                p.MagicTargets = Pick(c, p.Magic, true, mPool);
                p.EnergySurplus = Surplus(p.Energy, p.EnergyTargets);
                p.MagicSurplus = Surplus(p.Magic, p.MagicTargets);

                string Fmt(List<Entry> l) => l.Count == 0 ? "-"
                    : string.Join(", ", l.Take(3).Select(x => $"{x.Name} ×{Math.Min(x.Rating, 9.99):0.00}/hr").ToArray());
                p.Summary = $"E: {Fmt(p.Energy)} · M: {Fmt(p.Magic)}";
                p.Known = p.Energy.Count > 0 || p.Magic.Count > 0;
            }
            catch (Exception e) { Main.LogDebug($"NGUAdvisors: {e.Message}"); }
            _cache = p;
            _cacheAt = DateTime.UtcNow;
            return p;
        }

        private static void Build(Character c, int[] cands, bool magic, double pool, List<Entry> into)
        {
            if (cands == null || cands.Length == 0) return;
            double mult = SpeedMult(c, magic);
            double power = magic ? Math.Max(1, c.totalMagicPower()) : Math.Max(1, c.totalEnergyPower());
            var names = magic ? MNames : ENames;
            foreach (var id in cands)
            {
                try
                {
                    if (id < 0 || id >= names.Length) continue;
                    long level = Level(c, magic, id);
                    double divider = magic ? c.NGUController.magicSpeedDivider(id) : c.NGUController.energySpeedDivider(id);
                    if (divider <= 0) continue;
                    // progressPerTick = power / divider x allocated x mult / (level+1); 50 ticks/s.
                    double lphPerUnit = power / divider * mult / (level + 1) * 50.0 * 3600.0;
                    double rating = ValueRatio(c, magic, id, level, lphPerUnit * pool);
                    into.Add(new Entry
                    {
                        Id = id,
                        Name = names[id],
                        Level = level,
                        Rating = rating,
                        Ratio = rating,   // refined to the actual share in Pick()
                        LphPerUnit = lphPerUnit
                    });
                }
                catch { }
            }
            into.Sort((a, b) => b.Rating.CompareTo(a.Rating));
        }

        // Rating exactly 1.0 = a capped curve (Respawn at its floor): genuinely worthless even
        // for otherwise-idle energy. Everything else with positive value beats idling.
        private static int[] Surplus(List<Entry> list, int[] targets) =>
            list.Where(x => !targets.Contains(x.Id) && x.Rating > 1.0001)
                .OrderByDescending(x => x.Rating).Select(x => x.Id).ToArray();

        // Equal-share prune to a stable hot set: each pass splits the pool over the keepers and
        // drops anyone under 1.05x/hr AT THAT SHARE — survivors' shares grow, so the loop is
        // monotone and terminates. Prune-only by design (re-admitting on the larger share would
        // oscillate). Nothing hot: deepen the top two by rating.
        private static int[] Pick(Character c, List<Entry> list, bool magic, double pool)
        {
            if (list.Count == 0) return new int[0];
            var keep = new List<Entry>(list);
            for (int iter = 0; iter < 12 && keep.Count > 0; iter++)
            {
                double share = pool / keep.Count;
                foreach (var e in keep)
                {
                    e.Lph = e.LphPerUnit * share;
                    e.Ratio = ValueRatio(c, magic, e.Id, e.Level, e.Lph);
                }
                var hot = keep.Where(x => x.Ratio >= 1.05).ToList();
                if (hot.Count == keep.Count) break;
                if (hot.Count == 0)
                {
                    keep = keep.OrderByDescending(x => x.Rating).Take(2).ToList();
                    double s2 = pool / keep.Count;
                    foreach (var e in keep)
                    {
                        e.Lph = e.LphPerUnit * s2;
                        e.Ratio = ValueRatio(c, magic, e.Id, e.Level, e.Lph);
                    }
                    break;
                }
                keep = hot;
            }
            return keep.OrderByDescending(x => x.Rating).Select(x => x.Id).ToArray();
        }
    }
}
