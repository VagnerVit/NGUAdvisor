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

        private static bool BelowFirstMilestone(int id, long[] levels, long[] thresholds, long[] hardCaps)
        {
            if (levels == null || thresholds == null || hardCaps == null) return false;
            if (id >= levels.Length || id >= thresholds.Length || id >= hardCaps.Length) return false;
            if (thresholds[id] <= 0) return false;   // unreadable threshold: no target to chase
            return levels[id] < thresholds[id] && levels[id] < hardCaps[id];
        }
    }
}
