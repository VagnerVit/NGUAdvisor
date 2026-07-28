using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // One-time migration helper for the boost priority list (spec:
    // docs/superpowers/specs/2026-07-28-boosts-panel-ux-design.md §4).
    //
    // Boosting used to have three implicit sources: the priority list, every EQUIPPED item, and every
    // LOCKED inventory item. It now has one — the list. This copies the two implicit groups into the
    // list ONCE so the change is visible instead of silent.
    //
    // Deliberately Unity-free and game-free so it can be unit-tested: it is the only place in that
    // change where a wrong result would go unnoticed.
    public static class BoostSeed
    {
        // Appends equipped (then locked) ids that are not already present, preserving the caller's
        // order within each group and never reordering what the user already had.
        public static int[] SeedPriorityBoosts(int[] current, int[] equippedInSlotOrder, int[] lockedInSlotOrder)
        {
            List<int> result = new List<int>();
            HashSet<int> seen = new HashSet<int>();

            void Take(int[] source)
            {
                if (source == null) return;
                for (int i = 0; i < source.Length; i++)
                {
                    int id = source[i];
                    if (id <= 0) continue;
                    if (!seen.Add(id)) continue;
                    result.Add(id);
                }
            }

            Take(current);
            Take(equippedInSlotOrder);
            Take(lockedInSlotOrder);
            return result.ToArray();
        }
    }
}
