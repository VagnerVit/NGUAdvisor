using System;
using System.Collections.Generic;
using System.Linq;

namespace NGUAdvisor.Managers
{
    // Which NGU lane a short pool should fund first. A lane never earns more than one level per tick
    // (decomp NGUController.updateNGU), so its whole hour is bought at BB; the BBs of one tree differ by
    // ~10 000x at the same level, so a lane is worth what its hour at BB earns per unit that BB costs.
    public static class NguLaneRank
    {
        // NGUBP.BbAt(1): the lookahead the allocation sizes a one-level-per-tick lane with.
        public const int BbLookaheadLevels = 500;

        // The hot band: a lane joins at HotEnterRatio and leaves only below HotExitRatio.
        public const double HotEnterRatio = 1.05;
        public const double HotExitRatio = 1.04;

        // A challenger must out-score an incumbent by the margin the hot band asks of a lane's growth,
        // or near-equal lanes swap the slot every plan.
        public const double IncumbentEdge = (HotEnterRatio - 1.0) / (HotExitRatio - 1.0);

        public struct Lane
        {
            public int Id;
            public double RatioAtBb;   // bonus x/hr while the lane levels once per tick
            public double BbCost;      // energy/magic that holds it there
        }

        // Mirrors NGUBP.EnergyBb/MagicBb(BbLookaheadLevels) term for term: speedMult is the same
        // speed stack, without power.
        public static double BbCost(double speedDivider, double level, double power, double speedMult)
        {
            double bb = Math.Ceiling(speedDivider * (level + 1.0 + BbLookaheadLevels) / (power * speedMult));
            return bb < 1.0 ? 1.0 : bb;
        }

        // Log growth per unit: bonuses multiply, so two lanes' hours compare as ln(ratio).
        public static double Score(Lane lane)
            => lane.RatioAtBb > 1.0 && lane.BbCost > 0 ? Math.Log(lane.RatioAtBb) / lane.BbCost : 0.0;

        public static int[] Order(IEnumerable<Lane> lanes)
            => lanes.OrderByDescending(Score).ThenBy(l => l.Id).Select(l => l.Id).ToArray();

        // The `count` best lanes, an incumbent scored IncumbentEdge higher; returned in Order.
        public static int[] Top(IList<Lane> lanes, int count, ICollection<int> incumbents)
        {
            double Held(Lane l) => incumbents != null && incumbents.Contains(l.Id) ? Score(l) * IncumbentEdge : Score(l);
            List<Lane> picked = lanes.OrderByDescending(Held).ThenBy(l => l.Id).Take(count).ToList();
            return Order(picked);
        }
    }
}
