using NGUAdvisor.Managers;

namespace NGUAdvisor.AllocationProfiles.RebirthStuff
{
    public class MuffinRebirth : TimeRebirth
    {
        private const double _24h = 60 * 60 * 24;
        private bool _shouldMuffin = true;
        private readonly ConsumablesManager.Muffin _muffinConsumable;

        public bool BalanceTime = false;
        public double MuffinMinuteBuffer = 60;

        public MuffinRebirth()
        {
            RebirthTime = _24h;
            _muffinConsumable = new ConsumablesManager.Muffin();
        }

        public override bool RebirthAvailable(out bool challenges)
        {
            RebirthTime = _24h;
            _shouldMuffin = true;

            if (!Main.Settings.AutoRebirth)
            {
                _shouldMuffin = false;
                return base.RebirthAvailable(out challenges);
            }

            // Don't use muffins if we are currently in a challenge
            _shouldMuffin &= !_character.challenges.inChallenge;
            // Don't use muffins before finishing sadistic troll challenge 2
            _shouldMuffin &= _character.allChallenges.trollChallenge.sadisticCompletions() >= 2;
            // Don't use muffins before maxxing 5 O'Clock Shadow perk
            _shouldMuffin &= _character.adventure.itopod.perkLevel[21] >= _character.adventureController.itopod.maxLevel[21];
            // Don't use muffins before maxxing Beast's Fertilizer quirk
            _shouldMuffin &= _character.beastQuest.quirkLevel[13] >= _character.beastQuestPerkController.maxLevel[13];
            // Do 24 hour Rebirths if we don't have any muffins and aren't configured to purchase more or don't have the AP to purchase more
            _shouldMuffin &= _muffinConsumable.CanUse(1);

            // Cycle between 24 and 23 hours  (24h -> Activate Muffin if possible -> Rebirth -> 23h -> Rebirth)
            if (_shouldMuffin)
            {
                double longTime = _24h + (BalanceTime ? (MuffinMinuteBuffer * 60) : 0);
                double shortTime = _24h - (MuffinMinuteBuffer * 60);

                bool muffinIsActive = _muffinConsumable.MuffinIsActive();
                double muffinTimeLeft = _muffinConsumable.MuffinTimeLeft();

                if (muffinTimeLeft > 0 && !muffinIsActive)
                    RebirthTime = shortTime;
                else
                    RebirthTime = longTime;
            }

            return base.RebirthAvailable(out challenges);
        }

        protected override bool PreRebirth()
        {
            if (BaseRebirth.PreRebirth())
                return true;

            // Try to eat a muffin if it has completely expired
            if (_shouldMuffin && !_muffinConsumable.MuffinIsActive() && _muffinConsumable.MuffinTimeLeft() <= 0)
            {
                if (_muffinConsumable.CanUse(1))
                    _muffinConsumable.Use(1);
            }

            return false;
        }
    }
}
