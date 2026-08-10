using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    /// <summary>
    /// One row of the AP plan, resolved against the running game.
    ///
    /// <see cref="Known"/> and <see cref="CostKnown"/> are deliberately separate facts. A row can be
    /// perfectly identified (we know which tier-list entry it is, and that it is not owned) while its
    /// price is unreadable — its shop pod may not exist yet on an account that has not unlocked the
    /// entry. A consumer must render the price ONLY when <see cref="CostKnown"/> is true; a zero
    /// <see cref="Cost"/> on an unknown row is absence of data, not a free purchase.
    /// </summary>
    public struct ApRec
    {
        public bool Known;        // false when this row could not be resolved at all
        public ApItem Item;
        public long Cost;
        public bool CostKnown;    // false when the cost read failed — never render Cost as a price then
        public bool Affordable;
        public long Balance;
    }

    /// <summary>
    /// The live binding for <see cref="ApTierTable"/>: reads the running game and answers "what should
    /// I spend AP on next?".
    ///
    /// ADVISE-ONLY BY DESIGN. AP is not refundable and the ordering is one player's opinion, so
    /// nothing in this file may buy anything. Do not add a purchase call here, not even behind a flag.
    ///
    /// MAIN THREAD ONLY. Every method touches live Unity objects (<c>Character</c>, the shop
    /// MonoBehaviours). Do not call any of this from a <c>FileSystemWatcher</c> callback or a WinForms
    /// handler — set a pending flag and let <c>Main.Update()</c> drain it, as the rest of the repo does.
    ///
    /// Game truth, read off the decompiled Assembly-CSharp.dll (2026-08-10):
    /// <list type="bullet">
    /// <item>Balance is <c>character.arbitrary.curArbitraryPoints</c> (long).</item>
    /// <item><c>ArbitraryController</c> is a per-shop-entry MonoBehaviour with a public <c>int id</c>,
    ///   an instance <c>long cost()</c>, and <c>bool shouldDisableBuyButton(int id)</c>. The owning
    ///   list is <c>character.allArbitrary.arbitraryPods</c>.</item>
    /// <item><c>shouldDisableBuyButton</c> is a pure owned/maxed predicate — case by case it returns
    ///   the ownership flag or a <c>count &gt;= max()</c> check. It does NOT consider affordability,
    ///   which is exactly why it is the right ownership read and why no hand-mapped field table is
    ///   needed (or wanted: a hand map is wrong the day the game adds an entry).</item>
    /// <item>Hearts are absent from that switch because they can be re-bought to raise their level, so
    ///   ownership for a heart is <c>inventory.itemList.itemDropped[itemId]</c>. They DO have shop
    ///   pods that price them, as do the repeatable rows — absence from the ownership switch says
    ///   nothing about pricing. That is why ownership and pricing use two different keys
    ///   (<see cref="ApItem.Key"/> and <see cref="ApItem.CostId"/>).</item>
    /// </list>
    /// </summary>
    public static class ApPurchaseAdvisor
    {
        // The shop pods are created with the scene and do not churn, so the id map is built once.
        // It is rebuilt whenever it reads back empty: a cached empty map would answer "no controller"
        // for every id, which — see the Owned() comment — degrades to "nothing is owned" and would
        // silently recommend the whole tier list from the top.
        private static Dictionary<int, ArbitraryController> _pods;

        public static long Balance()
        {
            try
            {
                Character c = Main.Character;
                return c?.arbitrary?.curArbitraryPoints ?? 0;
            }
            catch (Exception e)
            {
                Main.LogDebug($"ApPurchaseAdvisor: AP balance read failed: {e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Whether the game considers this entry bought (or maxed).
        ///
        /// KNOWN, ACCEPTED RISK: every failure path here reports "not owned". A broken read therefore
        /// makes the advisor recommend something the user has already bought. That is the lesser evil
        /// against throwing out of a one-second UI refresh — but it is a real failure mode, not a
        /// harmless default, and it must stay visible: the cost of an unresolvable row comes back with
        /// <see cref="ApRec.CostKnown"/> false so the panel says "cost unknown" instead of printing a
        /// fabricated zero.
        /// </summary>
        public static bool Owned(ApItem item)
        {
            if (item == null) return false;
            try
            {
                switch (item.Source)
                {
                    case ApSource.ShopId:
                        ArbitraryController pod = ControllerFor(item.Key);
                        return pod != null && pod.shouldDisableBuyButton(item.Key);

                    case ApSource.Heart:
                        List<bool> dropped = Main.Character?.inventory?.itemList?.itemDropped;
                        return dropped != null
                            && item.Key >= 0
                            && item.Key < dropped.Count
                            && dropped[item.Key];

                    default:
                        // Repeatable (PP, EXP) has no owned state and never blocks the queue.
                        return false;
                }
            }
            catch (Exception e)
            {
                Main.LogDebug($"ApPurchaseAdvisor: ownership read failed for {item.Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>The next unowned entry in tier-then-rank order, or a row with Known = false when
        /// the whole table is owned.</summary>
        public static ApRec Next()
        {
            long balance = Balance();
            foreach (ApItem item in ApTierTable.Items)
            {
                if (Owned(item)) continue;
                return Describe(item, balance);
            }
            return new ApRec();
        }

        /// <summary>The next <paramref name="n"/> unowned entries, in the table's own order.</summary>
        public static IReadOnlyList<ApRec> Queue(int n)
        {
            List<ApRec> recs = new List<ApRec>();
            if (n <= 0) return recs;

            long balance = Balance();
            foreach (ApItem item in ApTierTable.Items)
            {
                if (Owned(item)) continue;
                recs.Add(Describe(item, balance));
                if (recs.Count >= n) break;
            }
            return recs;
        }

        private static ApRec Describe(ApItem item, long balance)
        {
            ApRec rec = new ApRec { Known = true, Item = item, Balance = balance };

            // The cost read is guarded on its own so a price failure never masquerades as an
            // ownership fact (and vice versa) — the two questions fail independently.
            long cost;
            rec.CostKnown = TryCost(item, out cost);
            rec.Cost = rec.CostKnown ? cost : 0;

            // Unaffordable when the price is unknown: claiming "you can afford it" without a price
            // would be a guess, and this row's whole job is to be honest about what it does not know.
            rec.Affordable = rec.CostKnown && balance >= cost;
            return rec;
        }

        // Every row is priced through a shop pod, including hearts and repeatables — being absent from
        // shouldDisableBuyButton is an OWNERSHIP fact and says nothing about pricing. Which pod does the
        // pricing is ApItem.CostId, falling back to Key when it is 0 (the ShopId case, where the two
        // keys are the same number). A row only reports CostKnown = false when its pod is genuinely
        // missing — typically an entry the account has not unlocked yet.
        private static bool TryCost(ApItem item, out long cost)
        {
            cost = 0;
            if (item == null) return false;
            try
            {
                int costId = item.CostId != 0 ? item.CostId : item.Key;
                ArbitraryController pod = ControllerFor(costId);
                if (pod == null) return false;
                cost = pod.cost();
                return true;
            }
            catch (Exception e)
            {
                Main.LogDebug($"ApPurchaseAdvisor: cost read failed for {item.Name}: {e.Message}");
                return false;
            }
        }

        private static ArbitraryController ControllerFor(int id)
        {
            Dictionary<int, ArbitraryController> map = Pods();
            ArbitraryController pod;
            if (map == null || !map.TryGetValue(id, out pod)) return null;
            // `pod != null` is Unity's overloaded operator, so a DESTROYED component reads as null here
            // even though the C# reference is still alive. Without dropping the cache on that, every row
            // would silently degrade to "not owned" for the rest of the process — the module's worst
            // failure mode, because it recommends things already bought rather than showing an error.
            // Character and the scene persist for the process (see Main.cs's caching invariant), so this
            // is defensive; it costs one rebuild if it ever fires.
            if (pod == null) { _pods = null; return null; }
            return pod;
        }

        private static Dictionary<int, ArbitraryController> Pods()
        {
            if (_pods != null && _pods.Count > 0) return _pods;
            try
            {
                Character c = Main.Character;
                List<ArbitraryController> pods = c?.allArbitrary?.arbitraryPods;
                if (pods == null) return null;

                Dictionary<int, ArbitraryController> map = new Dictionary<int, ArbitraryController>();
                foreach (ArbitraryController pod in pods)
                {
                    if (pod == null) continue;
                    map[pod.id] = pod;
                }
                if (map.Count == 0) return null;

                _pods = map;
                return _pods;
            }
            catch (Exception e)
            {
                Main.LogDebug($"ApPurchaseAdvisor: shop pod map build failed: {e.Message}");
                return null;
            }
        }
    }
}
