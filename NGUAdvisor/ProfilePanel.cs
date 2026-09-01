using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // UI4: the dedicated PROFILE section — the canonical MUTABLE UI for the allocation source
    // (Settings.AutoProfile), the selected/standby profile file (Settings.AllocationFile), applying the
    // advisor's recommendation, and opening the editor / profiles folder.
    //
    // Every mutation reuses an EXISTING validated path and this panel owns NO new authority:
    //   • source     -> flips Settings.AutoProfile (the same setter the old Overview toggle used)
    //   • SWITCH/APPLY -> SettingsForm.ApplyProfile (WarnIfProfileInvalid -> AllocationFile -> Main.RequestAllocationReload)
    //   • EDIT       -> ProfileEditorForm.ShowEditor(GetProfilesDir(), AllocationFile)
    //   • FILES      -> opens GetProfilesDir()
    // ProfilePanel NEVER instantiates CustomAllocation, calls LoadAllocation, writes settings.json, or writes
    // a profile file. Validation here is READ-ONLY (File.Exists + ProfileValidator) and changes no behaviour.
    // Sync() runs on show/nav/action only — it is NOT on any per-frame path, so there is no throttle here.
    public class ProfilePanel : Panel
    {
        private readonly SettingsForm _form;
        private readonly ToolTip _tips = new ToolTip();

        private Button _srcAuto, _srcFile;   // ALLOCATION SOURCE segment
        private Label _srcExplain;
        private ComboBox _combo;             // SELECTED FILE
        private Button _switchBtn;
        private Label _fileStatus, _fileValid;
        private Label _recLine, _recReason, _recRelation;   // RECOMMENDED FILE
        private Button _applyBtn;
        private Button _editBtn, _filesBtn, _refreshBtn;  // PROFILE TOOLS
        private Label _emptyNote;

        private string _recommended = "";
        private readonly int _contentW;

        private enum FileState { Valid, Missing, Invalid }

        public ProfilePanel(SettingsForm form, int canvasW)
        {
            _form = form;
            BackColor = UiTheme.Ground;
            Width = canvasW;
            int m = UiTheme.S(20);
            _contentW = canvasW - m - UiTheme.S(20);

            Controls.Add(new Label { Text = "PROFILE", AutoSize = true, Font = UiTheme.Bold, ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground, Location = new Point(m, UiTheme.S(8)) });

            // ---- ALLOCATION SOURCE ----
            MkHeader("ALLOCATION SOURCE", m, UiTheme.S(42));
            _srcAuto = MkToggle("ADVISOR-GENERATED");
            _srcFile = MkToggle("PROFILE FILE");
            _srcAuto.Click += (s, e) => SetSource(true);
            _srcFile.Click += (s, e) => SetSource(false);
            UiLayout.Row(m, UiTheme.S(66), UiTheme.S(8), _srcAuto, _srcFile);
            _srcExplain = MkNote(m, UiTheme.S(98), _contentW);

            // ---- SELECTED FILE ----
            MkHeader("SELECTED FILE", m, UiTheme.S(134));
            _combo = new LineComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = UiTheme.S(260), Font = UiTheme.Ui };
            UiTheme.StyleCombo(_combo);
            Controls.Add(_combo);
            _switchBtn = MkBtn("SWITCH");
            _switchBtn.Click += (s, e) =>
            {
                var sel = _combo.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(sel)) ApplyByName(sel);
            };
            UiLayout.Row(m, UiTheme.S(158), UiTheme.S(8), _combo, _switchBtn);
            _fileStatus = MkValue(m, UiTheme.S(190), _contentW);
            _fileValid = MkNote(m, UiTheme.S(216), _contentW);
            _emptyNote = MkNote(m, UiTheme.S(190), _contentW);   // shown instead of status/valid when no files exist
            _emptyNote.Visible = false;

            // ---- RECOMMENDED FILE ----
            MkHeader("RECOMMENDED FILE", m, UiTheme.S(252));
            _recLine = MkValue(m, UiTheme.S(276), _contentW);
            _recReason = MkNote(m, UiTheme.S(302), _contentW);
            _recRelation = MkValue(m, UiTheme.S(330), UiTheme.S(320));
            _applyBtn = new Button { Text = "APPLY", Size = new Size(UiLayout.BtnWidth("APPLY"), UiTheme.SCtl(24)), Font = UiTheme.Ui, Location = new Point(m + UiTheme.S(330), UiTheme.S(328)), Visible = false };
            UiTheme.StylePrimary(_applyBtn);
            _applyBtn.Click += (s, e) => { if (!string.IsNullOrEmpty(_recommended)) ApplyByName(_recommended); };
            Controls.Add(_applyBtn);

            // ---- PROFILE TOOLS ----
            MkHeader("PROFILE TOOLS", m, UiTheme.S(372));
            _editBtn = MkBtn("EDIT");
            _editBtn.Click += (s, e) => { try { ProfileEditorForm.ShowEditor(GetProfilesDir(), Settings.AllocationFile); } catch (Exception ex) { LogDebug($"Profile edit: {ex.Message}"); } };
            _filesBtn = MkBtn("FILES");
            _filesBtn.Click += (s, e) => { try { System.Diagnostics.Process.Start(GetProfilesDir()); } catch (Exception ex) { LogDebug($"Profile files: {ex.Message}"); } };
            _refreshBtn = MkBtn("REFRESH");
            _refreshBtn.Click += (s, e) => { LoadFiles(); Sync(); };
            UiLayout.Row(m, UiTheme.S(396), UiTheme.S(8), _editBtn, _filesBtn, _refreshBtn);

            Height = UiTheme.S(440);

            LoadFiles();
            // On show: refresh the list, SYNC the final labels/visibility/bounds, THEN audit once. Order
            // matters — the old order audited the PRE-sync state and consumed the one-shot. Run synchronously,
            // NOT via BeginInvoke: this codebase uses no Control.BeginInvoke/Invoke because the injected forms
            // have no reliable WinForms message pump (see [[nguinjector-runtime-constraint]]) — a posted audit
            // could be dropped and lost. Every child here has explicit bounds, so there is no post-Sync reflow
            // to wait for; the synchronous audit already sees the settled tree.
            VisibleChanged += (s, e) => { if (!Visible) return; LoadFiles(); Sync(); UiLayout.AuditOnce(this, "Profile"); };
        }

        // Called from SettingsForm.UpdateProfileList — the AllocationWatcher-driven refresh path. When the
        // profile file SET changes on disk (the editor's Save CREATES a file -> watcher Created ->
        // _reloadProfilesPending -> Main.Update -> LoadAllocationProfiles -> UpdateProfileList), the list here
        // re-scans too, so it is not stale after editing. A concrete event path, not a claim — and the
        // REFRESH button forces the same re-scan on demand.
        public void OnFilesChanged()
        {
            LoadFiles();
            if (Visible) Sync();
        }

        // ---- the sole allocation-source mutation (same setter the Overview toggle used) ----
        private void SetSource(bool advisorGenerated)
        {
            if (Settings == null || Settings.AutoProfile == advisorGenerated) return;
            Settings.AutoProfile = advisorGenerated;
            Log(advisorGenerated
                ? "Auto profile ON — allocation now generated (profile file on standby)"
                : $"Auto profile OFF — back to {Settings.AllocationFile ?? "profile"} timeline");
            Sync();
        }

        // ---- the validated switch path (reuses SettingsForm.ApplyProfile verbatim) ----
        private void ApplyByName(string name)
        {
            try
            {
                _form?.ApplyProfile(name);
                LoadFiles();
                Sync();
                Activity.Completed($"Profile applied — {name}.");
            }
            catch (Exception e)
            {
                LogDebug($"Profile apply: {e.Message}");
                Log($"Could not apply profile '{name}': {e.Message}");
                Activity.Failed("Could not apply the profile — see the log.", logLink: true);
            }
        }

        private void LoadFiles()
        {
            try
            {
                var names = Directory.GetFiles(GetProfilesDir())
                    .Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToArray();
                _combo.Items.Clear();
                _combo.Items.AddRange(names);
                if (Settings != null && names.Contains(Settings.AllocationFile))
                    _combo.SelectedItem = Settings.AllocationFile;
            }
            catch (Exception e) { LogDebug($"Profile LoadFiles: {e.Message}"); }
        }

        // Refresh every label/segment/combo from current state. Called on show, nav, and after each action —
        // never per-frame. ComboBox selection is only reflected here for DISPLAY; it never applies a profile.
        public void Sync()
        {
            if (Settings == null) return;
            try
            {
                bool advisor = Settings.AutoProfile;
                string file = Settings.AllocationFile ?? "-";

                StyleSeg(_srcAuto, advisor);
                StyleSeg(_srcFile, !advisor);
                SetNote(_srcExplain, advisor
                    ? "The advisor generates allocation decisions. The selected file remains configured as a standby file."
                    : "The selected profile file drives the allocation timeline.", UiTheme.Muted);

                bool haveFiles = _combo.Items.Count > 0;
                _combo.Visible = _switchBtn.Visible = _fileStatus.Visible = _fileValid.Visible = haveFiles;
                _emptyNote.Visible = !haveFiles;
                if (!haveFiles)
                {
                    SetNote(_emptyNote, "No profile files found. Use EDIT to create a profile or FILES to inspect the profile folder.", UiTheme.Muted);
                }
                else
                {
                    if (_combo.Items.Contains(file) && _combo.SelectedItem?.ToString() != file) _combo.SelectedItem = file;

                    SetValue(_fileStatus, advisor ? $"Standby profile file: {file}" : $"Active profile file: {file}");
                    var (state, msg) = ValidateFile(file, advisor);
                    SetNote(_fileValid, msg, state == FileState.Valid ? UiTheme.Muted : UiTheme.Danger);
                }

                string reason = "";
                try { var prog = ProgressionAnalyzer.Detect(); _recommended = prog.Known ? prog.RecommendedProfile : ""; reason = prog.RecommendReason; }
                catch { _recommended = ""; }

                if (string.IsNullOrEmpty(_recommended))
                {
                    SetValue(_recLine, "Recommended: —");
                    SetNote(_recReason, "", UiTheme.Faint);
                    SetValue(_recRelation, "");
                    _applyBtn.Visible = false;
                }
                else
                {
                    SetValue(_recLine, $"Recommended: {_recommended}");
                    SetNote(_recReason, string.IsNullOrEmpty(reason) ? "" : $"Reason: {reason}", UiTheme.Faint);
                    bool matches = _recommended == file;
                    // NEVER call the recommendation "current" just because the filename matches while advisor-
                    // generated mode is active — say STANDBY MATCHES instead. Only a file-mode match is "current".
                    string rel = matches
                        ? (advisor ? "STANDBY FILE MATCHES" : "CURRENT PROFILE FILE MATCHES")
                        : "APPLY AS SELECTED FILE";
                    SetValue(_recRelation, rel);
                    _applyBtn.Visible = !matches;
                }
            }
            catch (Exception e) { LogDebug($"Profile sync: {e.Message}"); }
        }

        // READ-ONLY validity of the configured file. Uses only File.Exists + ProfileValidator — it drives the
        // display and nothing else. Messages differ by mode, and NEVER imply an invalid standby file is
        // controlling allocation (advisor-generated allocation remains in control regardless).
        private (FileState, string) ValidateFile(string name, bool advisor)
        {
            try
            {
                if (string.IsNullOrEmpty(name) || name == "-")
                    return (FileState.Missing, advisor ? "No standby file configured." : "No profile file configured.");
                var path = Path.Combine(GetProfilesDir(), name + ".json");
                if (!File.Exists(path))
                    return (FileState.Missing, advisor
                        ? "Standby profile file is missing. Advisor-generated allocation remains active."
                        : "Selected profile file is missing. The profile timeline is unavailable.");
                var v = ProfileValidator.Validate(File.ReadAllText(path));
                if (!v.Ok)
                    return (FileState.Invalid, advisor
                        ? "Standby profile file is invalid. Advisor-generated allocation remains active."
                        : "Selected profile file is invalid. It was not applied.");
                return (FileState.Valid, "Valid.");
            }
            catch (Exception e) { LogDebug($"Profile validate: {e.Message}"); return (FileState.Invalid, "Unavailable."); }
        }

        // ---- build/style helpers ----
        private void MkHeader(string text, int x, int y)
            => Controls.Add(new Label { Text = text, AutoSize = true, Font = UiTheme.ColHeader, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, Location = new Point(x, y) });

        private Label MkValue(int x, int y, int w)
        {
            var l = new Label { Text = "", AutoSize = false, Size = new Size(w, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground, AutoEllipsis = false, Location = new Point(x, y) };
            Controls.Add(l);
            return l;
        }

        private Label MkNote(int x, int y, int w)
        {
            var l = new Label { Text = "", AutoSize = false, Size = new Size(w, UiTheme.TextH), Font = UiTheme.Ui, ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground, AutoEllipsis = false, Location = new Point(x, y) };
            Controls.Add(l);
            return l;
        }

        private Button MkToggle(string text)
        {
            var b = new Button { Text = text, Size = new Size(UiLayout.BtnWidth(text), UiTheme.SCtl(26)), Font = UiTheme.Ui, FlatStyle = FlatStyle.Flat };
            b.FlatAppearance.BorderColor = UiTheme.Border;
            Controls.Add(b);
            return b;
        }

        private Button MkBtn(string text)
        {
            var b = new Button { Text = text, Size = new Size(UiLayout.BtnWidth(text), UiTheme.SCtl(24)), Font = UiTheme.Ui };
            UiTheme.StyleFlat(b);
            Controls.Add(b);
            return b;
        }

        private static void StyleSeg(Button b, bool active)
            => UiTheme.ApplyState(b, active ? UiTheme.Accent : UiTheme.BtnFace, active ? Color.White : UiTheme.Muted);

        // Fixed-width dynamic label: measured-fit display + full text in the shared tooltip (Mono blanks a
        // fixed label whose text overflows, so every dynamic string goes through FitText).
        private void SetValue(Label l, string text)
        {
            _tips.SetToolTip(l, text ?? "");
            string fit = UiLayout.FitText(text ?? "", l.Font, l.Width);
            if (l.Text != fit) l.Text = fit;
        }

        private void SetNote(Label l, string text, Color color)
        {
            _tips.SetToolTip(l, text ?? "");
            string fit = UiLayout.FitText(text ?? "", l.Font, l.Width);
            if (l.Text != fit) l.Text = fit;
            if (l.ForeColor != color) l.ForeColor = color;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tips?.Dispose();   // the one owned ToolTip has no container to dispose it
            base.Dispose(disposing);
        }
    }
}
