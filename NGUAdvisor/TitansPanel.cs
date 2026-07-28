using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Combat > TITANS, R2v2 (user-approved): advisor-first hero card with AK STATS and NEXT VERSION
    // requirement boxes, each carrying its own progress bar; chip strip for the rest; manual mode is
    // the M-A uniform grid (centered abbreviations, tight boxes). Locked / wrong-difficulty titans are
    // HIDDEN everywhere; version fractions (v n/n) appear on versioned titans (T6-T12) only.
    public class TitansPanel : Panel
    {
        // Shared with the Gold pipeline's TITAN BANK stage.
        public static readonly string[] Abbrev =
        {
            "GRB", "GCT", "Jake", "UUG", "Walderp", "Beast", "Nerd",
            "Godmother", "Exile", "Hungers", "Lobster", "Amalg", "Tippi", "Traitor"
        };

        // Abbreviation + version tag ONLY when the game's own enemy entry carries one (user-reported
        // mislabel: "Walderp v1" — WALDERP has no versions; the Beast's V1/V2 are separate enemy #s).
        // Reads the live enemy name and keeps our abbreviations for display.
        public static string AbbrevWithVersion(int titanIndex)
        {
            string name = titanIndex >= 0 && titanIndex < Abbrev.Length ? Abbrev[titanIndex] : $"T{titanIndex + 1}";
            try
            {
                var enemy = ZoneHelpers.TitanEnemyName(titanIndex);
                if (enemy != null)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(enemy.TrimEnd(), @"V(\d+)$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    return m.Success ? $"{name} v{m.Groups[1].Value}" : name;
                }
            }
            catch { }
            // Enemy list unreadable: fall back to the versioned-titan table.
            return ZoneHelpers.IsVersionedTitan(titanIndex) ? $"{name} v{ZoneHelpers.TitanVersion(titanIndex)}" : name;
        }
        private static readonly int[] RiddleTitanIndexes = { 5, 6, 7 };
        private const int FirstVersioned = 5;   // T6..T12 = indexes 5..11 have versions
        private const int LastVersioned = 11;

        private SystemControlBar _controlBar;
        private Button _srcToggle;
        private Panel _heroCard;
        private Label _targetName;
        private Label _stateInfo;

        private class ReqBox
        {
            public Panel Box;
            public Label Title;
            public Label Stats;
            public Panel BarOuter;
            public Panel BarInner;
            public Label Caption;
        }
        private ReqBox _akBox;
        private ReqBox _verBox;

        private Panel _chipArea;
        private Panel _manualView;
        private readonly Button[] _killToggles = new Button[14];
        private ComboBox _combatMode;
        private Button _beast;
        private Label _modeLbl;
        private Label _combatSummary;

        private bool _syncing;

        // canvasW: explicit canvas width when hosted in an M1 section column (0 = UiLayout.PanelW).
        public TitansPanel(int canvasW = 0)
        {
            int W = canvasW > 0 ? canvasW : UiLayout.PanelW;
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            // The two layers, in one place, in the canonical vocabulary (slice 1). AUTOMATION is the
            // permission the MANAGERS read (Settings.ManageTitans — Main.Update); DECISIONS is the
            // strategy the ADVISOR reads (Settings.AdvisorTitans — AdvisorApply.Tick). Separate fields,
            // ANDed in code: ADVISOR with AUTOMATION off computes a target nothing ever acts on, which
            // is exactly the silent state the bar now names.
            _controlBar = new SystemControlBar(
                W - UiTheme.S(54),
                () => Settings.ManageTitans, v => Settings.ManageTitans = v,
                () => Settings.AdvisorTitans, v => Settings.AdvisorTitans = v,
                // Kept short on purpose: the bar is 461px wide here, so the line has ~437px. Anything
                // longer would ellipsize (FitText) — measured, not guessed.
                "The advisor picks the target and the tool kills it.",
                "Your manual kill targets drive it; the tool executes them.",
                "Automation is off — the tool will not touch titans.")
            {
                Location = new Point(UiTheme.S(10), UiTheme.S(10))
            };
            _controlBar.Changed += SyncFromSettings;
            Controls.Add(_controlBar);

            // Everything below the bar shifts by its height + an 8px gap.
            int top = UiTheme.S(10) + SystemControlBar.BarHeight + UiTheme.S(8);

            _heroCard = new Panel { Location = new Point(UiTheme.S(10), top), Size = new Size(W - UiTheme.S(54), UiTheme.S(150)), BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle, Tag = "exclusive" };
            Controls.Add(_heroCard);

            _heroCard.Controls.Add(new Label { Text = "CURRENT TARGET", AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.AccentDark, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(10), UiTheme.S(8)) });

            _srcToggle = new Button { Text = "ADVISOR", Size = new Size(UiLayout.BtnWidth("ADVISOR"), UiTheme.SCtl(24)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat };
            _srcToggle.FlatAppearance.BorderColor = UiTheme.Border;
            _srcToggle.Location = new Point(_heroCard.Width - UiTheme.S(12) - _srcToggle.Width, UiTheme.S(6));
            _srcToggle.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.AdvisorTitans = !Settings.AdvisorTitans;
                SyncFromSettings();
            };
            _heroCard.Controls.Add(_srcToggle);

            _targetName = new Label { Text = "…", AutoSize = true, Font = UiTheme.Bold, ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(10), UiTheme.S(34)) };
            _heroCard.Controls.Add(_targetName);
            // Fixed width + measured fit — long state strings blank an overflowing Mono label.
            _stateInfo = new Label { Text = "", AutoSize = false, Size = new Size(_heroCard.Width - UiTheme.S(20), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Surface, Location = new Point(UiTheme.S(10), UiTheme.S(62)), Visible = false };
            _heroCard.Controls.Add(_stateInfo);

            // Two requirement boxes split the hero card (10px margins, 12px gap).
            int reqW = (_heroCard.Width - UiTheme.S(32)) / 2;
            _akBox = MkReqBox(UiTheme.S(10), reqW);
            _verBox = MkReqBox(UiTheme.S(10) + reqW + UiTheme.S(12), reqW);
            // Req boxes grew to fit their floored captions — the card keeps its original 4px bottom margin.
            _heroCard.Height = Math.Max(_heroCard.Height, _akBox.Box.Bottom + UiTheme.S(4));

            _chipArea = new Panel { Location = new Point(UiTheme.S(10), top + UiTheme.S(158)), Size = new Size(W - UiTheme.S(54), UiTheme.S(66)), BackColor = UiTheme.Ground, Tag = "exclusive" };
            Controls.Add(_chipArea);

            // Manual M-A: uniform 4-column grid, centered abbreviations, only unlocked titans.
            _manualView = new Panel { Location = new Point(UiTheme.S(10), top), Size = new Size(W - UiTheme.S(54), UiTheme.S(224)), BackColor = UiTheme.Ground, Visible = false, Tag = "exclusive" };
            Controls.Add(_manualView);
            _manualView.Controls.Add(new Label { Text = "MANUAL KILL TARGETS", AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(0, UiTheme.S(4)) });
            // Canonical vocabulary: this is the DECISIONS layer, so it says ADVISOR — not "advisor mode".
            var mToggle = new Button { Text = "SWITCH TO ADVISOR", Size = new Size(UiLayout.BtnWidth("SWITCH TO ADVISOR"), UiTheme.SCtl(24)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat };
            mToggle.FlatAppearance.BorderColor = UiTheme.Border;
            mToggle.Location = new Point(_manualView.Width - mToggle.Width, 0);
            UiTheme.ApplyState(mToggle, UiTheme.Cap, Color.White);
            mToggle.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.AdvisorTitans = true;
                SyncFromSettings();
            };
            _manualView.Controls.Add(mToggle);
            int killW = (_manualView.Width - UiTheme.S(18)) / 4;   // 148 legacy
            for (int i = 0; i < 14; i++)
            {
                int idx = i;
                var b = new Button { Size = new Size(killW, UiTheme.SCtl(24)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat, Visible = false };
                b.FlatAppearance.BorderColor = UiTheme.Border;
                b.Click += (s, e) =>
                {
                    if (Settings == null) return;
                    try
                    {
                        var arr = (Settings.TitanSwapTargets ?? new bool[14]).ToArray();
                        if (arr.Length < 14) Array.Resize(ref arr, 14);
                        arr[idx] = !arr[idx];
                        Settings.TitanSwapTargets = arr;
                    }
                    catch (Exception ex) { LogDebug($"Titan toggle: {ex.Message}"); }
                    SyncFromSettings();
                };
                _killToggles[i] = b;
                _manualView.Controls.Add(b);
            }

            _modeLbl = new Label { Text = "Mode", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground };
            _combatMode = new ComboBox { Width = UiTheme.S(110), DropDownStyle = ComboBoxStyle.DropDownList, Font = UiTheme.Ui };
            UiTheme.StyleCombo(_combatMode);
            _combatMode.Items.AddRange(new object[] { "Idle", "Snipe", "Defensive", "Offensive" });
            _combatMode.SelectedIndexChanged += (s, e) => { if (!_syncing && Settings != null) Settings.TitanCombatMode = _combatMode.SelectedIndex; };
            _beast = new Button { Text = "Beast Mode", Size = new Size(UiLayout.BtnWidth("Beast Mode"), UiTheme.SCtl(24)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat };
            _beast.FlatAppearance.BorderColor = UiTheme.Border;
            _beast.Click += (s, e) =>
            {
                if (Settings == null) return;
                Settings.TitanBeastMode = !Settings.TitanBeastMode;
                SyncFromSettings();
            };
            // Advisor mode picks combat posture itself (AK-ratio heuristic) — the controls give way to
            // a read-only summary of its choice.
            _combatSummary = new Label { Text = "", AutoSize = true, Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Visible = false, Location = new Point(UiTheme.S(10), top + UiTheme.S(241)) };
            Controls.Add(_modeLbl);
            Controls.Add(_combatMode);
            Controls.Add(_beast);
            Controls.Add(_combatSummary);
            UiLayout.Row(UiTheme.S(10), top + UiTheme.S(236), UiTheme.S(8), _modeLbl, _combatMode, _beast);

            SyncFromSettings();
        }

        private ReqBox MkReqBox(int x, int w)
        {
            var rb = new ReqBox();
            // DPI truth: the 9pt stats line renders ~25px tall — bar must start at y52+ or the text
            // overlaps it (user caught the regression when the caption fix moved the bar to y44).
            rb.Box = new Panel { Location = new Point(x, UiTheme.S(62)), Size = new Size(w, UiTheme.S(84)), BackColor = UiTheme.Zebra, BorderStyle = BorderStyle.FixedSingle };
            rb.Title = new Label { Text = "", AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Zebra, Location = new Point(UiTheme.S(8), UiTheme.S(2)) };
            rb.Stats = new Label { Text = "", AutoSize = false, Size = new Size(w - UiTheme.S(16), UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Ink, BackColor = UiTheme.Zebra, Location = new Point(UiTheme.S(8), UiTheme.S(24)) };
            rb.BarOuter = new Panel { Location = new Point(UiTheme.S(8), UiTheme.S(52)), Size = new Size(w - UiTheme.S(16), UiTheme.S(9)), BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle };
            rb.BarInner = new Panel { Location = new Point(0, 0), Size = new Size(0, UiTheme.S(7)), BackColor = UiTheme.Accent };
            rb.BarOuter.Controls.Add(rb.BarInner);
            rb.Caption = new Label { Text = "", AutoSize = false, Size = new Size(w - UiTheme.S(16), UiTheme.SHead(18)), Font = UiTheme.Chip, ForeColor = UiTheme.Muted, BackColor = UiTheme.Zebra, Location = new Point(UiTheme.S(8), UiTheme.S(64)) };
            // The caption is floored at the measured header line, so the 84px box no longer contains it —
            // keep its original 2px bottom padding instead of scaling the box height on its own.
            rb.Box.Height = Math.Max(rb.Box.Height, rb.Caption.Bottom + UiTheme.S(2));
            rb.Box.Controls.Add(rb.Title);
            rb.Box.Controls.Add(rb.Stats);
            rb.Box.Controls.Add(rb.BarOuter);
            rb.Box.Controls.Add(rb.Caption);
            _heroCard.Controls.Add(rb.Box);
            return rb;
        }

        private static bool Versioned(int i) => i >= FirstVersioned && i <= LastVersioned;

        private static string TitanTag(int i)
        {
            string name = i >= 0 && i < Abbrev.Length ? Abbrev[i] : $"T{i + 1}";
            if (!Versioned(i)) return name;
            int v = 1;
            try { v = ZoneHelpers.TitanVersion(i); } catch { }
            return $"{name}  v{v}/{OptimizationAdvisor.AkVersionCount(i)}";
        }

        private static bool RiddleUnlocked(int i)
        {
            try
            {
                var adv = Main.Character.adventure;
                if (i == 5) return adv.titan6Unlocked;
                if (i == 6) return adv.titan7Unlocked;
                if (i == 7) return adv.titan8Unlocked;
            }
            catch { }
            return true;
        }

        private static string RiddleProgress(int i)
        {
            try
            {
                var adv = Main.Character.adventure;
                if (i == 5)
                {
                    int clues = (adv.clue1Complete ? 1 : 0) + (adv.clue2Complete ? 1 : 0) + (adv.clue3Complete ? 1 : 0) + (adv.clue4Complete ? 1 : 0);
                    return $"clues {clues}/4";
                }
                if (i == 6) return $"locks {adv.titan7QuestSequence}/5";
            }
            catch { }
            return "riddle";
        }

        // Visible anywhere = zone reachable (post riddle-bug-fix) — locked titans are hidden entirely.
        private static bool Reachable(int i, int maxZone) => ZoneHelpers.TitanZones[i] <= maxZone;

        public void SyncFromSettings()
        {
            if (Settings == null) return;
            _syncing = true;
            try
            {
                // Reflects both layers, including a flip made from the legacy twin toggles below or
                // from a settings reload. Sync() never raises Changed, so this cannot recurse.
                _controlBar?.Sync();

                bool advisor = Settings.AdvisorTitans;
                _srcToggle.Text = advisor ? "ADVISOR" : "MANUAL";
                UiTheme.ApplyState(_srcToggle, advisor ? UiTheme.Cap : UiTheme.Danger, Color.White);
                _heroCard.Visible = advisor;
                _chipArea.Visible = advisor;
                _manualView.Visible = !advisor;

                int cm = Settings.TitanCombatMode;
                if (cm >= 0 && cm < _combatMode.Items.Count) _combatMode.SelectedIndex = cm;
                UiTheme.ApplyState(_beast, Settings.TitanBeastMode ? UiTheme.Cap : UiTheme.Danger, Color.White);

                // Advisor manages combat posture: controls hidden, summary shown.
                _modeLbl.Visible = _combatMode.Visible = _beast.Visible = !advisor;
                _combatSummary.Visible = advisor;
                if (advisor)
                {
                    string[] modes = { "Idle", "Snipe", "Defensive", "Offensive" };
                    string mode = cm >= 0 && cm < modes.Length ? modes[cm] : "?";
                    _combatSummary.Text = $"Combat: {mode} · Beast {(Settings.TitanBeastMode ? "on" : "off")} — advisor-managed";
                }

                if (advisor) RefreshHero();
                else RefreshManual();
            }
            finally { _syncing = false; }
        }

        private void RefreshManual()
        {
            int maxZone = 0;
            try { maxZone = ZoneHelpers.GetMaxReachableZone(true); } catch { }
            var targets = Settings.TitanSwapTargets ?? new bool[14];

            int col = 0, row = 0;
            for (int i = 0; i < 14; i++)
            {
                bool show = Reachable(i, maxZone) && RiddleUnlocked(i);
                _killToggles[i].Visible = show;
                if (!show) continue;

                bool on = i < targets.Length && targets[i];
                _killToggles[i].Text = TitanTag(i);
                UiTheme.ApplyState(_killToggles[i], on ? UiTheme.Cap : UiTheme.Danger, Color.White);
                _killToggles[i].Location = new Point(col * (_killToggles[i].Width + UiTheme.S(6)), UiTheme.S(34) + row * UiTheme.S(28));
                col++;
                if (col == 4) { col = 0; row++; }
            }
            UiLayout.AuditOnce(_manualView, "Titans/MANUAL");
        }

        private void RefreshHero()
        {
            try
            {
                var c = Main.Character;
                if (c == null) return;

                var objv = OptimizationAdvisor.NextObjective();
                int target = objv.Known ? objv.Index : -1;
                int maxZone = ZoneHelpers.GetMaxReachableZone(true);

                // Challenge banner: reduced stats + constant rebirths make below-AK titans unviable —
                // AK'd ones keep dying automatically (AK flags are permanent). Targeting is paused.
                string challenge = null;
                try { challenge = ChallengeDetector.Current(); } catch { }
                if (challenge != null)
                {
                    SetState($"Challenge active ({challenge})", "Only auto-killed titans are viable — targeting paused until the challenge ends.");
                    RefreshChips(-1, maxZone);
                    return;
                }

                if (target < 0)
                {
                    SetState("All titans auto-killed at this difficulty",
                        "Nothing left here until the next difficulty — bosses and NGUs are the push now.");
                }
                else if (!Reachable(target, maxZone))
                {
                    int zone = ZoneHelpers.TitanZones[target];
                    string unlock = zone < ZoneHelpers.ZoneUnlocks.Length ? ZoneHelpers.ZoneUnlocks[zone].ToString() : "?";
                    SetState($"{Abbrev[target]} (not yet available)", $"Zone unlocks at boss {unlock} (current: {c.bossID - 1})");
                }
                else if (RiddleTitanIndexes.Contains(target) && !RiddleUnlocked(target))
                {
                    SetState($"{Abbrev[target]} (riddle pending)", $"Unlock riddle in progress — {RiddleProgress(target)}");
                }
                else
                {
                    _titleBase = TitanTag(target);
                    _countdownTarget = target;
                    _targetName.Text = _titleBase + SpawnSuffix(target);
                    _stateInfo.Visible = false;

                    double atk = c.totalAdvAttack();
                    double def = c.totalAdvDefense();
                    double rgn = 0;
                    try { rgn = c.totalAdvHPRegen(); } catch { }

                    // The chase is the OBJECTIVE version at its KILL-LADDER stage (user rule):
                    // never killed -> manual first-kill stats; killed -> idle stats; then AK
                    // (which from T4 up also gates on HP regen — shown as a third stat).
                    int ver = objv.Version;
                    string stageTitle = (objv.Stage ?? "auto-kill").ToUpperInvariant();

                    _akBox.Box.Visible = true;
                    _akBox.Title.Text = Versioned(target) ? $"{stageTitle} (v{ver})" : stageTitle;
                    // Gear-swap projection (user request): if current gear falls short but the
                    // optimizer's best P/T set would clear it, the caption says so — the titan
                    // swap machinery equips kill gear at spawn, so that IS killable now. The
                    // optimizer doesn't project regen, so the claim needs it already met.
                    string cap = objv.ReqRegen > 0
                        ? $"Current {Fmt(atk)} / {Fmt(def)} / {Fmt(rgn)}"
                        : $"Current {Fmt(atk)} / {Fmt(def)}";
                    // Guide footnote: manual first-kill stats assume max move-cooldown gear + Beast Mode.
                    if (objv.Stage == "first kill") cap += " · CD gear+BM";
                    if (atk < objv.ReqAttack || def < objv.ReqDefense)
                    {
                        OptimizationAdvisor.ProjectedBestGear(out var am, out var dm);
                        if (atk * am >= objv.ReqAttack && def * dm >= objv.ReqDefense
                            && (objv.ReqRegen <= 0 || rgn >= objv.ReqRegen))
                            cap = $"✓ with best P/T gear (≈{Fmt(atk * am)} / {Fmt(def * dm)})";
                    }
                    FillBox(_akBox, objv.ReqAttack, objv.ReqDefense, objv.ReqRegen, atk, def, rgn, cap);

                    int maxVer = OptimizationAdvisor.AkVersionCount(target);
                    if (Versioned(target) && ver < maxVer)
                    {
                        // The next version has never been killed by definition — first-kill stats.
                        OptimizationAdvisor.StagedRequirementFor(target, ver + 1, out var nReqA, out var nReqD, out var nReqR, out var nStage);
                        _verBox.Box.Visible = true;
                        _verBox.Title.Text = $"NEXT VERSION (v{ver + 1} · {nStage})";
                        FillBox(_verBox, nReqA, nReqD, nReqR, atk, def, rgn, $"Current {Fmt(atk)} / {Fmt(def)}");
                    }
                    else if (Versioned(target))
                    {
                        _verBox.Box.Visible = true;
                        _verBox.Title.Text = "TOP VERSION";
                        UiLayout.FitInto(_verBox.Stats, "Nothing further");
                        _verBox.BarInner.Width = 0;
                        UiLayout.FitInto(_verBox.Caption, "");
                    }
                    else
                    {
                        _verBox.Box.Visible = false;
                    }
                }

                RefreshChips(target, maxZone);
            }
            catch (Exception ex) { LogDebug($"Titan hero: {ex.Message}"); }
        }

        // Live spawn countdown (user request): the swap machinery already reads the spawn timers —
        // this just shows them. Ticked from the form's status pump while the tab is visible.
        private string _titleBase;
        private int _countdownTarget = -1;
        private DateTime _lastCountdownTick = DateTime.MinValue;

        private static string SpawnSuffix(int titanIndex)
        {
            try
            {
                float? t = ZoneHelpers.TimeTillTitanSpawn(titanIndex);
                if (!t.HasValue) return "";
                if (t.Value <= 1) return "  ·  SPAWNING";
                int m = (int)(t.Value / 60), s = (int)(t.Value % 60);
                return m > 0 ? $"  ·  spawns in {m}m {s:00}s" : $"  ·  spawns in {s}s";
            }
            catch { return ""; }
        }

        public void TickCountdown()
        {
            if (!Visible || _countdownTarget < 0 || _titleBase == null) return;
            if ((DateTime.UtcNow - _lastCountdownTick).TotalSeconds < 5) return;
            _lastCountdownTick = DateTime.UtcNow;
            try { _targetName.Text = _titleBase + SpawnSuffix(_countdownTarget); } catch { }
        }

        private void SetState(string name, string info)
        {
            _countdownTarget = -1;
            _targetName.Text = name;
            UiLayout.FitOrGrow(_stateInfo, info);   // req boxes are hidden in state mode — free to wrap
            _stateInfo.Visible = true;
            _akBox.Box.Visible = false;
            _verBox.Box.Visible = false;
        }

        private static void FillBox(ReqBox rb, double reqA, double reqD, double reqR, double atk, double def, double rgn, string caption)
        {
            // Tighter separators when the regen gate joins the line — three stats must share it.
            string stats = reqR > 0
                ? $"ADV P {Fmt(reqA)} / ADV T {Fmt(reqD)} / RGN {Fmt(reqR)}"
                : $"ADV P {Fmt(reqA)}  /  ADV T {Fmt(reqD)}";
            UiLayout.FitInto(rb.Stats, stats);
            double pct = Math.Min(reqA > 0 ? atk / reqA : 1, reqD > 0 ? def / reqD : 1);
            if (reqR > 0) pct = Math.Min(pct, rgn / reqR);
            pct = Math.Min(1.0, pct);
            rb.BarInner.Width = (int)((rb.BarOuter.Width - 2) * pct);
            UiLayout.FitInto(rb.Caption, $"{pct * 100:0}% · {caption}");
        }

        // Chip-strip signature of the last build — rebuild only when the content changes. This runs
        // on every settings save (sync path), and Controls.Clear() does NOT dispose: the orphaned
        // chips kept their native handles and exhausted the process GDI budget over a long session
        // (user-reported — the form died with GDI+ OutOfMemory and could no longer open).
        private string _chipSig;

        private void RefreshChips(int target, int maxZone)
        {
            var items = new List<KeyValuePair<string, Color>>();
            for (int i = 0; i < ZoneHelpers.TitanZones.Length && i < 14; i++)
            {
                try
                {
                    if (i == target) continue;
                    if (!Reachable(i, maxZone)) continue;   // locked titans hidden entirely

                    if (RiddleTitanIndexes.Contains(i) && !RiddleUnlocked(i))
                    {
                        items.Add(new KeyValuePair<string, Color>($"{Abbrev[i]} {RiddleProgress(i)}", UiTheme.Wandoos));
                    }
                    else
                    {
                        bool ak = false;
                        try { ak = ZoneHelpers.AutokillAvailable(i); } catch { }
                        if (ak)
                            items.Add(new KeyValuePair<string, Color>(
                                Versioned(i) ? $"✓ {Abbrev[i]} v{ZoneHelpers.TitanVersion(i)}/{OptimizationAdvisor.AkVersionCount(i)}" : $"✓ {Abbrev[i]}",
                                UiTheme.Cap));
                        else
                            items.Add(new KeyValuePair<string, Color>($"{Abbrev[i]} queued", UiTheme.Danger));
                    }
                }
                catch (Exception ex) { LogDebug($"Titan chip {i}: {ex.Message}"); }
            }

            string sig = string.Join("|", items.Select(kv => $"{kv.Key}:{kv.Value.ToArgb()}").ToArray());
            if (sig == _chipSig) return;

            UiLayout.DisposeChildren(_chipArea);

            var chips = new List<Control>();
            foreach (var kv in items)
            {
                var chip = new Label
                {
                    Text = kv.Key,
                    AutoSize = false,
                    Size = new Size(UiLayout.MeasureText(kv.Key, UiTheme.Chip) + UiTheme.S(14), UiTheme.SHead(18)),
                    Font = UiTheme.Chip,
                    ForeColor = Color.White,
                    BackColor = kv.Value,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                chips.Add(chip);
                _chipArea.Controls.Add(chip);
            }
            UiLayout.WrapRow(0, UiTheme.S(4), UiTheme.S(6), _chipArea.Width - UiTheme.S(6), UiTheme.S(24), chips);
            // Sig LAST: chip construction measures text, which can throw under GDI pressure, and
            // RefreshHero's catch swallows it. Committing the sig first would pin a half-built strip
            // until the titan set itself changed; leaving it stale means the next refresh retries.
            _chipSig = sig;
        }

        // Full suffix ladder (matches OptimizationAdvisor.Fmt) — capping at B rendered T6v4's
        // 2.5e12 as "2500B" and would only get worse from T7 (5e14) up.
        private static string Fmt(double v) => NumberFormatter.Abbrev(v);   // consolidated (finding #31)
    }
}
