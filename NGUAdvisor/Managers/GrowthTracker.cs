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

        // Two samples 60 s apart should see the run clock advance ~60 s. This much disagreement means
        // they describe different states, not elapsed play — generous enough that a frame-rate stall or
        // a slow status pump never trips it.
        private const double DiscontinuitySeconds = 120;

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
                // Track-aware (NGUAdvisors owns the track rule): on Evil/Sadistic the normal
                // `level` field barely moves, so summing it read a flat 0 against a nonzero
                // prediction — the two numbers have to count the same levels to be comparable.
                s.Ngu = Read(() => NGUAdvisors.TrackedLevelTotal(c), prev?.Ngu ?? 0);
                s.RunSec = Read(() => c.rebirthTime.totalseconds, prev?.RunSec ?? 0);

                // A SAVE LOAD IS A DISCONTINUITY, NOT A GAIN. Character is one instance for the whole
                // process (Main.cs's caching invariant) and the save deserializes INTO it, so while the
                // game sits on its title screen every balance reads as a fresh character's zero. The
                // moment the save loads, the next sample jumps the entire account — measured 2026-08-12
                // as "NGU +10.1K/hr" against a predicted 44.9, i.e. 22402%.
                //
                // Detected the same way a rebirth is: the run clock disagreeing with the wall clock.
                // A rebirth runs it BACKWARDS; a save load runs it far FORWARD (0 -> 91619s). Either
                // way the two samples describe different states and no delta between them is real, so
                // this one carries the gain counters forward untouched and becomes the new baseline.
                bool sameRun = prev != null &&
                    Math.Abs((s.RunSec - prev.RunSec) - (s.T - prev.T).TotalSeconds) < DiscontinuitySeconds;
                if (prev != null && !sameRun)
                {
                    s.GExp = prev.GExp; s.GAp = prev.GAp; s.GPp = prev.GPp;
                    s.GCubeP = prev.GCubeP; s.GCubeT = prev.GCubeT; s.GNgu = prev.GNgu;
                    Main.LogDebug($"[GrowthDbg] discontinuity — run clock moved {s.RunSec - prev.RunSec:0}s "
                                + $"over {(s.T - prev.T).TotalSeconds:0}s of wall clock (save load or rebirth); "
                                + "this sample is a new baseline, no gain counted");
                }
                else if (prev != null)
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
