using System;
using System.Collections.Generic;

namespace NGUAdvisor.Managers
{
    // G1 growth tracker (user-approved): a 60s sampler on the status pump feeding a session ring
    // buffer (~2.5h). Read-only — every value is something the UI already reads elsewhere.
    // Semantics: the chips track GAINS (user rule) — spending EXP/AP/PP must never count a rate
    // down, so each sample also carries cumulative positive-deltas (G*) and the rate walks read
    // those. NGU levels RESET on rebirth, so rate walks stop at a run boundary (rebirthTime went
    // backwards) for per-run metrics and for RUN windows — rebirth is the only "reset" the chips see.
    public static class GrowthTracker
    {
        public class Sample
        {
            public DateTime T;
            public double Exp, Ap, Pp, CubeP, CubeT, Ngu;          // raw balances (tile values)
            public double GExp, GAp, GPp, GCubeP, GCubeT, GNgu;    // cumulative gains since load
            public double RunSec;
        }

        private static readonly List<Sample> _samples = new List<Sample>();   // oldest → newest
        private static DateTime _lastSample = DateTime.MinValue;
        private const int MaxSamples = 150;

        // Called every frame from the status pump (main thread); samples once a minute, always —
        // history builds even while another section is open.
        public static void Tick()
        {
            if ((DateTime.UtcNow - _lastSample).TotalSeconds < 60) return;
            _lastSample = DateTime.UtcNow;
            try
            {
                var c = Main.Character;
                if (c == null) return;
                var prev = Newest;
                // A failed read must carry the PREVIOUS value, not 0: with cumulative gains, a
                // one-tick dip to 0 would register the whole balance as a fresh gain next minute.
                double Read(Func<double> f, double carry) { try { return f(); } catch { return carry; } }
                var s = new Sample { T = DateTime.UtcNow };
                s.Exp = Read(() => c.realExp, prev?.Exp ?? 0);
                s.Ap = Read(() => c.arbitrary.curArbitraryPoints, prev?.Ap ?? 0);
                s.Pp = Read(() => c.adventure.itopod.perkPoints, prev?.Pp ?? 0);
                s.CubeP = Read(() => c.inventoryController.cubePower(), prev?.CubeP ?? 0);
                s.CubeT = Read(() => c.inventoryController.cubeToughness(), prev?.CubeT ?? 0);
                s.Ngu = Read(() =>
                {
                    long total = 0;
                    for (int i = 0; i < c.NGU.skills.Count; i++) total += c.NGU.skills[i].level;
                    for (int i = 0; i < c.NGU.magicSkills.Count; i++) total += c.NGU.magicSkills[i].level;
                    return total;
                }, prev?.Ngu ?? 0);
                s.RunSec = Read(() => c.rebirthTime.totalseconds, prev?.RunSec ?? 0);
                if (prev != null)
                {
                    // Positive deltas only: spending drops the balance but never the gain counters.
                    // (An NGU rebirth reset is a big negative delta — ignored, counters stay flat.)
                    s.GExp = prev.GExp + Math.Max(0, s.Exp - prev.Exp);
                    s.GAp = prev.GAp + Math.Max(0, s.Ap - prev.Ap);
                    s.GPp = prev.GPp + Math.Max(0, s.Pp - prev.Pp);
                    s.GCubeP = prev.GCubeP + Math.Max(0, s.CubeP - prev.CubeP);
                    s.GCubeT = prev.GCubeT + Math.Max(0, s.CubeT - prev.CubeT);
                    s.GNgu = prev.GNgu + Math.Max(0, s.Ngu - prev.Ngu);
                }
                _samples.Add(s);
                if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
            }
            catch (Exception e) { Main.LogDebug($"GrowthTracker: {e.Message}"); }
        }

        public static Sample Newest => _samples.Count > 0 ? _samples[_samples.Count - 1] : null;

        // Rate per hour for a metric over a window. windowMinutes <= 0 means RUN (since the last
        // rebirth boundary). perRun metrics (NGU levels) also stop at a boundary inside a timed
        // window — a rate across a reset is meaningless. Returns false until two usable samples.
        public static bool Rate(Func<Sample, double> get, double windowMinutes, bool perRun, out double perHour)
        {
            perHour = 0;
            if (_samples.Count < 2) return false;
            var newest = _samples[_samples.Count - 1];
            var oldest = newest;
            for (int i = _samples.Count - 2; i >= 0; i--)
            {
                var s = _samples[i];
                bool boundary = s.RunSec > _samples[i + 1].RunSec + 1;   // run clock went backwards → rebirth
                if ((perRun || windowMinutes <= 0) && boundary) break;
                if (windowMinutes > 0 && (newest.T - s.T).TotalMinutes > windowMinutes) break;
                oldest = s;
            }
            double hours = (newest.T - oldest.T).TotalHours;
            if (hours < 1.0 / 120.0) return false;   // < 30s of history in window
            perHour = (get(newest) - get(oldest)) / hours;
            return true;
        }

        // Delta since the run started (or as far back as the buffer reaches within this run).
        public static double RunDelta(Func<Sample, double> get)
        {
            double rate;
            if (_samples.Count < 2) return 0;
            var newest = _samples[_samples.Count - 1];
            var oldest = newest;
            for (int i = _samples.Count - 2; i >= 0; i--)
            {
                if (_samples[i].RunSec > _samples[i + 1].RunSec + 1) break;
                oldest = _samples[i];
            }
            rate = get(newest) - get(oldest);
            return rate;
        }
    }
}
