using SimpleJSON;
using System;
using System.Linq;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    public abstract class BaseBreakpoints<T>
    {
        public class Breakpoint
        {
            public double time;
            public T priorities;
            // Optional challenge tag: this breakpoint only applies while that challenge is active (uppercase
            // code, e.g. "NOTM"). null/empty = untagged (the normal timeline, used when no challenge match).
            public string challenge;

            public Breakpoint(JSONNode bp, T priorities)
            {
                time = ParseTime(bp["Time"]);
                var ch = bp["Challenge"];
                challenge = (ch != null && !string.IsNullOrEmpty(ch.Value)) ? ch.Value.ToUpper() : null;
                this.priorities = priorities;
            }

            private static double ParseTime(JSONNode timeNode)
            {
                var time = 0;

                if (timeNode.IsObject)
                {
                    foreach (var N in timeNode)
                    {
                        if (N.Value.IsNumber)
                        {
                            switch (N.Key.ToLower())
                            {
                                case "h":
                                    time += 60 * 60 * N.Value.AsInt;
                                    break;
                                case "m":
                                    time += 60 * N.Value.AsInt;
                                    break;
                                default:
                                    time += N.Value.AsInt;
                                    break;
                            }
                        }
                    }
                }

                if (timeNode.IsNumber)
                    time = timeNode.AsInt;

                return time;
            }
        }

        protected static readonly Character _character = Main.Character;
        protected Breakpoint[] breakpoints = new Breakpoint[0];
        protected Breakpoint current = null;
        protected bool swapped = false;
        // The challenge under which `current` was selected, so a change of active challenge re-triggers a swap.
        protected string currentChallenge = null;

        public int Length => breakpoints.Length;

        protected BaseBreakpoints() { }

        protected BaseBreakpoints(JSONNode bps, Func<JSONNode, T> selector)
        {
            breakpoints = bps?.Children.Select(bp => new Breakpoint(bp, selector(bp))).OrderByDescending(x => x.time).ToArray();
        }

        // Challenge-aware selection: while a challenge is active, prefer a breakpoint tagged for it; otherwise
        // (or if none matches) fall back to the untagged breakpoints = the normal time-based timeline.
        // breakpoints are sorted descending by time, so the first whose time has passed is the latest one.
        public Breakpoint GetCurrentBreakpoint()
        {
            if (breakpoints == null)
                return null;

            double t = Main.Character.rebirthTime.totalseconds;
            var cur = Managers.ChallengeDetector.Current();

            if (cur != null)
                foreach (var b in breakpoints)
                    if (b.challenge == cur && t > b.time)
                        return b;

            foreach (var b in breakpoints)
                if (b.challenge == null && t > b.time)
                    return b;

            return null;
        }

        public void Swap()
        {
            var cur = Managers.ChallengeDetector.Current();
            var bp = GetCurrentBreakpoint();
            if (bp == null)
            {
                current = null;
                currentChallenge = cur;
                return;
            }

            // Re-swap when the selected breakpoint changes OR the active challenge changes.
            if (bp != current || cur != currentChallenge)
            {
                current = bp;
                currentChallenge = cur;
                swapped = false;
            }

            if (swapped)
                return;

            swapped = PerformSwap(bp);
        }

        protected abstract bool PerformSwap(Breakpoint bp);

        public virtual void Reset() { current = null; currentChallenge = null; }
    }
}
