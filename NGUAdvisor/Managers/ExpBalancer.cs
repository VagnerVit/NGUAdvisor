using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // EXP purchase planner/balancer. The TARGET ratios per guide phase live in ExpRatioTables (a
    // Unity-free, unit-tested table); this class reads the live character, decides which phase it is
    // in, and walks the purchased stats toward those targets. The two ratio conversions people get
    // wrong (P:C:B is a unit ratio, E:M is a VALUE ratio, neither is an EXP split) are documented in
    // the ExpRatioTables class note.
    //
    // WALK-TOWARD-RATIO MODEL (user request — replaces the old "catch the runaway leader" target):
    //   Each stat has a "level" k = ExpSpent / TargetShare (all levels equal == perfect ratio). The
    //   old code anchored every stat's target to the SINGLE highest level, so a stat left ahead by an
    //   earlier ratio phase (the guide's early 1:37.5k:1 pours EXP into CAP) demanded an astronomical
    //   catch-up lump. You can't un-spend that leader, and the ratio is meant to govern EXP going
    //   FORWARD anyway. So BuyTick() now WATERFILLS a small budget across the lagging stats — always
    //   raising the lowest levels first up to a common water line, never referencing the leader. Over
    //   many ticks the floor rises to meet the ceiling and the ratio converges smoothly, in affordable
    //   chunks. Once every level is within band it degenerates to a proportional maintenance buy.
    //   Analyze() reports a balance % (lowest level / highest level) and which stats the next chunk
    //   feeds, so the advisor row shows convergence progress instead of a scary catch-up number.
    public static class ExpBalancer
    {
        public struct Verdict
        {
            public bool Known;
            public bool Balanced;
            public double BalancePct;   // lowest level / highest level, 0..100 (100 == on-ratio)
            public string NextNames;    // stats the next walk chunk will feed, e.g. "Magic CAP, Magic BARS"
            public string NextShort;    // same, abbreviated for tight tiles, e.g. "M.Cap, M.Bar"
            public string Phase;        // guide phase the targets came from, e.g. "3:1 E:M (5:160k:4)"
        }

        // Compact stat labels for narrow readouts (the GROWTH tile sub-line).
        private static string Abbrev(string name)
        {
            switch (name)
            {
                case "Energy POWER": return "E.Pow";
                case "Energy CAP": return "E.Cap";
                case "Energy BARS": return "E.Bar";
                case "Magic POWER": return "M.Pow";
                case "Magic CAP": return "M.Cap";
                case "Magic BARS": return "M.Bar";
            }
            return name;
        }

        // Levels within this fraction of the leader count as balanced (matches the advisor tolerance).
        private const double BalanceTolerance = 0.75;

        // Read the phase signals off the live character and let ExpRatioTables pick the targets.
        // Each read is individually guarded: an unreadable signal must degrade to the table's
        // conservative default, not throw the whole balancer away.
        private static ExpRatioTables.Targets Targets(Character c, bool magicUnlocked)
        {
            int chapter = 0;
            try { var prog = ProgressionAnalyzer.Detect(); if (prog.Known) chapter = prog.Chapter; } catch { }
            bool t5Beaten = false;
            try { t5Beaten = ProgressionAnalyzer.TitanBeaten(4); } catch { }
            int t6v = 1;
            try { t6v = ZoneHelpers.TitanVersion(5); } catch { }
            bool cblock2Done = false;
            try { cblock2Done = c.challenges.hour24Challenge.curCompletions >= 3; } catch { }

            return ExpRatioTables.For(chapter, t5Beaten, t6v, cblock2Done, magicUnlocked);
        }

        private struct Stat
        {
            public string Name;
            public double ExpSpent;    // current units × unit cost
            public double TargetShare; // fraction of total EXP this stat should hold
            public int Index;          // 0..5 = ePow,eCap,eBar,mPow,mCap,mBar
            public bool Buyable;       // false when the game gate blocks a buy right now (excluded from balance)
        }

        private static Stat[] Snapshot(Character c, out string phase)
        {
            // magic RESOURCE + custom purchases are permanent (never re-lock on Evil) → all-time highestBoss.
            bool magicUnlocked = c.highestBoss >= 37;
            double eP = Math.Max(0, c.energyPower) * 150.0;
            double eC = c.capEnergy / 250.0;
            double eB = c.energyBars * 80.0;
            double mP = magicUnlocked ? Math.Max(0, c.magic.magicPower) * 450.0 : 0;
            double mC = magicUnlocked ? c.magic.capMagic * 3.0 / 250.0 : 0;
            double mB = magicUnlocked ? c.magic.magicPerBar * 240.0 : 0;

            var tg = Targets(c, magicUnlocked);
            phase = tg.Phase;
            double pe = tg.PoolE, pm = tg.PoolM;
            // Cap buys are gated by the game until the cap crosses 100k; a gated stat can't be walked,
            // so it's excluded from the balance so it never pins the % at 0 forever.
            var stats = new[]
            {
                new Stat { Name = "Energy POWER", ExpSpent = eP, TargetShare = pe * tg.ShareP, Index = 0, Buyable = true },
                new Stat { Name = "Energy CAP", ExpSpent = eC, TargetShare = pe * tg.ShareC, Index = 1, Buyable = c.capEnergy >= 100000 },
                new Stat { Name = "Energy BARS", ExpSpent = eB, TargetShare = pe * tg.ShareB, Index = 2, Buyable = true },
                new Stat { Name = "Magic POWER", ExpSpent = mP, TargetShare = pm * tg.ShareP, Index = 3, Buyable = magicUnlocked },
                new Stat { Name = "Magic CAP", ExpSpent = mC, TargetShare = pm * tg.ShareC, Index = 4, Buyable = magicUnlocked && c.magic.capMagic >= 100000 },
                new Stat { Name = "Magic BARS", ExpSpent = mB, TargetShare = pm * tg.ShareB, Index = 5, Buyable = magicUnlocked },
            };
            return stats;
        }

        private static double Level(Stat s) => s.TargetShare > 0 ? s.ExpSpent / s.TargetShare : 0;

        public static Verdict Analyze()
        {
            var v = new Verdict();
            try
            {
                var c = Main.Character;
                if (c == null || c.highestBoss < 17) return v;   // custom purchases (permanent unlock) before boss 17

                var stats = Snapshot(c, out var phase);
                v.Phase = phase;
                double minL = double.MaxValue, maxL = 0;
                foreach (var s in stats)
                {
                    if (!s.Buyable || s.TargetShare <= 0) continue;
                    double L = Level(s);
                    if (L < minL) minL = L;
                    if (L > maxL) maxL = L;
                }
                if (maxL <= 0) return v;

                v.Known = true;
                v.BalancePct = minL / maxL * 100.0;
                v.Balanced = v.BalancePct >= BalanceTolerance * 100.0;

                if (!v.Balanced)
                {
                    // The next chunk feeds the stats sitting below the balance line, most-behind first.
                    double line = maxL * BalanceTolerance;
                    var lag = new List<Stat>();
                    foreach (var s in stats)
                        if (s.Buyable && s.TargetShare > 0 && Level(s) < line) lag.Add(s);
                    lag.Sort((a, b) => Level(a).CompareTo(Level(b)));
                    var names = new List<string>();
                    var shorts = new List<string>();
                    for (int i = 0; i < lag.Count && i < 3; i++) { names.Add(lag[i].Name); shorts.Add(Abbrev(lag[i].Name)); }
                    v.NextNames = string.Join(", ", names.ToArray());
                    v.NextShort = string.Join(", ", shorts.ToArray());
                }
            }
            catch (Exception e) { Main.LogDebug($"ExpBalancer: {e.Message}"); }
            return v;
        }

        // Mirror a REACHABLE plan into the game's CUSTOM PURCHASE boxes (user request): bring each
        // lagging stat up to the balance line implied by the EXP already invested (total / sum-of-
        // shares), so the game's own "Buy ALL Custom" button reflects the walk — not the runaway
        // leader's astronomical catch-up. Over-invested stats get 0 (you can't un-spend them).
        private static void WriteCustomPlan(Character c)
        {
            try
            {
                var stats = Snapshot(c, out _);
                double totalSpent = 0, sumShare = 0;
                foreach (var s in stats)
                {
                    if (!s.Buyable || s.TargetShare <= 0) continue;
                    totalSpent += s.ExpSpent;
                    sumShare += s.TargetShare;
                }
                if (sumShare <= 0) return;
                double lbar = totalSpent / sumShare;   // balanced water line from what's already invested

                long Deficit(int idx)
                {
                    var s = stats[idx];
                    if (!s.Buyable || s.TargetShare <= 0) return 0;
                    return (long)Math.Max(0, s.TargetShare * lbar - s.ExpSpent);
                }
                int ClampI(double v2) => (int)Math.Max(0, Math.Min(int.MaxValue, v2));

                var set = c.settings;
                set.customPowerAmount = ClampI(Deficit(0) / 150.0);
                set.customCapAmount = (long)Math.Max(0, Deficit(1) * 250.0);
                set.customBarAmount = ClampI(Deficit(2) / 80.0);
                set.customMagicPowerAmount = ClampI(Deficit(3) / 450.0);
                set.customMagicCapAmount = (long)Math.Max(0, Deficit(4) / 3.0 * 250.0);
                set.customMagicBarAmount = ClampI(Deficit(5) / 240.0);
            }
            catch { }
        }

        // One walk step: waterfill up to `fraction` of banked EXP across the lagging stats — raise the
        // lowest levels first to a common water line. Returns a log-worthy description, or null if
        // nothing was bought. Replicates the game's buyCustom* math per stat (unit costs, rounding,
        // hardCap clamp, unlock gates).
        public static string BuyTick(double fraction)
        {
            try
            {
                var c = Main.Character;
                if (c == null || c.highestBoss < 17) return null;   // custom purchases: permanent unlock
                WriteCustomPlan(c);

                var stats = Snapshot(c, out _);
                var elig = new List<Stat>();
                foreach (var s in stats)
                    if (s.Buyable && s.TargetShare > 0) elig.Add(s);
                if (elig.Count == 0) return null;

                elig.Sort((a, b) => Level(a).CompareTo(Level(b)));   // ascending by level

                // Budget is `fraction` of the bank, but never a DEAD ZONE. The old flat "under 100 EXP,
                // skip" floor made small banks permanently unspendable: 959 EXP × 10% = 95, so the tick
                // bought nothing, every minute, while EXP trickled in at +144/hr — and because nothing
                // was ever spent the bank never crossed the floor either. Waiting buys nothing (a
                // purchase is an instant, permanent stat), so the floor is the cheapest unit actually on
                // offer, clamped to what's banked.
                double cheapest = double.MaxValue;
                foreach (var s in elig)
                {
                    double u = UnitCost(s.Name);
                    if (u < cheapest) cheapest = u;
                }
                double budgetD = Math.Max(c.realExp * fraction, cheapest);
                if (budgetD > c.realExp) budgetD = c.realExp;
                if (budgetD > long.MaxValue) budgetD = long.MaxValue;
                long budget = (long)budgetD;
                if (budget < cheapest) return null;   // genuinely can't afford a single unit yet

                // Waterfill: raise the floor across the lowest levels until the budget runs out.
                double remaining = budget;
                double w = Level(elig[0]);
                double sumShare = 0;
                for (int j = 0; j < elig.Count; j++)
                {
                    sumShare += elig[j].TargetShare;
                    double nextLevel = (j + 1 < elig.Count) ? Level(elig[j + 1]) : double.MaxValue;
                    double costToNext = sumShare * (nextLevel - w);
                    if (nextLevel != double.MaxValue && costToNext <= remaining)
                    {
                        remaining -= costToNext;
                        w = nextLevel;
                    }
                    else
                    {
                        w += remaining / sumShare;   // budget stops the water partway up
                        remaining = 0;
                        break;
                    }
                }

                long total = 0;
                var fed = new List<string>();
                foreach (var s in elig)
                {
                    double L = Level(s);
                    if (L >= w) continue;
                    long amt = (long)(s.TargetShare * (w - L));
                    if (amt < 1) continue;
                    long spent = BuyStat(c, s.Name, amt);
                    if (spent > 0) { total += spent; fed.Add(s.Name); }
                }
                if (total <= 0)
                {
                    // The waterfill can slice a small budget into per-stat crumbs that each round down
                    // to zero units. Spend it whole on the most-behind stat one unit is affordable for.
                    foreach (var s in elig)
                    {
                        if (UnitCost(s.Name) > budget) continue;
                        long one = BuyStat(c, s.Name, budget);
                        if (one > 0) return $"{s.Name} for {Fmt(one)} EXP (walking toward ratio)";
                    }
                    return null;
                }
                return $"{string.Join(", ", fed.ToArray())} for {Fmt(total)} EXP (walking toward ratio)";
            }
            catch (Exception e) { Main.LogDebug($"ExpBalancer buy: {e.Message}"); return null; }
        }

        // Smallest EXP outlay that still moves a stat by the game's own rounding: power/bars are
        // per-unit costs; cap is 250 units per 1 EXP (×3 for magic) and the game rounds to 250s, so
        // 1 EXP (3 for magic) is the smallest cap buy that isn't rounded away. Mirrors BuyStat.
        private static double UnitCost(string name)
        {
            switch (name)
            {
                case "Energy POWER": return 150;
                case "Energy CAP": return 1;
                case "Energy BARS": return 80;
                case "Magic POWER": return 450;
                case "Magic CAP": return 3;
                case "Magic BARS": return 240;
            }
            return double.MaxValue;
        }

        // Replicates the game's buyCustom* math for one stat, spending at most maxExp. Returns EXP spent.
        private static long BuyStat(Character c, string name, long maxExp)
        {
            if (maxExp < 1) return 0;
            if (maxExp > c.realExp) maxExp = (long)c.realExp;

            switch (name)
            {
                case "Energy POWER":
                {
                    long units = maxExp / 150;
                    if (units < 1) return 0;
                    long cost = units * 150;
                    c.realExp -= cost;
                    c.energyPower += units;
                    return cost;
                }
                case "Energy CAP":
                {
                    if (c.capEnergy < 100000) return 0;          // game gate
                    double units = (double)maxExp * 250;         // 250 cap per 1 EXP
                    units -= units % 250;                        // game rounds cap to 250s
                    if (units < 250) return 0;
                    long cost = maxExp;                          // == units / 250 exactly (units is maxExp*250)
                    c.realExp -= cost;
                    if (c.capEnergy + units >= c.hardCap()) c.capEnergy = c.hardCap();
                    else c.capEnergy += (long)units;
                    return cost;
                }
                case "Energy BARS":
                {
                    long units = maxExp / 80;
                    if (units < 1) return 0;
                    long cost = units * 80;
                    c.realExp -= cost;
                    c.energyBars += units;
                    return cost;
                }
                case "Magic POWER":
                {
                    long units = maxExp / 450;
                    if (units < 1) return 0;
                    long cost = units * 450;
                    c.realExp -= cost;
                    c.magic.magicPower += units;
                    return cost;
                }
                case "Magic CAP":
                {
                    if (c.magic.capMagic < 100000) return 0;
                    double units = Math.Floor((double)maxExp * 250 / 3);
                    units -= units % 250;
                    if (units < 250) return 0;
                    long cost = (long)(units / 250 * 3);
                    c.realExp -= cost;
                    if (c.magic.capMagic + units >= c.hardCap()) c.magic.capMagic = c.hardCap();
                    else c.magic.capMagic += (long)units;
                    return cost;
                }
                case "Magic BARS":
                {
                    long units = maxExp / 240;
                    if (units < 1) return 0;
                    long cost = units * 240;
                    c.realExp -= cost;
                    c.magic.magicPerBar += units;
                    return cost;
                }
            }
            return 0;
        }

        // Canonical abbreviation lives in NumberFormatter.Abbrev (finding #31). Kept as a thin public alias
        // because external callers reference ExpBalancer.Fmt.
        public static string Fmt(double v) => NumberFormatter.Abbrev(v);
    }
}
