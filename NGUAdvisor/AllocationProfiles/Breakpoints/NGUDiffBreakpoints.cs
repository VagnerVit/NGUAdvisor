using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using SimpleJSON;

namespace NGUAdvisor.AllocationProfiles.Breakpoints
{
    public class NGUDiffBreakpoints : BaseBreakpoints<int>
    {
        public NGUDiffBreakpoints() : base() { }

        public NGUDiffBreakpoints(JSONNode bps) : base(bps, (bp) => bp["Diff"].AsInt) { }

        protected override bool PerformSwap(Breakpoint bp)
        {
            var setDifficulty = (difficulty)bp.priorities;
            if (_character.settings.rebirthDifficulty < setDifficulty)
                return false;

            _character.settings.nguLevelTrack = setDifficulty;
            _character.NGUController.refreshMenu();
            return true;
        }
    }
}
