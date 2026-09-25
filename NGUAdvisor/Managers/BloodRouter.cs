using System;

namespace NGUAdvisor.Managers
{
    public enum BloodRoute { Idle, Gold, Loot, Number }

    // Whether a spell takes part in the split at all.
    public enum SinkMode
    {
        Off = 0,
        On = 1
    }

    public struct SinkInput
    {
        public SinkMode Mode;
        public double Invested;         // goldSpellBlood / lootSpellBlood
        public double MinBlood;         // minGoldBlood() / minLootBlood()
    }

    public struct BudgetInput
    {
        public double BloodOnHand;
        public double Bps;
        public double CapGrowthPerSec;  // blood income grows with the magic cap feeding the rituals
        public double HorizonSec;       // blood still generated before the rebirth
        public bool NumberEligible;     // a rebirth will cash the NUMBER bank
        public double RebirthPower;     // 1 + every blood point cast into NUMBER this run
        public SinkInput Gold, Loot;
    }

    public struct SinkPlan
    {
        public SinkMode Mode;
        public double Invested, TargetBlood;
        public int NowPct, TargetPct;   // the game bonus the invested / target blood buys
        public double NextStepCost;     // blood still needed for the next +1 %; 0 when the plan buys no more
        public double Deficit => Math.Max(0, TargetBlood - Invested);
    }

    public struct BudgetPlan
    {
        public double FutureBlood;      // projected income until the rebirth
        public double TotalBlood;       // everything this run generates: in spells + on hand + future
        public int Spells;              // how many spells share the run's blood
        public double Share;            // the level every spell is filled to (TotalBlood / Spells when none overshoots)
        public SinkPlan Gold, Loot;
        public double NumberInvested, NumberTarget;   // blood in NUMBER (rebirthPower - 1)
        public BloodRoute Route;
    }

    // Blood budget: the run's blood is split EQUALLY between the enabled spells (user decision
    // 2026-09-23). Blood already cast cannot be taken back, so a spell above the level keeps what it
    // has and the rest is levelled across the others (water-filling).
    //
    // Counterfeit and Spaghetti are floored by the game, so blood between two thresholds buys nothing:
    // their share is snapped DOWN to the last whole +1 % it pays for, and the remainder goes to NUMBER,
    // where every point counts. The executor pools blood until the next whole step is paid for and then
    // casts exactly that amount.
    public static class BloodRouter
    {
        // AllBloodMagicController.goldBonus: 1 + floor((log2(b/min)+1)^2) %.
        public static int GoldPct(double invested, double min) =>
            min > 0 && invested >= min ? (int)Math.Floor(Math.Pow(Math.Log(invested / min, 2.0) + 1.0, 2.0) + 1e-9) : 0;

        public static double GoldBloodFor(int pct, double min) =>
            pct <= 0 ? 0 : min * Math.Pow(2.0, Math.Sqrt(pct) - 1.0) * (1 + 1e-12);

        // AllBloodMagicController.lootBonus: 1 + floor(log2(b/min)+1) %.
        public static int LootPct(double invested, double min) =>
            min > 0 && invested >= min ? (int)Math.Floor(Math.Log(invested / min, 2.0) + 1.0 + 1e-9) : 0;

        public static double LootBloodFor(int pct, double min) =>
            pct <= 0 ? 0 : min * Math.Pow(2.0, pct - 1.0) * (1 + 1e-12);

        // Blood pooled over [0, t] with income growing linearly with the magic cap.
        public static double Income(double bps, double growth, double t) =>
            t <= 0 || bps <= 0 ? 0 : bps * t * (1.0 + growth * t / 2.0);

