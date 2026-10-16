using System;

namespace NGUAdvisor.Managers
{
    // The auto profile's run segment as a pure function of the run's state, split out Unity-free so the
    // time anchors can be tested (ChallengeOverlay supplies the live reads).
    //
    // Ch.5 follows the guide's Evil cadence, not a boss number: 30-minute rebirths climb (EVIL CLIMB),
    // and once the profile runs long rebirths the "24HR RB Schedule" applies — which the guide gives for
    // the climb TO Boss 125, not only after it: TM 0-0.5h, augments 0.5-3h, Normal NGUs + AT, and the
    // Evil NGU tail (the NGU track switch, owned by LevelPlanner via EvilNguStartSec).
    public static class RunShape
    {
        public const string EvilClimb = "EVIL CLIMB";
        public const string Augmentation = "AUGMENTATION";
        public const string TmHour = "TM HOUR";
        public const string AtHour = "AT HOUR";
        public const string Recovery = "RECOVERY";
        public const string Marathon = "NGU MARATHON";

        public const int EvilChapter = 5;

        // Guide ch.5 "Evil Entry": 30-minute rebirths; anything up to 2h is still that cadence.
        public const double ShortRunMaxSec = 7200;
        public const double EvilTmEndSec = 1800;
        public const double EvilAugEndSec = 10800;
        public const double NormalTmEndSec = 3600;
        public const double RecoveryEndSec = 14400;

        public struct Input
        {
            public int Chapter;
            public double RunSec;
            public double RebirthTargetSec;   // the profile's time-based rebirth, <= 0 when none
            public bool TmUnlocked;           // usable now (false in NOTM)
            public bool TmRelocked;           // Evil has not re-unlocked it yet (progression, not a challenge)
            public bool AtUnlocked;
            public bool TmEmpty;
            public bool NumberCheap;
        }

        // A timed long rebirth in Ch.5: the guide's Evil day, whose Evil NGU tail counts back from the
        // rebirth. Without a time target (an LRB) there is no end to count from, so the file keeps the track.
        public static bool IsEvilDay(int chapter, double rebirthTargetSec)
            => chapter == EvilChapter && rebirthTargetSec > ShortRunMaxSec;

        public static bool IsShortRun(double rebirthTargetSec)
            => rebirthTargetSec > 0 && rebirthTargetSec <= ShortRunMaxSec;

        // Guide ch.5: Evil NGUs for the last hour, plus one hour per T7 version defeated
        // ("After v1, that means 2 hours"). NaN when the run has no time target to count back from.
        public static double EvilNguStartSec(double rebirthTargetSec, int t7VersionsBeaten)
        {
            if (rebirthTargetSec <= 0) return double.NaN;
            return rebirthTargetSec - (1 + Math.Max(0, t7VersionsBeaten)) * 3600.0;
        }

        // atHourEndSec is lazy: AtHourPlanner makes its one-shot extension decision when first asked.
        public static string Segment(Input i, Func<double> atHourEndSec)
        {
            if (i.Chapter == EvilChapter)
            {
                // A re-locked TM means the climb is not past boss ~30 yet, whatever the rebirth length.
                if (IsShortRun(i.RebirthTargetSec) || i.TmRelocked) return EvilClimb;
                if (i.RunSec < EvilTmEndSec && i.TmUnlocked) return TmHour;
                if (i.RunSec < EvilAugEndSec) return Augmentation;
                return Marathon;
            }

            if (i.TmUnlocked && (i.TmEmpty || i.RunSec < NormalTmEndSec)) return TmHour;
            if (i.AtUnlocked && i.RunSec < atHourEndSec()) return AtHour;
            if (i.NumberCheap && i.RunSec < RecoveryEndSec) return Recovery;
            return Marathon;
        }
    }
}
