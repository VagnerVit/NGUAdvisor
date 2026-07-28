using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using SimpleJSON;

namespace NGUAdvisor.AllocationProfiles.Breakpoints
{
    public class WandoosBreakpoints : BaseBreakpoints<int>
    {
        public WandoosBreakpoints() : base() { }

        public WandoosBreakpoints(JSONNode bps) : base(bps, (bp) => bp["OS"].AsInt) { }

        protected override bool PerformSwap(Breakpoint bp)
        {
            if (_character.wandoos98.OSlevel <= 0)
                return false;

            int id = bp.priorities;

            if (id == (int)_character.wandoos98.os)
                return true;

            if (id == 1 && !_character.inventory.itemList.jakeComplete)
                return false;
            if (id == 2 && _character.wandoos98.XLLevels <= 0)
                return false;

            var controller = Main.Character.wandoos98Controller;
            controller.SetFieldValue("nextOS", id);
            controller.CallMethod("setOSType");

            _character.wandoos98Controller.refreshMenu();

            return true;
        }
    }
}
