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
            public double RichJerksPct; // current Rich Jerks attack/defense boost, % (the lower of the two)
            public double RichJerksTargetPct; // guide milestone for the current chapter, 0 = none
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
                case "R3 POWER": return "R3.Pow";
                case "R3 CAP": return "R3.Cap";
                case "R3 BARS": return "R3.Bar";
            }
            return name;
        }

        // Levels within this fraction of the leader count as balanced (matches the advisor tolerance).
        private const double BalanceTolerance = 0.75;

        // Order of the nine purchasable stats; Stat.Index and the cost table share it.
        private static readonly string[] StatNames =
        {
            "Energy POWER", "Energy CAP", "Energy BARS",
            "Magic POWER", "Magic CAP", "Magic BARS",
            "R3 POWER", "R3 CAP", "R3 BARS",
        };

        // Used only if the game's pricing methods cannot be reached; mirrors their current bodies.
        private static readonly double[] FallbackCosts = { 150, 1, 80, 450, 3, 240, 15000000, 100000, 8000000 };

        // EXP per power unit, per 250 cap and per bar, read from the game's own custom*Cost methods.
        private static double[] UnitCosts(Character c)
        {
            try
            {
                return new double[]
                {
                    c.energyPurchases.customPowerCost(1), c.energyPurchases.customCapCost(250), c.energyPurchases.customBarCost(1),
                    c.magicPurchases.customPowerCost(1), c.magicPurchases.customCapCost(250), c.magicPurchases.customBarCost(1),
                    c.res3Purchases.customPowerCost(1), c.res3Purchases.customCapCost(250), c.res3Purchases.customBarCost(1),
                };
            }
            catch { return FallbackCosts; }
        }

        // The game stores no Hack Day completion (no counter, no flag) and the guide defines it as an
        // event, not a state, so there is nothing reliable to read. Unknown keeps the E/M-only walk.
        private static ExpRatioTables.HackDayPhase DetectHackDay(Character c) => ExpRatioTables.HackDayPhase.Unknown;

        // Each read is individually guarded: an unreadable signal must degrade to the table's
        // conservative default, not throw the whole balancer away.
        private static void ReadSignals(out int chapter, out bool t5Beaten, out int t6VersionsBeaten)
        {
            chapter = 0;
            try { var prog = ProgressionAnalyzer.Detect(); if (prog.Known) chapter = prog.Chapter; } catch { }
            t5Beaten = false;
            try { t5Beaten = ProgressionAnalyzer.TitanBeaten(4); } catch { }
            // Versions of T6 BEATEN, not the version selected in the zone — `titan6Version` is the
            // difficulty dropdown and never moves on its own (user-reported 2026-09-12).
            t6VersionsBeaten = 0;
            try { t6VersionsBeaten = ZoneHelpers.TitanVersionsBeaten(5); } catch { }
        }

        private struct Stat
        {
            public string Name;
            public double ExpSpent;    // current units × unit cost
            public double TargetShare; // fraction of total EXP this stat should hold
            public int Index;          // 0..8 = ePow,eCap,eBar,mPow,mCap,mBar,rPow,rCap,rBar
            public bool Buyable;       // false when the game gate blocks a buy right now (excluded from balance)
        }

        private static Stat[] Snapshot(Character c, out string phase)
        {
            // magic RESOURCE + custom purchases are permanent (never re-lock on Evil) → all-time highestBoss.
            bool magicUnlocked = c.highestBoss >= 37;
            bool r3On = c.res3 != null && c.res3.res3On;
            double[] cost = UnitCosts(c);
            long hardCap = c.hardCap();
            double[] spent = new double[9];
            spent[0] = Math.Max(0, c.energyPower) * cost[0];
            spent[1] = c.capEnergy / 250.0 * cost[1];
            spent[2] = c.energyBars * cost[2];
            spent[3] = magicUnlocked ? Math.Max(0, c.magic.magicPower) * cost[3] : 0;
            spent[4] = magicUnlocked ? c.magic.capMagic / 250.0 * cost[4] : 0;
            spent[5] = magicUnlocked ? c.magic.magicPerBar * cost[5] : 0;
            spent[6] = r3On ? Math.Max(0, c.res3.res3Power) * cost[6] : 0;
            spent[7] = r3On ? c.res3.capRes3 / 250.0 * cost[7] : 0;
            spent[8] = r3On ? c.res3.res3PerBar * cost[8] : 0;

            // Energy CAP is gated by the game until the cap crosses 100k; a gated stat can't be walked,
            // so it's excluded from the balance so it never pins the % at 0 forever. A cap at the hard
            // cap cannot grow either.
            bool[] buyable =
            {
                true, c.capEnergy >= 100000 && c.capEnergy < hardCap, true,
                magicUnlocked, magicUnlocked && c.magic.capMagic < hardCap, magicUnlocked,
                r3On, r3On && c.res3.capRes3 < hardCap, r3On,
            };

            int chapter; bool t5Beaten; int t6v;
            ReadSignals(out chapter, out t5Beaten, out t6v);
            ExpRatioTables.Targets baseTargets = ExpRatioTables.For(chapter, t5Beaten, t6v, magicUnlocked);
            ExpRatioTables.HackDayPhase hd = r3On ? DetectHackDay(c) : ExpRatioTables.HackDayPhase.Unknown;
            double[] floorSpent = new double[6];
            for (int i = 0; i < 6; i++) floorSpent[i] = buyable[i] ? spent[i] : double.PositiveInfinity;
            bool floorReached = ExpRatioTables.EmFloorReached(hd, floorSpent, cost[0], cost[3], baseTargets);
            ExpRatioTables.Targets tg = ExpRatioTables.For(chapter, t5Beaten, t6v, magicUnlocked, hd, floorReached);
            phase = tg.Phase;

            double[] share =
            {
                tg.PoolE * tg.ShareP, tg.PoolE * tg.ShareC, tg.PoolE * tg.ShareB,
                tg.PoolM * tg.ShareP, tg.PoolM * tg.ShareC, tg.PoolM * tg.ShareB,
                tg.PoolR * ExpRatioTables.R3ShareP, tg.PoolR * ExpRatioTables.R3ShareC, tg.PoolR * ExpRatioTables.R3ShareB,
            };
            Stat[] stats = new Stat[9];
            for (int i = 0; i < 9; i++)
                stats[i] = new Stat { Name = StatNames[i], ExpSpent = spent[i], TargetShare = share[i], Index = i, Buyable = buyable[i] };
            return stats;
        }

        private static double Level(Stat s) => s.TargetShare > 0 ? s.ExpSpent / s.TargetShare : 0;

        private struct RichJerks
        {
            public double NowPct;
            public double TargetPct;
        }

        private static RichJerks ReadRichJerks(Character c)
        {
            int chapter; bool t5Beaten; int t6v;
            ReadSignals(out chapter, out t5Beaten, out t6v);
            return new RichJerks
            {
                NowPct = Math.Min(c.attackBoost, c.defenseBoost) * 100.0,
                TargetPct = ExpRatioTables.RichJerksTargetPct(chapter, t5Beaten, t6v),
            };
        }

        public static Verdict Analyze()
        {
            Verdict v = new Verdict();
            try
            {
                Character c = Main.Character;
                if (c == null || c.highestBoss < 17) return v;   // custom purchases (permanent unlock) before boss 17

                Stat[] stats = Snapshot(c, out string phase);
                RichJerks rj = ReadRichJerks(c);
                v.RichJerksPct = rj.NowPct;
                v.RichJerksTargetPct = rj.TargetPct;
                v.Phase = rj.TargetPct > 0 ? $"{phase} · Rich Jerks {Fmt(rj.NowPct)}% / {Fmt(rj.TargetPct)}%" : phase;
                double minL = double.MaxValue, maxL = 0;
                foreach (Stat s in stats)
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

                List<string> names = new List<string>();
                List<string> shorts = new List<string>();
                if (rj.NowPct < rj.TargetPct) { names.Add("Rich Jerks A/D"); shorts.Add("RJ A/D"); v.Balanced = false; }
                if (v.BalancePct < BalanceTolerance * 100.0)
                {
                    // The next chunk feeds the stats sitting below the balance line, most-behind first.
                    double line = maxL * BalanceTolerance;
                    List<Stat> lag = new List<Stat>();
                    foreach (Stat s in stats)
                        if (s.Buyable && s.TargetShare > 0 && Level(s) < line) lag.Add(s);
                    lag.Sort((a, b) => Level(a).CompareTo(Level(b)));
                    for (int i = 0; i < lag.Count && names.Count < 3; i++) { names.Add(lag[i].Name); shorts.Add(Abbrev(lag[i].Name)); }
                }
                v.NextNames = string.Join(", ", names.ToArray());
                v.NextShort = string.Join(", ", shorts.ToArray());
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
                Stat[] stats = Snapshot(c, out _);
                double[] cost = UnitCosts(c);
                double totalSpent = 0, sumShare = 0;
                foreach (Stat s in stats)
                {
                    if (!s.Buyable || s.TargetShare <= 0) continue;
                    totalSpent += s.ExpSpent;
                    sumShare += s.TargetShare;
                }
                if (sumShare <= 0) return;
                double lbar = totalSpent / sumShare;   // balanced water line from what's already invested

                double Deficit(int idx)
                {
                    Stat s = stats[idx];
                    if (!s.Buyable || s.TargetShare <= 0) return 0;
                    return Math.Max(0, s.TargetShare * lbar - s.ExpSpent);
                }
                int ClampI(double v2) => (int)Math.Max(0, Math.Min(int.MaxValue, v2));
                long CapUnits(int idx) => (long)Math.Max(0, Deficit(idx) / cost[idx] * 250.0);

                var set = c.settings;
                set.customPowerAmount = ClampI(Deficit(0) / cost[0]);
                set.customCapAmount = CapUnits(1);
                set.customBarAmount = ClampI(Deficit(2) / cost[2]);
                set.customMagicPowerAmount = ClampI(Deficit(3) / cost[3]);
                set.customMagicCapAmount = CapUnits(4);
                set.customMagicBarAmount = ClampI(Deficit(5) / cost[5]);
                set.customRes3PowerAmount = ClampI(Deficit(6) / cost[6]);
                set.customRes3CapAmount = CapUnits(7);
                set.customRes3BarAmount = ClampI(Deficit(8) / cost[8]);
            }
            catch { }
        }

        // Rich Jerks milestone buys: the shop is permanent and cheap next to the E/M/R3 pools, so a
        // milestone below the chapter's target is filled first. Returns EXP spent; `budget` caps it.
        private static double BuyRichJerks(Character c, double budget, List<string> log)
        {
            RichJerks rj = ReadRichJerks(c);
            if (rj.TargetPct <= 0) return 0;
            double spentTotal = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                bool attack = pass == 0;
                double nowPct = (attack ? c.attackBoost : c.defenseBoost) * 100.0;
                double share = (budget - spentTotal) / (2 - pass);
                float pct = (float)Math.Floor(Math.Min(rj.TargetPct - nowPct, share / 3.0));
                if (pct < 1f) continue;
                long cost = attack ? c.statBoostPurchases.customAttackCost(pct) : c.statBoostPurchases.customDefenseCost(pct);
                if (cost < 1 || cost > c.realExp) continue;
                c.realExp -= cost;
                if (attack) c.attackBoost += pct / 100f; else c.defenseBoost += pct / 100f;
                spentTotal += cost;
                log.Add($"Rich Jerks {(attack ? "attack" : "defense")} +{Fmt(pct)}%");
            }
            return spentTotal;
        }

        // One walk step: Rich Jerks milestones first, then waterfill the rest of up to `fraction` of
        // banked EXP across the lagging stats — raise the lowest levels first to a common water line.
        // Returns a log-worthy description, or null if nothing was bought. Replicates the game's
        // buyCustom* math per stat (unit costs, rounding, hardCap clamp, unlock gates).
        public static string BuyTick(double fraction)
        {
            try
            {
                Character c = Main.Character;
                if (c == null || c.highestBoss < 17) return null;   // custom purchases: permanent unlock
                WriteCustomPlan(c);

                double tickBudget = c.realExp * fraction;
                List<string> rjLog = new List<string>();
                double rjSpent = BuyRichJerks(c, tickBudget, rjLog);
                string rjText = rjLog.Count > 0 ? string.Join(", ", rjLog.ToArray()) + $" for {Fmt(rjSpent)} EXP" : null;

                Stat[] stats = Snapshot(c, out _);
                double[] cost = UnitCosts(c);
                List<Stat> elig = new List<Stat>();
                foreach (Stat s in stats)
                    if (s.Buyable && s.TargetShare > 0) elig.Add(s);
                if (elig.Count == 0) return rjText;

                elig.Sort((a, b) => Level(a).CompareTo(Level(b)));   // ascending by level

                // Budget is `fraction` of the bank, but never a DEAD ZONE. The old flat "under 100 EXP,
                // skip" floor made small banks permanently unspendable: 959 EXP × 10% = 95, so the tick
                // bought nothing, every minute, while EXP trickled in at +144/hr — and because nothing
                // was ever spent the bank never crossed the floor either. Waiting buys nothing (a
                // purchase is an instant, permanent stat), so the floor is the cheapest unit actually on
                // offer, clamped to what's banked.
                double cheapest = double.MaxValue;
                foreach (Stat s in elig)
                {
                    double u = cost[s.Index];
                    if (u < cheapest) cheapest = u;
                }
                double budgetD = rjSpent > 0 ? tickBudget - rjSpent : Math.Max(tickBudget, cheapest);
                if (budgetD > c.realExp) budgetD = c.realExp;
                if (budgetD > long.MaxValue) budgetD = long.MaxValue;
                long budget = (long)budgetD;
                if (budget < cheapest) return rjText;   // genuinely can't afford a single unit yet

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
                List<string> fed = new List<string>();
                foreach (Stat s in elig)
                {
                    double L = Level(s);
                    if (L >= w) continue;
                    long amt = (long)(s.TargetShare * (w - L));
                    if (amt < 1) continue;
                    long spent = BuyStat(c, s, amt, cost);
                    if (spent > 0) { total += spent; fed.Add(s.Name); }
                }
                if (total <= 0)
                {
                    // The waterfill can slice a small budget into per-stat crumbs that each round down
                    // to zero units. Spend it whole on the most-behind stat one unit is affordable for.
                    foreach (Stat s in elig)
                    {
                        if (cost[s.Index] > budget) continue;
                        long one = BuyStat(c, s, budget, cost);
                        if (one > 0) return Join(rjText, $"{s.Name} for {Fmt(one)} EXP (walking toward ratio)");
                    }
                    return rjText;
                }
                return Join(rjText, $"{string.Join(", ", fed.ToArray())} for {Fmt(total)} EXP (walking toward ratio)");
            }
            catch (Exception e) { Main.LogDebug($"ExpBalancer buy: {e.Message}"); return null; }
        }

        private static string Join(string first, string second) => first == null ? second : first + "; " + second;

        // Replicates the game's buyCustom* math for one stat, spending at most maxExp. Power and bars
        // are per-unit costs; cap is bought in blocks of 250 priced by customCapCost(250), as the game
        // rounds cap to 250s. Returns EXP spent.
        private static long BuyStat(Character c, Stat stat, long maxExp, double[] cost)
        {
            if (maxExp < 1) return 0;
            if (maxExp > c.realExp) maxExp = (long)c.realExp;
            long unitCost = (long)cost[stat.Index];
            if (unitCost < 1) return 0;
            long units = maxExp / unitCost;
            if (units < 1) return 0;
            long spent = units * unitCost;

            switch (stat.Index)
            {
                case 0: c.realExp -= spent; c.energyPower += units; return spent;
                case 2: c.realExp -= spent; c.energyBars += units; return spent;
                case 3: c.realExp -= spent; c.magic.magicPower += units; return spent;
                case 5: c.realExp -= spent; c.magic.magicPerBar += units; return spent;
                case 6: c.realExp -= spent; c.res3.res3Power += units; return spent;
                case 8: c.realExp -= spent; c.res3.res3PerBar += units; return spent;
                case 1:
                    if (c.capEnergy < 100000) return 0;          // game gate
                    c.realExp -= spent;
                    c.capEnergy = AddCap(c.capEnergy, units * 250, c.hardCap());
                    return spent;
                case 4:
                    c.realExp -= spent;
                    c.magic.capMagic = AddCap(c.magic.capMagic, units * 250, c.hardCap());
                    return spent;
                case 7:
                    c.realExp -= spent;
                    c.res3.capRes3 = AddCap(c.res3.capRes3, units * 250, c.hardCap());
                    return spent;
            }
            return 0;
        }

        private static long AddCap(long current, long add, long hardCap) => current + add >= hardCap ? hardCap : current + add;

        // Canonical abbreviation lives in NumberFormatter.Abbrev (finding #31). Kept as a thin public alias
        // because external callers reference ExpBalancer.Fmt.
        public static string Fmt(double v) => NumberFormatter.Abbrev(v);
    }
}
