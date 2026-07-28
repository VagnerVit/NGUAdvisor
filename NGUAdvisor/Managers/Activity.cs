using System;

namespace NGUAdvisor.Managers
{
    public enum ActivityKind { Completed, Queued, Warning, Failure }

    // ONE user-triggered outcome, with the time it happened. Nothing else.
    //
    // The record owns WHEN — not the ribbon. If lifetime started when Sync() first painted the message,
    // an outcome reported while the window was on another section (or mid-navigation, or during a frame
    // the form skipped) would silently get a fresh 8 seconds. Age is a property of the event.
    public class ActivityItem
    {
        public int Seq;                 // bumped per report: the ribbon's change-gate compares this
        public ActivityKind Kind;
        public string Message;          // one readable line — the outcome, not a log dump
        public string Detail;           // optional, appended if it fits
        public bool LogLink;            // only true when LOGS genuinely holds more than this line
        public DateTime ReportedAt;     // UTC, authoritative
    }

    // The one-slot model. LOGS remains the history; this is "what happened because of the thing I just
    // clicked". A newer outcome always replaces an older one — including a success replacing a failure,
    // because the user just acted again and the newest result is the one they are looking at.
    //
    // Threading: every caller is a UI-thread click handler (verified — the wired actions are all Button
    // Click handlers in panels), and the only reader is SettingsForm.UpdateStatus on the Unity main
    // thread. No locks; adding synchronisation here would be inventing a problem.
    public static class Activity
    {
        // Failure has no lifetime: it stands until dismissed or until the user's next action replaces it.
        private const double CompletedSec = 8;
        private const double QueuedSec = 12;
        private const double WarningSec = 30;

        private static int _seq;

        public static ActivityItem Current { get; private set; }

        public static void Completed(string message, string detail = null)
            => Report(ActivityKind.Completed, message, detail, false);

        public static void Queued(string message, string detail = null)
            => Report(ActivityKind.Queued, message, detail, false);

        public static void Warning(string message, string detail = null, bool logLink = false)
            => Report(ActivityKind.Warning, message, detail, logLink);

        public static void Failed(string message, string detail = null, bool logLink = false)
            => Report(ActivityKind.Failure, message, detail, logLink);

        public static void Report(ActivityKind kind, string message, string detail, bool logLink)
        {
            if (string.IsNullOrEmpty(message)) return;
            Current = new ActivityItem
            {
                Seq = ++_seq,
                Kind = kind,
                Message = message,
                Detail = detail,
                LogLink = logLink,
                ReportedAt = DateTime.UtcNow
            };
        }

        public static void Dismiss() => Current = null;

        // Age decides, not the paint. A FAILURE never expires on its own.
        public static bool Expired(ActivityItem a, DateTime nowUtc)
        {
            if (a == null) return true;
            switch (a.Kind)
            {
                case ActivityKind.Failure: return false;
                case ActivityKind.Warning: return (nowUtc - a.ReportedAt).TotalSeconds > WarningSec;
                case ActivityKind.Queued: return (nowUtc - a.ReportedAt).TotalSeconds > QueuedSec;
                default: return (nowUtc - a.ReportedAt).TotalSeconds > CompletedSec;
            }
        }
    }
}
