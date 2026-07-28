namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    public class HackBP : ResourceBreakpoint
    {
        protected override bool CorrectResourceType() => Type == ResourceType.R3;

        protected override bool Unlocked() => Index <= 14 && _character.buttons.hacks.interactable;

        protected override bool TargetMet() => _character.hacksController.hitTarget(Index);

        public override bool Allocate()
        {
            long alloc = MaxAllocation;
            _character.hacksController.addR3(Index, alloc);
            return true;
        }
    }
}
