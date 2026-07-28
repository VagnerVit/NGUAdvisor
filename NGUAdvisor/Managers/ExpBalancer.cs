using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // EXP purchase planner/balancer (guide ch4, verbatim: "Split EXP evenly into energy/magic
    // (3:1 E:M base), using a pow:cap:bar ratio of 5:160k:4").
    //
    // RATIO SEMANTICS (user-verified vs Blaze's Ratioz tab — both are STAT-VALUE ratios, not EXP):
    //   - The 3:1 E:M is the ratio of PURCHASED STAT VALUES. Magic units cost exactly 3x energy
    //     (game code: pow 450 vs 150, cap 3-per-750 vs 1-per-250, bars 240 vs 80 EXP/unit), so an
    //     EVEN 1:1 EXP split is what produces the 3:1 value ratio. Pools are therefore 50/50 in
    //     EXP-space. (Getting this wrong as an EXP-split would drive values to 9:1 and waste EXP.)
    //   - 5:160k:4 pow:cap:bars is also a UNIT ratio; in EXP-space a pool's shares become
    //     power:cap:bars = 750 : 640 : 320 (identical for magic since all its costs scale 3x).
    //   - Guide phase tweaks (post-T6v2: value ratio 2:1 -> EXP 2:3 toward magic; back to 3:1 after
    //     T6v4 accs + BB) are NOT auto-detected yet; the base 3:1 is used throughout. Later: E:M:R3.
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

        // EVEN EXP split (= 3:1 stat-value ratio, since magic units cost 3x). See header comment.
        private const double PoolE = 0.5, PoolM = 0.5;

        // Levels within this fraction of the leader count as balanced (matches the advisor tolerance).
        private const double BalanceTolerance = 0.75;

        // D4 (guide ch.4): post-CBlock2 / T6v2 the target VALUE ratio becomes 2:1, i.e. EXP 2:3
        // toward magic (E 0.4 / M 0.6); reverts to even at T6v4. Detected via T6 titan version with
        // 24HR-completions>=3 as the CBlock2-done proxy.
        private static void Pools(Character c, out double pe, out double pm)
        {
            pe = PoolE; pm = PoolM;
            try
            {
                int t6v = 1;
                try { t6v = ZoneHelpers.TitanVersion(5); } catch { }
                bool cblock2Done = false;
                try { cblock2Done = c.challenges.hour24Challenge.curCompletions >= 3; } catch { }
                if ((t6v >= 2 || cblock2Done) && t6v < 4) { pe = 0.4; pm = 0.6; }
            }
            catch { }
        }
        private const double ShP = 750.0 / 1710, ShC = 640.0 / 1710, ShB = 320.0 / 1710;

        private struct Stat
        {
            public string Name;
            public double ExpSpent;    // current units × unit cost
            public double TargetShare; // fraction of total EXP this stat should hold
            public int Index;          // 0..5 = ePow,eCap,eBar,mPow,mCap,mBar
            public bool Buyable;       // false when the game gate blocks a buy right now (excluded from balance)
        }

        private static Stat[] Snapshot(Character c)
        {
            // magic RESOURCE + custom purchases are permanent (never re-lock on Evil) → all-time highestBoss.
            bool magicUnlocked = c.highestBoss >= 37;
            double eP = Math.Max(0, c.energyPower) * 150.0;
            double eC = c.capEnergy / 250.0;
            double eB = c.energyBars * 80.0;
            double mP = magicUnlocked ? Math.Max(0, c.magic.magicPower) * 450.0 : 0;
            double mC = magicUnlocked ? c.magic.capMagic * 3.0 / 250.0 : 0;
            double mB = magicUnlocked ? c.magic.magicPerBar * 240.0 : 0;

            Pools(c, out var poolE, out var poolM);
            double pe = magicUnlocked ? poolE : 1.0, pm = magicUnlocked ? poolM : 0.0;
            // Cap buys are gated by the game until the cap crosses 100k; a gated stat can't be walked,
            // so it's excluded from the balance so it never pins the % at 0 forever.
            var stats = new[]
            {
                new Stat { Name = "Energy POWER", ExpSpent = eP, TargetShare = pe * ShP, Index = 0, Buyable = true },
                new Stat { Name = "Energy CAP", ExpSpent = eC, TargetShare = pe * ShC, Index = 1, Buyable = c.capEnergy >= 100000 },
                new Stat { Name = "Energy BARS", ExpSpent = eB, TargetShare = pe * ShB, Index = 2, Buyable = true },
                new Stat { Name = "Magic POWER", ExpSpent = mP, TargetShare = pm * ShP, Index = 3, Buyable = magicUnlocked },
                new Stat { Name = "Magic CAP", ExpSpent = mC, TargetShare = pm * ShC, Index = 4, Buyable = magicUnlocked && c.magic.capMagic >= 100000 },
                new Stat { Name = "Magic BARS", ExpSpent = mB, TargetShare = pm * ShB, Index = 5, Buyable = magicUnlocked },
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

                var stats = Snapshot(c);
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
                var stats = Snapshot(c);
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
                double budgetD = c.realExp * fraction;
                if (budgetD > long.MaxValue) budgetD = long.MaxValue;
                long budget = (long)budgetD;
                if (budget < 100) return null;

                var stats = Snapshot(c);
                var elig = new List<Stat>();
                foreach (var s in stats)
                    if (s.Buyable && s.TargetShare > 0) elig.Add(s);
                if (elig.Count == 0) return null;

                elig.Sort((a, b) => Level(a).CompareTo(Level(b)));   // ascending by level

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
                if (total <= 0) return null;
                return $"{string.Join(", ", fed.ToArray())} for {Fmt(total)} EXP (walking toward ratio)";
            }
            catch (Exception e) { Main.LogDebug($"ExpBalancer buy: {e.Message}"); return null; }
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
