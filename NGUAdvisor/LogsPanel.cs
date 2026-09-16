using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // LOGS section (L1, revised for the A1 rail sub-nav): the sources live as rail children —
    // the rail calls SelectSource. This panel is the reader: adaptive filter chips + the list.
    //  - ADVISOR:    advisor.log  (ChallengeOverlay.Record's own writer)
    //  - LOOT:       loot.log
    //  - SESSION:    inject.log
    //  - DIAGNOSTIC: debug.log / combat.log / pitspin.log / yggdrasil.log, chosen by the chips
    //
    // EVERY source is a file tail (shared read — the writers keep them open). The in-memory rings
    // these replaced were emptied by every reload and capped at 50/400 entries, so the reader went
    // blank after a hot-swap while the files kept growing; and four of the seven writers had no way
    // into the UI at all, debug.log — where every [GearDbg]/[ZoneDbg] line lands — among them.
    // cards.log is deliberately absent: nothing writes to it yet.
    public class LogsPanel : Panel
    {
        private static readonly string[] AdvisorCats = { "ALL", "ALLOC", "GEAR", "TITAN", "SEGMENT", "QUEST" };
        private static readonly string[] LootCats = { "ALL", "DROPS", "EXP · AP", "BOOSTS" };
        private static readonly string[] SessionCats = { "ALL" };
        // The remaining writers, reachable at last. They are not filters of one feed but separate files,
        // so the chips here SELECT THE FILE -- the only axis that matters once a source is "raw log".
        private static readonly string[] DiagCats = { "DEBUG", "COMBAT", "PIT", "YGG" };
        private static readonly string[][] SourceFilters = { AdvisorCats, LootCats, SessionCats, DiagCats };

        // Every source now reads its FILE, not an in-memory ring: a reload empties the rings while the
        // files keep growing, which is why LOGS looked empty after every hot-swap. null = curated feed.
        private static readonly string[] SourceFiles = { "advisor.log", "loot.log", "inject.log", null };

        private const int TailLines = 400;

        private static string DiagFile(string filter)
        {
            switch (filter)
            {
                case "COMBAT": return "combat.log";
                case "PIT": return "pitspin.log";
                case "YGG": return "yggdrasil.log";
                default: return "debug.log";
            }
        }

        private readonly List<Button> _chips = new List<Button>();
        private ListBox _list;
        private Button _pause;
        private Button _openFile;
        private Button _export;
        private int _active;              // 0 advisor · 1 loot · 2 session (rail children)
        private string _filter = "ALL";
        private bool _paused;
        private string _lastTop;
        private int _lastCount = -1;
        private DateTime _lastTick = DateTime.MinValue;

        public LogsPanel(int canvasW)
        {
            BackColor = UiTheme.Ground;
            Width = canvasW;

            // Filter chip strip + actions; chips rebuild per source.
            _pause = MkChip("⏸ PAUSE");
            _pause.Click += (s, e) =>
            {
                _paused = !_paused;
                UiTheme.ApplyState(_pause, _paused ? UiTheme.Energy : UiTheme.BtnFace, _paused ? Color.White : UiTheme.Ink);
                if (!_paused) Rebuild(force: true);
            };
            _openFile = MkChip("OPEN FILE");
            _openFile.Click += (s, e) =>
            {
                try
                {
                    string file = _active == 3 ? DiagFile(_filter) : SourceFiles[_active];
                    System.Diagnostics.Process.Start(file == null ? GetLogDir() : Path.Combine(GetLogDir(), file));
                }
                catch (Exception ex) { LogDebug($"Logs open: {ex.Message}"); }
            };

            // MAIN-THREAD RULE: StateExport reads live Character and scene objects for every section,
            // so this handler only REQUESTS — Main.Update() runs the dump on the Unity thread and the
            // "State exported to ..." line lands in this very panel's ADVISOR feed.
            _export = MkChip("EXPORT STATE");
            _export.Click += (s, e) => RequestStateExport();

            _list = new ListBox
            {
                Bounds = new Rectangle(0, UiTheme.S(34), canvasW - UiTheme.S(20), UiTheme.ListH(24)),   // the tuned S(600) at the 25px line height
                Font = UiTheme.Ui,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Ink,
                SelectionMode = SelectionMode.None
            };
            UiTheme.StyleList(_list);
            Controls.Add(_list);
            Controls.Add(_pause);
            Controls.Add(_openFile);
            Controls.Add(_export);

            Height = _list.Bottom + UiTheme.S(6);
            BuildChips();
            SelectSource(0);
            VisibleChanged += (s, e) => { if (Visible) Rebuild(force: true); };
        }

        private Button MkChip(string text)
        {
            var b = new Button { Text = text, Size = new Size(UiLayout.BtnWidth(text), UiTheme.SCtl(24)), Font = UiTheme.Chip, FlatStyle = FlatStyle.Flat };
            b.FlatAppearance.BorderColor = UiTheme.Border;
            UiTheme.ApplyState(b, UiTheme.BtnFace, UiTheme.Ink);
            return b;
        }

        // Chips adapt to the source: rebuild the strip, then right-align PAUSE / OPEN FILE.
        private void BuildChips()
        {
            foreach (var c in _chips) { Controls.Remove(c); c.Dispose(); }
            _chips.Clear();

            int cx = 0;
            foreach (var cat in SourceFilters[_active])
            {
                var b = MkChip(cat);
                b.Location = new Point(cx, UiTheme.S(2));
                string captured = cat;
                b.Click += (s, e) => { _filter = captured; StyleChips(); Rebuild(force: true); };
                Controls.Add(b);
                _chips.Add(b);
                cx += b.Width + UiTheme.S(6);
            }
            _openFile.Location = new Point(_list.Right - _openFile.Width, UiTheme.S(2));
            _pause.Location = new Point(_openFile.Left - _pause.Width - UiTheme.S(6), UiTheme.S(2));
            _export.Location = new Point(_pause.Left - _export.Width - UiTheme.S(6), UiTheme.S(2));
            StyleChips();
        }

        private void StyleChips()
        {
            var cats = SourceFilters[_active];
            for (int i = 0; i < _chips.Count; i++)
                UiTheme.ApplyState(_chips[i], cats[i] == _filter ? UiTheme.Accent : UiTheme.BtnFace,
                    cats[i] == _filter ? Color.White : UiTheme.Ink);
        }

        // Called by the rail's LOGS children (A1 sub-nav owns source selection).
        public void SelectSource(int idx)
        {
            _active = Math.Max(0, Math.Min(SourceFilters.Length - 1, idx));
            _filter = "ALL";
            BuildChips();
            Rebuild(force: true);
        }

        public void TickLogs()
        {
            if (!Visible) return;
            if ((DateTime.UtcNow - _lastTick).TotalSeconds < 2) return;
            _lastTick = DateTime.UtcNow;
            if (!_paused) Rebuild();
        }

        private static bool LootMatch(string line, string filter)
        {
            switch (filter)
            {
                case "EXP · AP": return line.Contains(" EXP") || line.Contains(" AP");
                case "BOOSTS": return line.IndexOf("boost", StringComparison.OrdinalIgnoreCase) >= 0;
                case "DROPS": return !(line.Contains(" EXP") || line.Contains(" AP"));
                default: return true;
            }
        }

        private List<string> CurrentLines()
        {
            switch (_active)
            {
                case 0:
                    var adv = Tail("advisor.log");
                    // The category tag sits after the timestamp the writer prepends, so match anywhere in
                    // the line rather than at its start (the ring entries this replaced led with the tag).
                    return _filter == "ALL" ? adv : adv.Where(l => l.Contains($"[{_filter}]")).ToList();
                case 1:
                    var loot = Tail("loot.log");
                    return _filter == "ALL" ? loot : loot.Where(l => LootMatch(l, _filter)).ToList();
                case 3:
                    return Tail(DiagFile(_filter));
                default:
                    return Tail("inject.log");
            }
        }

        // Newest first, bounded window at the end of the file — these grow all session and this
        // refreshes every 2 s, so never read the whole thing.
        private static List<string> Tail(string file) => LogTail.Read(Path.Combine(GetLogDir(), file), TailLines);

        private void Rebuild(bool force = false)
        {
            try
            {
                var lines = CurrentLines();
                string top = lines.Count > 0 ? lines[0] : null;
                if (!force && lines.Count == _lastCount && top == _lastTop) return;
                _lastCount = lines.Count;
                _lastTop = top;

                _list.BeginUpdate();
                try
                {
                    _list.Items.Clear();
                    if (lines.Count == 0)
                        _list.Items.Add(_active == 0 ? "(no advisor actions logged yet)" : "(this log is empty)");
                    else
                        foreach (var l in lines) _list.Items.Add(l);
                }
                finally { _list.EndUpdate(); }
            }
            catch (Exception ex) { LogDebug($"Logs panel: {ex.Message}"); }
        }
    }
}
