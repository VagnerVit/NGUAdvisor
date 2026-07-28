namespace NGUAdvisor.AllocationProfiles.RebirthStuff
{
    public class NumberRebirth : TimeRebirth
    {
        public double MultTarget { get; set; }

        public override bool RebirthAvailable(out bool challenges)
        {
            if (!base.RebirthAvailable(out challenges))
                return false;

            if (challenges)
                return true;

            double target = _character.attackMulti * MultTarget;

            return _character.nextAttackMulti > target;
        }
    }
}
