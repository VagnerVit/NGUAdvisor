using System;

namespace NGUAdvisor.AllocationProfiles.RebirthStuff
{
    public class BossNumRebirth : TimeRebirth
    {
        public double NumBosses { get; set; }

        public override bool RebirthAvailable(out bool challenges)
        {
            if (!base.RebirthAvailable(out challenges))
                return false;

            if (challenges)
                return true;

            double bosses = Math.Round(Math.Log10(_character.nextAttackMulti / _character.attackMulti));
            return bosses >= NumBosses;
        }
    }
}
