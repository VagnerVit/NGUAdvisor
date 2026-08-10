using System;

namespace NGUAdvisor.Managers
{
    // The ONE place a "when can I afford the next perk" estimate is computed.
    //
    // It exists as its own Unity-free file for two reasons: it is the only arithmetic in the PP
    // module, so isolating it makes it unit-testable without an NGU install; and a second copy in the
    // panel would be free to drift from this one.
    //
    // Every "no answer" case returns null rather than a number. A rendered 0h or an infinity reads as
    // a real prediction, and the module's whole value is that its numbers can be trusted.
    public static class PpEta
    {
        public static double? HoursTo(long cost, long banked, double perHour)
        {
            if (banked >= cost) return null;                                   // already affordable
            if (double.IsNaN(perHour) || double.IsInfinity(perHour)) return null;
            if (perHour <= 0) return null;                                     // no rate -> no estimate
            return (cost - banked) / perHour;
        }
    }
}