        public static BudgetPlan Plan(in BudgetInput a)
        {
            BudgetPlan plan = new BudgetPlan();
            plan.FutureBlood = Income(a.Bps, a.CapGrowthPerSec, a.HorizonSec);
            double gold = a.Gold.Mode == SinkMode.On ? Math.Max(0, a.Gold.Invested) : 0;
            double loot = a.Loot.Mode == SinkMode.On ? Math.Max(0, a.Loot.Invested) : 0;
            double number = Math.Max(0, a.RebirthPower - 1.0);
            double open = Math.Max(0, a.BloodOnHand) + plan.FutureBlood;

            double[] held = { gold, loot, a.NumberEligible ? number : 0 };
            bool[] on = { a.Gold.Mode == SinkMode.On, a.Loot.Mode == SinkMode.On, a.NumberEligible };
            plan.TotalBlood = open + held[0] + held[1] + held[2];
            plan.Share = Level(held, on, open, out int spells);
            plan.Spells = spells;

            plan.Gold = Close(a.Gold, true, plan.Share);
            plan.Loot = Close(a.Loot, false, plan.Share);
            plan.NumberInvested = number;
            plan.NumberTarget = a.NumberEligible ? number + Math.Max(0, open - plan.Gold.Deficit - plan.Loot.Deficit) : number;
            plan.Route = Route(a, plan);
            return plan;
        }

        // The water level L with sum(max(L, held_i)) = held + open over the enabled spells.
        private static double Level(double[] held, bool[] on, double open, out int spells)
        {
            spells = 0;
            double sum = open;
            for (int i = 0; i < held.Length; i++)
                if (on[i]) { spells++; sum += held[i]; }
            if (spells == 0) return 0;

            bool[] fixedAbove = new bool[held.Length];
            while (true)
            {
                int free = 0;
                double pool = sum;
                for (int i = 0; i < held.Length; i++)
                {
                    if (!on[i]) continue;
                    if (fixedAbove[i]) pool -= held[i];
                    else free++;
                }
                if (free == 0) return 0;
                double level = pool / free;
                bool changed = false;
                for (int i = 0; i < held.Length; i++)
                    if (on[i] && !fixedAbove[i] && held[i] > level) { fixedAbove[i] = true; changed = true; }
                if (!changed) return level;
            }
        }

        private static SinkPlan Close(SinkInput s, bool gold, double share)
        {
            double invested = Math.Max(0, s.Invested);
            int now = gold ? GoldPct(invested, s.MinBlood) : LootPct(invested, s.MinBlood);
            SinkPlan p = new SinkPlan { Mode = s.Mode, Invested = invested, NowPct = now, TargetPct = now, TargetBlood = invested };
            if (s.Mode != SinkMode.On) return p;

            int pct = gold ? GoldPct(Math.Max(invested, share), s.MinBlood) : LootPct(Math.Max(invested, share), s.MinBlood);
            if (pct > now)
            {
                p.TargetPct = pct;
                p.TargetBlood = Math.Max(invested, gold ? GoldBloodFor(pct, s.MinBlood) : LootBloodFor(pct, s.MinBlood));
                double next = gold ? GoldBloodFor(now + 1, s.MinBlood) : LootBloodFor(now + 1, s.MinBlood);
                // The game refuses a cast below the spell's minimum (castGoldSpell / castLootSpell).
                p.NextStepCost = Math.Max(next - invested, s.MinBlood);
            }
            return p;
        }

        // The cheaper next step first: both targets are paid from the same pool anyway, and the cheaper
        // one turns blood into a whole +1 % sooner.
        private static BloodRoute Route(in BudgetInput a, BudgetPlan plan)
        {
            bool gold = plan.Gold.Deficit > 0, loot = plan.Loot.Deficit > 0;
            if (gold && loot) return plan.Gold.NextStepCost <= plan.Loot.NextStepCost ? BloodRoute.Gold : BloodRoute.Loot;
            if (gold) return BloodRoute.Gold;
            if (loot) return BloodRoute.Loot;
            return a.NumberEligible ? BloodRoute.Number : BloodRoute.Idle;
        }
    }
}
