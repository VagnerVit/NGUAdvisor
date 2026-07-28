using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using SimpleJSON;
using System.Collections.Generic;
using System.Linq;
using static NGUAdvisor.Managers.ConsumablesManager;

namespace NGUAdvisor.AllocationProfiles.Breakpoints
{
    public class ConsumablesBreakpoints : BaseBreakpoints<Dictionary<Consumable, int>>
    {
        public ConsumablesBreakpoints() : base() { }

        public ConsumablesBreakpoints(JSONNode bps) : base(bps, (bp) => ParseConsumableItemNames(bp)) { }

        private static Dictionary<Consumable, int> ParseConsumableItemNames(JSONNode bp)
        {
            var itemNames = bp["Items"].AsArray.Children.Select(x => x.Value.ToUpper());
            var items = new Dictionary<Consumable, int>();

            foreach (string item in itemNames)
            {
                var values = item.Split(':');
                if (values.Length <= 0)
                    continue;

                var consumable = Consumable.CreateInstance(values[0].ToUpper());
                if (consumable == null)
                {
                    Main.Log($"ConsumablesManager - Invalid consumable name: {values[0]}");
                    continue;
                }

                if (values.Length <= 1 || !int.TryParse(values[1], out var amount))
                    amount = 1;

                items.Add(consumable, amount);
            }

            return items;
        }

        protected override bool PerformSwap(Breakpoint bp)
        {
            current = bp;
            EatConsumables(bp.priorities, bp.time);

            return true;
        }

        public override void Reset()
        {
            base.Reset();
            ResetLastConsumables();
        }
    }
}
