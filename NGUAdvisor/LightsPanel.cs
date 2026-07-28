using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.AllocationProfiles.BreakpointTypes;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // The twelve status lights — split out of the old StatusBoardPanel for the M1 Control Room
    // shell so the lights can span the canvas while the action cards live in their own column.
    // Lights: green pass · red blocked/skipped · amber attention · grey idle-by-design. Every value
    // is measured-fit into the widget's budget. Click-through jumps to the system's section.
    // DotColors + BoardRefreshed feed the rail nav's per-section health dots.
    public class LightsPanel : Panel
    {
        private class Widget
        {
            public Panel Box;
            public Panel Dot;
            public Label Name;
            public Label Value;
            public string Nav;
        }

        private readonly List<Widget> _widgets = new List<Widget>();
        private DateTime _lastRefresh = DateTime.MinValue;
        private readonly SettingsForm _form;

        // Live dot colors by light index (see Names) — the rail derives section health from these.
        public readonly Color[] DotColors = new Color[12];
        public event Action BoardRefreshed;

        public static readonly string[] Names =
            { "TM", "GOLD", "SEGMENT", "NGUs", "PILL", "GEAR", "TITAN", "DIGGERS", "BEARDS", "QUEST", "PIT", "BOSS" };
        // Click-through targets, one per light in Names order. Semantic destinations, not rail paths:
        // slice 8 retargets them in Managers/Destinations.cs and this array does not move.
        // Empty target = the detail IS the Advisors canvas; no navigation.
        private static readonly string[] NavTargets =
        {
            Destinations.Gold,      // TM — the time machine is a stage of the gold pipeline
            Destinations.Gold,      // GOLD
            "",                     // SEGMENT
            "",                     // NGUs
            "",                     // PILL
            Destinations.Loadouts,  // GEAR
            Destinations.Titans,    // TITAN
            "",                     // DIGGERS
            "",                     // BEARDS
            Destinations.Quests,    // QUEST
            Destinations.Pit,       // PIT
            ""                      // BOSS
        };

        public LightsPanel(SettingsForm form, int canvasW, int cols)
        {
            _form = form;
            BackColor = UiTheme.Ground;

            int widgetW = (canvasW - (cols - 1) * UiTheme.S(6)) / cols;
            // Both caption lines are floored at the measured 7.5pt line box, so the widget height and
            // the row pitch are DERIVED from them (scaling them apart is what clipped the value line).
            int capH = UiTheme.SHead(18);
            int nameY = UiTheme.S(3);
            int valueY = nameY + capH;
            int widgetH = valueY + capH + UiTheme.S(3);
            int rowPitch = widgetH + UiTheme.S(6);
            for (int i = 0; i < Names.Length; i++)
            {
                var w = new Widget { Nav = NavTargets[i] };
                int col = i % cols, row = i / cols;
                w.Box = new Panel
                {
                    Location = new Point(col * (widgetW + UiTheme.S(6)), row * rowPitch),
                    Size = new Size(widgetW, widgetH),
                    BackColor = UiTheme.Surface,
                    BorderStyle = BorderStyle.FixedSingle,
                    Cursor = Cursors.Hand
                };
                w.Dot = new Panel { Location = new Point(UiTheme.S(8), (widgetH - UiTheme.S(12)) / 2), Size = new Size(UiTheme.S(12), UiTheme.S(12)), BackColor = UiTheme.Faint };
                w.Name = new Label { Text = Names[i], AutoSize = false, Size = new Size(widgetW - UiTheme.S(32), capH), Font = UiTheme.Chip, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(26), nameY) };
                w.Value = new Label { Text = "…", AutoSize = false, Size = new Size(widgetW - UiTheme.S(32), capH), Font = UiTheme.Chip, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(26), valueY) };
                string nav = w.Nav;
                EventHandler go = (s, e) => { try { _form?.NavigateTo(nav); } catch { } };
                w.Box.Click += go; w.Dot.Click += go; w.Name.Click += go; w.Value.Click += go;
                w.Box.Controls.Add(w.Dot);
                w.Box.Controls.Add(w.Name);
                w.Box.Controls.Add(w.Value);
                Controls.Add(w.Box);
                _widgets.Add(w);
            }
            Height = ((Names.Length + cols - 1) / cols) * rowPitch - UiTheme.S(6);
            Width = canvasW;

            VisibleChanged += (s, e) => { if (Visible) RefreshBoard(); };
        }

        public void TickBoard()
        {
            if (!Visible) return;
            if ((DateTime.UtcNow - _lastRefresh).TotalSeconds < 3) return;
            RefreshBoard();
        }

        // Hidden-canvas cadence: the rail health dots need the light colors even when the Advisors
        // canvas isn't showing.
        public void TickBackground(int seconds)
        {
            if ((DateTime.UtcNow - _lastRefresh).TotalSeconds < seconds) return;
            RefreshBoard();
        }

        private void Set(int i, Color dot, string value)
        {
            var w = _widgets[i];
            w.Dot.BackColor = dot;
            DotColors[i] = dot;
            UiLayout.FitInto(w.Value, Report(value));
        }

        // Capitalization scheme (user rule): reports are sentence case — first letter up, rest as
        // written (acronyms/keywords keep their caps).
        private static string Report(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.IsLower(s[0]) ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
        }

        public void RefreshBoard()
        {
            _lastRefresh = DateTime.UtcNow;
            var c = Main.Character;
            if (c == null || Settings == null) return;
            var G = UiTheme.Cap; var R = UiTheme.Danger; var Y = UiTheme.Energy; var N = UiTheme.Faint;

            // 0 TM
            try
            {
                bool funded = c.machine.realBaseGold > 0;
                Set(0, funded ? G : R, funded ? (LevelPlanner.TmFrozen ? "funded · capped" : "funded") : "EMPTY");
            }
            catch { Set(0, N, "—"); }

            // 1 GOLD
            try
            {
                bool starved = OptimizationAdvisor.GoldStarvedForAugs(c, 1.0);
                Set(1, starved ? R : G, starved ? "STARVED · snipe" : "augs OK");
            }
            catch { Set(1, N, "—"); }

            // 2 SEGMENT ("NGU MARATHON" is too long for the widget — the short form is unambiguous)
            try
            {
                string seg = string.IsNullOrEmpty(ChallengeOverlay.Segment) ? "off" : ChallengeOverlay.Segment.Replace("NGU MARATHON", "MARATHON");
                double h = c.rebirthTime.totalseconds / 3600.0;
                Set(2, Settings.AutoProfile ? Y : N, Settings.AutoProfile ? $"{seg} {h:0.0}h" : "profile rules");
            }
            catch { Set(2, N, "—"); }

            // 3 NGUs
            try
            {
                var plan = NGUAdvisors.Compute(
                    ChallengeOverlay.ChapterNguIds(ResourceType.Energy),
                    ChallengeOverlay.ChapterNguIds(ResourceType.Magic));
                // "Hot" = a RUNNING lane clears 1.05x/hr at its actual share (non-target entries
                // carry the full-pool rating, which would read hot always).
                bool hot = plan.Energy.Where(x => plan.EnergyTargets.Contains(x.Id)).Any(x => x.Ratio >= 1.05)
                        || plan.Magic.Where(x => plan.MagicTargets.Contains(x.Id)).Any(x => x.Ratio >= 1.05);
                Set(3, plan.Known ? (hot ? G : Y) : N,
                    plan.Known ? $"{plan.EnergyTargets.Length}E/{plan.MagicTargets.Length}M {(hot ? "hot" : "deep")}" : "—");
            }
            catch { Set(3, N, "—"); }

            // 4 PILL
            try
            {
                var bp = BloodPlanner.Analyze();
                if (!bp.Known) Set(4, N, "—");
                else if (!bp.PillWorthwhile) Set(4, R, "skip: no gain");
                else if (bp.CastIronNow) Set(4, Y, "CAST now");
                else Set(4, G, "worth it");
            }
            catch { Set(4, N, "—"); }

            // 5 GEAR
            try
            {
                string want = Settings.AutoProfile ? null : AllocationProfiles.Breakpoints.GearBreakpoints.ActiveObjective;
                string have = ChallengeOverlay.GearObjectiveOverride ?? want;
                Set(5, G, have != null ? $"{have} set" : "loadout");
            }
            catch { Set(5, N, "—"); }

            // 6 TITAN
            try
            {
                var o = OptimizationAdvisor.NextObjective();
                if (!o.Known) Set(6, G, "all AK'd here");
                else
                {
                    float? t = ZoneHelpers.TimeTillTitanSpawn(o.Index);
                    string cd = t.HasValue ? (t.Value > 60 ? $" · {(int)(t.Value / 60)}m" : " · soon") : "";
                    bool imminent = t.HasValue && t.Value < 300;
                    string stage = o.Stage == "first kill" ? "1st" : o.Stage == "auto-kill" ? "AK" : o.Stage;
                    // Version tag only on versioned titans (T6+); "Walderp v1" was a mislabel.
                    string vtag = ZoneHelpers.IsVersionedTitan(o.Index) ? $" v{o.Version}" : "";
                    Set(6, imminent ? Y : G, $"{TitansPanel.Abbrev[o.Index]}{vtag} {stage}{cd}");
                }
            }
            catch { Set(6, N, "—"); }

            // 7 DIGGERS
            try
            {
                int active = c.diggers.activeDiggers.Count, slots = c.allDiggers.maxDiggerSlots();
                Set(7, active >= slots ? G : R, $"{active}/{slots}");
            }
            catch { Set(7, N, "—"); }

            // 8 BEARDS
            try
            {
                int active = c.beards.activeBeards.Count, slots = c.allBeards.capBeards();
                Set(8, active >= slots ? G : R, $"{active}/{slots}");
            }
            catch { Set(8, N, "—"); }

            // 9 QUEST
            try
            {
                var q = c.beastQuest;
                if (QuestManager.CapstoneItem != null) Set(9, Y, "HOLD: maxing");
                else if (q.inQuest && !q.reducedRewards) Set(9, G, $"major {q.curDrops}/{q.targetDrops}");
                else if (q.inQuest) Set(9, N, $"idle minor {q.curDrops}/{q.targetDrops}");
                else Set(9, N, "between quests");
            }
            catch { Set(9, N, "—"); }

            // 10 PIT
            try
            {
                var pit = MoneyPitManager.AdvisorPlan();
                if (pit.Throw) Set(10, Y, "THROW ready");
                else Set(10, pit.Verdict != null && pit.Verdict.StartsWith("COOLDOWN") ? N : G, pit.Verdict ?? "—");
            }
            catch { Set(10, N, "—"); }

            // 11 BOSS
            try
            {
                int ceiling = OptimizationAdvisor.BossUnlockCeiling();
                bool past = c.bossID - 1 >= ceiling;
                Set(11, past ? Y : G, past ? "past ceiling → EXP" : $"next unlock b.{ceiling}");
            }
            catch { Set(11, N, "—"); }

            try { BoardRefreshed?.Invoke(); } catch { }
        }
    }
}
