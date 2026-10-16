using System;
using NGUAdvisor.Managers;
using Xunit;

namespace NGUAdvisor.Tests
{
    public class RunShapeTests
    {
        private const double Hour = 3600;
        private const double EvilStartTarget = 32 * 60;          // EvilStart.json: 32 min
        private const double EvilDayTarget = 24 * Hour + 5 * 60;  // 24hr-Evil.json: 24h05m

        private static readonly Func<double> NoAtHour = () => 0;

        private static RunShape.Input Evil(double runSec, double target, bool tmRelocked = false, bool inNotm = false)
            => new RunShape.Input
            {
                Chapter = RunShape.EvilChapter,
                RunSec = runSec,
                RebirthTargetSec = target,
                TmUnlocked = !tmRelocked && !inNotm,
                TmRelocked = tmRelocked,
                AtUnlocked = true,
            };

        [Theory]
        [InlineData(60)]
        [InlineData(25 * 60)]
        public void ThirtyMinuteRebirthsClimb(double runSec)
        {
            Assert.Equal(RunShape.EvilClimb, RunShape.Segment(Evil(runSec, EvilStartTarget), NoAtHour));
        }

        [Fact]
        public void LockedTimeMachineStillClimbsOnALongRebirth()
        {
            Assert.Equal(RunShape.EvilClimb, RunShape.Segment(Evil(5 * Hour, EvilDayTarget, tmRelocked: true), NoAtHour));
        }

        // Guide ch.5 "24HR RB Schedule" — given for the climb to Boss 125, so no boss gate.
        [Theory]
        [InlineData(0, RunShape.TmHour)]
        [InlineData(29 * 60, RunShape.TmHour)]
        [InlineData(30 * 60, RunShape.Augmentation)]
        [InlineData(2.9 * Hour, RunShape.Augmentation)]
        [InlineData(3 * Hour, RunShape.Marathon)]
        [InlineData(23.5 * Hour, RunShape.Marathon)]
        public void TheEvilDayFollowsTheGuideSchedule(double runSec, string expected)
        {
            Assert.Equal(expected, RunShape.Segment(Evil(runSec, EvilDayTarget), NoAtHour));
        }

        [Theory]
        [InlineData(10 * 60, RunShape.Augmentation)]   // no TM half-hour without a TM
        [InlineData(5 * 3600, RunShape.Marathon)]
        public void NotmOnTheEvilDayIsNotAClimb(double runSec, string expected)
        {
            Assert.Equal(expected, RunShape.Segment(Evil(runSec, EvilDayTarget, inNotm: true), NoAtHour));
        }

        [Fact]
        public void AnLrbWithoutATimeTargetRunsTheEvilDayShape()
        {
            Assert.Equal(RunShape.Augmentation, RunShape.Segment(Evil(Hour, -1), NoAtHour));
        }

        [Fact]
        public void TheEvilDaySkipsTheNormalAtHourAndRecovery()
        {
            RunShape.Input i = Evil(1.5 * Hour, EvilDayTarget);
            i.NumberCheap = true;
            Assert.Equal(RunShape.Augmentation, RunShape.Segment(i, () => 5 * Hour));
            i.RunSec = 3.5 * Hour;
            Assert.Equal(RunShape.Marathon, RunShape.Segment(i, () => 5 * Hour));
        }

        [Theory]
        [InlineData(0, 23)]   // pre-T7: the last hour
        [InlineData(1, 22)]   // "After v1, that means 2 hours"
        [InlineData(3, 20)]
        public void EvilNguTailIsOneHourPlusOnePerT7Version(int t7, double startHour)
        {
            Assert.Equal(startHour * Hour, RunShape.EvilNguStartSec(24 * Hour, t7));
        }

        [Fact]
        public void EvilNguTailNeedsATimeTarget()
        {
            Assert.True(double.IsNaN(RunShape.EvilNguStartSec(-1, 2)));
        }

        [Theory]
        [InlineData(RunShape.EvilChapter, EvilDayTarget, true)]
        [InlineData(RunShape.EvilChapter, EvilStartTarget, false)]
        [InlineData(RunShape.EvilChapter, -1, false)]
        [InlineData(4, EvilDayTarget, false)]
        public void OnlyATimedLongEvilRebirthOwnsTheNguTrack(int chapter, double target, bool expected)
        {
            Assert.Equal(expected, RunShape.IsEvilDay(chapter, target));
        }

        [Theory]
        [InlineData(0, true, false, RunShape.TmHour)]
        [InlineData(5 * Hour, true, false, RunShape.TmHour)]       // TM gold empty refills any time
        [InlineData(1.5 * Hour, false, false, RunShape.AtHour)]
        [InlineData(3 * Hour, false, true, RunShape.Recovery)]
        [InlineData(5 * Hour, false, true, RunShape.Marathon)]
        public void NormalChaptersKeepTheTimeAnchoredChain(double runSec, bool tmEmpty, bool numberCheap, string expected)
        {
            var i = new RunShape.Input
            {
                Chapter = 4,
                RunSec = runSec,
                RebirthTargetSec = 24 * Hour,
                TmUnlocked = true,
                AtUnlocked = true,
                TmEmpty = tmEmpty,
                NumberCheap = numberCheap,
            };
            Assert.Equal(expected, RunShape.Segment(i, () => 2 * Hour));
        }
    }
}
