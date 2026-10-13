using System;

namespace NGUAdvisor.Managers
{
    // How a pool of energy/magic is shared between NGU lanes. An NGU gains at most one level per tick
    // and loses the overfill (decomp NGUController.updateNGU), so a lane holding A of its BB levels once
    // every ceil(BB/A) ticks: only BB/k amounts buy speed, anything between two of them is wasted.
    // Guide (ch.4-6): "Aim to BB", and only when nothing qualifies "split Magic into Ygg/EXP". So lanes
    // reach BB in priority order while the pool covers them, the rest is split equally, and what the
    // quantised split leaves over lifts lanes to their next breakpoint, again in priority order.
    public static class NguBbSplit
    {
        // NGUBP's own guard against a float-rounded BB landing one unit short of the level.
        public const double RoundingHeadroom = 1.00000202655792;

        public struct Result
        {
            public int[] Ticks;     // ticks per level, 0 = unfunded
            public long[] Amounts;
        }

        // bbAt(lane, k): the lane's BB when it runs at one level per k ticks (a faster lane looks
        // further ahead, so its BB is higher).
        public static Result Plan(Func<int, int, double> bbAt, int lanes, double pool)
        {
            int[] ticks = new int[lanes];
            long[] amounts = new long[lanes];
            double left = pool;

            int first = 0;
            while (first < lanes && Cost(bbAt, first, 1) <= left)
            {
                Fund(bbAt, first, 1, ticks, amounts, ref left);
                first++;
            }

            int rest = lanes - first;
            if (rest > 0)
            {
                double share = left / rest;
                for (int lane = first; lane < lanes; lane++)
                {
                    int k = FastestWithin(bbAt, lane, share);
                    if (k > 0) Fund(bbAt, lane, k, ticks, amounts, ref left);
                }

                for (int lane = first; lane < lanes; lane++)
                {
                    if (ticks[lane] <= 1) continue;
                    left += amounts[lane];
                    Fund(bbAt, lane, FastestWithin(bbAt, lane, left), ticks, amounts, ref left);
                }
            }

            return new Result { Ticks = ticks, Amounts = amounts };
        }

        private static long Cost(Func<int, int, double> bbAt, int lane, int k)
            => (long)Math.Ceiling(Math.Max(1.0, bbAt(lane, k)) / k * RoundingHeadroom);

        private static void Fund(Func<int, int, double> bbAt, int lane, int k, int[] ticks, long[] amounts, ref double left)
        {
            ticks[lane] = k;
            amounts[lane] = Cost(bbAt, lane, k);
            left -= amounts[lane];
        }

        // Smallest k whose cost fits the share; 0 when even one level per int.MaxValue ticks does not.
        // Cost never rises with k (BB only shrinks as the lookahead does), so a bisection finds it: right
        // after a rebirth k runs into the millions and every bbAt reads dozens of game getters.
        private static int FastestWithin(Func<int, int, double> bbAt, int lane, double share)
        {
            if (share < 1 || Cost(bbAt, lane, int.MaxValue) > share) return 0;
            int lo = 1, hi = int.MaxValue;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (Cost(bbAt, lane, mid) <= share) hi = mid;
                else lo = mid + 1;
            }
            return lo;
        }
    }
}
