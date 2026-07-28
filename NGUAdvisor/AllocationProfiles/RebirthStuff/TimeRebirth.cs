using System;

namespace NGUAdvisor.AllocationProfiles.RebirthStuff
{
    public class TimeRebirth
    {
        protected static readonly Character _character = Main.Character;

        public double RebirthTime { get; set; }

        public static TimeRebirth CreateRebirth(double time, double target, string type)
        {
            type = type.ToUpper();
            if (type == "TIME")
            {
                return new TimeRebirth { RebirthTime = time };
            }

            if (type.Contains("MUFFIN"))
            {
                bool balanceTime = type.StartsWith("TIMEBALANCED");

                double minimum = 1;
                double maximum = balanceTime ? 15 : 60;

                double minuteBuffer = Math.Min(Math.Max(target, minimum), maximum);

                return new MuffinRebirth()
                {
                    BalanceTime = balanceTime,
                    MuffinMinuteBuffer = minuteBuffer
                };
            }

            if (type == "NUMBER")
            {
                return new NumberRebirth
                {
                    MultTarget = target,
                    RebirthTime = time
                };
            }

            if (type == "BOSSES")
            {
                return new BossNumRebirth
                {
                    NumBosses = target,
                    RebirthTime = time
                };
            }

            return null;
        }

        public virtual bool RebirthAvailable(out bool challenges)
        {
            challenges = false;
            if (RebirthTime < 0.0)
                return false;

            if (!BaseRebirth.RebirthAvailable())
                return false;

            challenges = !_character.challenges.inChallenge && BaseRebirth.AnyChallengesValid();
            if (challenges)
                return true;

            return _character.rebirthTime.totalseconds >= RebirthTime;
        }
        
        public bool DoRebirth()
        {
            if (PreRebirth())
                return false;

            if (!_character.challenges.inChallenge && BaseRebirth.TryStartChallenge())
                return true;

            BaseRebirth.EngageRebirth();
            return true;
        }

        protected virtual bool PreRebirth() => BaseRebirth.PreRebirth();
    }
}
