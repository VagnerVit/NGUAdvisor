using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // The auto profile's R3 order, Unity-free (HackPlanTests). Guide ch.5 "Hacks":
    //   "Post-T7, run A/D Hack until completing CBlock 3"
    //   "Post-CBlock3, run Adventure Hack. Focus Adv Hack as your default hack to push"
    //   "After maxing Incriminating Evidence, get the first milestone on Hacks 3-7 (TM-mNGU)"
    // R3Breakpoints funds only the first valid hack, so the returned order is a sequence, not a split.
    public static class HackPlan
    {
        public const int AttackDefense = 0;
        public const int Adventure = 1;

        // The guide counts hacks from 1: "Hacks 3-7 (TM-mNGU)" are game ids 2 (Time Machine) .. 6 (Magic NGU).
        public const int FirstMilestoneFrom = 2;
        public const int FirstMilestoneTo = 6;

        // levels / milestoneThresholds / hardCaps are indexed by hack id; milestoneThresholds is the game's
        // perk/quirk-reduced HacksController.milestoneThreshold, so the first milestone IS that level.
        public static int[] Order(bool cblock3Complete, bool evidenceMaxxed,
            long[] levels, long[] milestoneThresholds, long[] hardCaps)
        {
            if (!cblock3Complete) return new[] { AttackDefense };
            List<int> order = new List<int>();
            if (evidenceMaxxed)
            {
                for (int id = FirstMilestoneFrom; id <= FirstMilestoneTo; id++)
                    if (BelowFirstMilestone(id, levels, milestoneThresholds, hardCaps)) order.Add(id);
            }
            order.Add(Adventure);
            return order.ToArray();
        }

        // One hack's inputs to the value ranking. Saturation is HackMath.Saturation, the per-level cost term.
        public struct Lane
        {
            public int Id;
            public long Level;
            public long HardCap;
            public double BaseEffectPerLevel;
            public double MilestoneEffect;
            public long MilestoneThreshold;
            public long Saturation;
        }

        // Value per R3 spent on this lane: the rate (MarginalDensity) plus the amortised next-milestone step.
        // 0 for a lane that is capped, free of effect, or whose cost is unreadable.
        public static double LaneValue(Lane lane)
        {
            if (lane.Level >= lane.HardCap || lane.Saturation <= 0) return 0.0;
            double rate = HackMath.MarginalDensity(lane.BaseEffectPerLevel, lane.Level, lane.Saturation);
            long toNext = lane.MilestoneThreshold > 0
                ? lane.MilestoneThreshold - lane.Level % lane.MilestoneThreshold
                : 0;
            return rate + HackMath.MilestoneStep(lane.MilestoneEffect, toNext, lane.Saturation);
        }

        // Hack ids by descending value, lanes worth nothing left out; equal values keep id order.
        // One percent on different stats counts as one percent: the ranking is dominated by cost
        // (commensurable), not by which stat a hack feeds, so a small gap between lanes means nothing.
        public static int[] RankByValue(IList<Lane> lanes)
        {
            List<KeyValuePair<int, double>> scored = new List<KeyValuePair<int, double>>();
            if (lanes != null)
                foreach (Lane lane in lanes)
                {
                    double v = LaneValue(lane);
                    if (v > 0) scored.Add(new KeyValuePair<int, double>(lane.Id, v));
                }
            scored.Sort((a, b) =>
            {
                int byValue = b.Value.CompareTo(a.Value);
                return byValue != 0 ? byValue : a.Key.CompareTo(b.Key);
            });
            int[] ids = new int[scored.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = scored[i].Key;
            return ids;
        }

        // best lane's value divided by the guide lane's; 0 when either has no value (caller prints n/a).
        public static double ValueRatio(IList<Lane> lanes, int bestId, int guideId)
        {
            double best = 0.0;
            double guide = 0.0;
            if (lanes == null) return 0.0;
            foreach (Lane lane in lanes)
            {
                if (lane.Id == bestId) best = LaneValue(lane);
                if (lane.Id == guideId) guide = LaneValue(lane);
            }
            return best > 0 && guide > 0 ? best / guide : 0.0;
        }

        private static bool BelowFirstMilestone(int id, long[] levels, long[] thresholds, long[] hardCaps)
        {
            if (levels == null || thresholds == null || hardCaps == null) return false;
            if (id >= levels.Length || id >= thresholds.Length || id >= hardCaps.Length) return false;
            if (thresholds[id] <= 0) return false;   // unreadable threshold: no target to chase
            return levels[id] < thresholds[id] && levels[id] < hardCaps[id];
        }
    }
}
