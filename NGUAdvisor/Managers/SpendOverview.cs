using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // "What is each currency saving for, and who decided that?" — one row per spendable currency,
    // each ASKING the module that owns the ordering rather than re-deriving it.
    //
    // WHY THIS EXISTS. The advisor already knew every one of these answers, but each lived behind a
    // different panel, so a currency with no panel open had no visible plan at all — and advice got
    // sourced from the guide instead of from the owning module (2026-08-12: a digger slot was
    // recommended for 100k AP while ApPurchaseAdvisor's own queue said an AP heart was next). Naming
    // the OWNER on every row makes that mistake visible instead of plausible.
    //
    // This file DECIDES NOTHING. It has no ordering, no thresholds and no plan of its own; every
    // verdict below is a straight read of an owner's output. If a row looks wrong, the bug is in the
    // owner, and that is where the fix belongs.
    //
    // MAIN THREAD ONLY — every owner it calls reads live Unity objects.
    public static class SpendOverview
    {
        public class Row
        {
            public string Currency;
            public string Owner;        // the module whose ordering this row reports
            public double Balance;
            public bool Known;          // false = the owner has no answer right now
            public string Next;         // what the balance is being saved for
            public double Cost;
            public bool CostKnown;      // never render Cost as a price when false
            public bool Affordable;
            public string Note;         // gate, phase, or why the plan is idling
        }

        public const string CurrencyAp = "AP";
        public const string CurrencyPp = "PP";
        public const string CurrencyQp = "QP";
        public const string CurrencySeeds = "Seeds";
        public const string CurrencyExp = "EXP";

        private static readonly string[] Currencies = { CurrencyAp, CurrencyPp, CurrencyQp, CurrencySeeds, CurrencyExp };

        public static IList<Row> Rows()
        {
            var rows = new List<Row>();
            foreach (string currency in Currencies) rows.Add(RowFor(currency));
            return rows;
        }

        // One currency only — each owner is a live game read, so a caller that needs a single answer
        // must not pay for the other four.
        public static Row RowFor(string currency)
        {
            switch (currency)
            {
                case CurrencyAp: return Ap();
                case CurrencyPp: return Perks();
                case CurrencyQp: return Quirks();
                case CurrencySeeds: return Seeds();
                case CurrencyExp: return Exp();
            }
            return new Row { Currency = currency, Owner = "-", Note = "unknown currency" };
        }

        // A farm rate expressed as the PURCHASE it brings forward. There is deliberately no scalar
        // exchange rate between PP, boosts and EXP — the rate between them is phase-dependent, so any
        // constant would be wrong for half a run (ItopodFarmAdvisor.md). "1.2k PP/hr" answers nothing
        // on its own; "Faster NGU Energy in ~3h" is the same number in the unit the decision is in.
        //
        // Returns null when there is nothing honest to say. A rendered "0h" or an infinity reads as a
        // real prediction (PpEta's rule), and this line's only value is that its numbers hold.
        public static string Buys(Row row, double perHour)
        {
            if (row == null || !row.Known || !row.CostKnown) return null;
            if (row.Affordable) return $"{row.Next} — affordable now";
            double? hours = PpEta.HoursTo((long)row.Cost, (long)row.Balance, perHour);
            return hours.HasValue ? $"{row.Next} in ~{NumberFormatter.Duration(hours.Value)}" : null;
        }

        private static Row Ap()
        {
            var row = new Row { Currency = "AP", Owner = "ApPurchaseAdvisor" };
            try
            {
                var rec = ApPurchaseAdvisor.Next();
                row.Balance = rec.Balance;
                row.Known = rec.Known;
                row.Cost = rec.Cost;
                row.CostKnown = rec.CostKnown;
                row.Affordable = rec.Affordable;
                if (rec.Known)
                {
                    row.Next = rec.Item.Name;
                    row.Note = $"tier {rec.Item.Tier} · advise-only, never bought automatically";
                }
                else row.Note = "no unowned entry resolved";
            }
            catch (Exception e) { row.Note = Fail(e); }
            return row;
        }

        private static Row Perks()
        {
            var row = new Row { Currency = "PP", Owner = "SpendPlanner" };
            try
            {
                row.Balance = Balance(() => Main.Character.adventure.itopod.perkPoints);
                var buy = SpendPlanner.NextPerk();
                if (buy.Known)
                {
                    row.Known = true;
                    row.Next = buy.Name?.Trim();
                    row.Cost = buy.Cost;
                    row.CostKnown = true;
                    row.Affordable = buy.Affordable;
                    row.Note = $"lvl {buy.CurLevel} → {LevelText(buy.TargetLevel)}";
                }
                else row.Note = Banked(SpendPlanner.NextPerkPlanned());
            }
            catch (Exception e) { row.Note = Fail(e); }
            return row;
        }

        private static Row Quirks()
        {
            var row = new Row { Currency = "QP", Owner = "SpendPlanner" };
            try
            {
                row.Balance = Balance(() => Main.Character.beastQuest.quirkPoints);
                var buy = SpendPlanner.NextQuirk();
                if (buy.Known)
                {
                    row.Known = true;
                    row.Next = buy.Name?.Trim();
                    row.Cost = buy.Cost;
                    row.CostKnown = true;
                    row.Affordable = buy.Affordable;
                    row.Note = $"lvl {buy.CurLevel} → {LevelText(buy.TargetLevel)}";
                }
                else row.Note = Banked(SpendPlanner.NextQuirkPlanned());
            }
            catch (Exception e) { row.Note = Fail(e); }
            return row;
        }

        private static Row Seeds()
        {
            var row = new Row { Currency = "Seeds", Owner = "SpendPlanner" };
            try
            {
                row.Balance = Balance(() => Main.Character.yggdrasil.seeds);
                var buy = SpendPlanner.NextFruit();
                if (buy.Known)
                {
                    row.Known = true;
                    row.Next = buy.Name?.Trim();
                    row.Cost = buy.Cost;
                    row.CostKnown = true;
                    row.Affordable = buy.Affordable;
                    row.Note = $"tier {buy.CurLevel} → {buy.TargetLevel}";
                }
                else row.Note = Banked(SpendPlanner.NextFruitPlanned());
            }
            catch (Exception e) { row.Note = Fail(e); }
            return row;
        }

        // EXP is the one currency with no discrete purchase: the guide spends it as a RATIO walk, so
        // the owner names the stats the next chunk feeds and there is no single price to quote.
        private static Row Exp()
        {
            var row = new Row { Currency = "EXP", Owner = "ExpBalancer" };
            try
            {
                row.Balance = Balance(() => Main.Character.realExp);
                var verdict = ExpBalancer.Analyze();
                row.Known = verdict.Known;
                if (verdict.Known)
                {
                    row.Next = verdict.Balanced ? "on guide ratio — spend anywhere in phase" : verdict.NextNames;
                    row.Note = $"{verdict.Phase} · balance {verdict.BalancePct:0}%";
                    row.Affordable = row.Balance > 0;
                }
                else row.Note = "phase unknown";
            }
            catch (Exception e) { row.Note = Fail(e); }
            return row;
        }

        private static string Banked(SpendPlanner.PlannedBuy planned)
        {
            if (!planned.Known) return "plan complete";
            // Cap first: it is the hard GAME gate (AllYggdrasil.capTier()), while the chapter is only
            // the guide's schedule -- reporting the chapter over a cap block names the wrong cause.
            string gate = planned.CapGated ? "tier cap 24 - Troll Challenge 3x"
                : planned.DifficultyGated ? "higher difficulty"
                : $"chapter {planned.MinChapter}";
            return $"banking for {planned.Name?.Trim()} ({gate})";
        }

        private static string LevelText(long target) =>
            target == long.MaxValue ? "max" : target.ToString();

        private static double Balance(Func<double> read)
        {
            try { return read(); }
            catch { return 0; }
        }

        private static string Fail(Exception e)
        {
            Main.LogDebug($"SpendOverview: {e.Message}");
            return "read failed";
        }

        // One log line per currency whenever the answer CHANGES, so debug.log carries when a plan
        // moved on without repeating five lines on every refresh.
        private static readonly Dictionary<string, string> _logged = new Dictionary<string, string>();

        public static void LogChanges(IList<Row> rows)
        {
            foreach (var row in rows)
            {
                string line = row.Known
                    ? $"{row.Next}" + (row.CostKnown ? $" @ {NumberFormatter.Abbrev(row.Cost)}" : " @ cost unknown")
                      + (row.Affordable ? " (affordable)" : " (keep saving)")
                    : row.Note;
                string prev;
                if (_logged.TryGetValue(row.Currency, out prev) && prev == line) continue;
                _logged[row.Currency] = line;
                Main.LogDebug($"[SpendDbg] {row.Currency} (owner {row.Owner}): {line}");
            }
        }
    }
}
