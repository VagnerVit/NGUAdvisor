using System;

namespace NGUAdvisor.AllocationProfiles.BreakpointTypes
{
    public class CapCalc
    {
        public double PPT { get; set; }

        public long Num { get; set; }

        public CapCalc(double ppt, long num)
        {
            PPT = ppt;
            Num = num;
        }

        public int Offset => (int)Math.Floor(PPT * 50 * 10);
    }
}
