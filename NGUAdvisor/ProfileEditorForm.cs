using System;
using System.IO;
using System.Windows.Forms;
using NGUAdvisor.Managers;

namespace NGUAdvisor
{
    // Code-built profile editor window (no designer/.resx), so it can grow without touching the main
    // SettingsForm's locked-down localized layout.
    //
    // Phase 2: edit the three resource priority timelines (Energy / Magic / R3) - add/remove/reorder
    // breakpoints and priorities via a typo-proof picker - then Save writes the profile back to disk,
    // which the advisor's file watcher hot-reloads. All other profile systems are preserved verbatim.
    public class ProfileEditorForm : Form
    {
        private static ProfileEditorForm _instance;

        private readonly Label _header;
        private readonly Label _status;
        private readonly TabControl _tabs;
        private readonly Button _saveBtn;

        private ProfileModel _model;
        private string _dir;
        private string _profile;
        private bool _dirty;
        private bool _isNewProfile;

        // Teardown on unload (Main.Unload): an open editor must not survive as an orphan window.
        public static void CloseEditor()
        {
            try
            {
                if (_instance != null && !_instance.IsDisposed)
                {
                    _instance.Close();
                    _instance.Dispose();
                }
            }
            catch { }
            _instance = null;
        }

        public static void ShowEditor(string profilesDir, string profileName)
        {
            try
            {
                if (_instance == null || _instance.IsDisposed)
                    _instance = new ProfileEditorForm();
                _instance.LoadProfile(profilesDir, profileName);
                if (!_instance.Visible) _instance.Show();
                _instance.BringToFront();
                _instance.Activate();
            }
            catch (Exception e)
            {
                Main.LogDebug($"ProfileEditor open failed: {e.Message}");
                Main.LogDebug(e.StackTrace);
            }
        }

        public ProfileEditorForm()
        {
            Text = "NGU Advisor - Profile Editor";
            Width = UiTheme.S(820);
            Height = UiTheme.S(620);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new System.Drawing.Size(UiTheme.S(680), UiTheme.S(460));
            Width = UiTheme.S(900);
            BackColor = UiTheme.Ground;
            Font = UiTheme.Ui;

            _header = new Label
            {
                Dock = DockStyle.Top,
                Height = UiTheme.S(28),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(UiTheme.S(10), 0, 0, 0),
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Ink,
                Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold)
            };

            _tabs = new TabControl { Dock = DockStyle.Fill };

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = UiTheme.S(44), FlowDirection = FlowDirection.LeftToRight, BackColor = UiTheme.Surface, Padding = new Padding(UiTheme.S(8), UiTheme.S(8), 0, 0) };
            _saveBtn = new Button { Text = "Update Profile", Width = UiTheme.S(140), Height = UiTheme.S(28) };
            var reloadBtn = new Button { Text = "Reload Profile", Width = UiTheme.S(130), Height = UiTheme.S(28) };
            UiTheme.StylePrimary(_saveBtn);
            UiTheme.StyleFlat(reloadBtn);
            _saveBtn.Click += (s, e) => Save();
            reloadBtn.Click += (s, e) =>
            {
                if (ConfirmDiscardIfDirty())
                    LoadProfile(_dir, _profile);
            };
            bottom.Controls.Add(_saveBtn);
            bottom.Controls.Add(reloadBtn);

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = UiTheme.S(24),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(UiTheme.S(10), 0, 0, 0),
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Muted
            };

            Controls.Add(_tabs);
            Controls.Add(bottom);
            Controls.Add(_status);
            Controls.Add(_header);

            // Audit at Shown, like SettingsForm: before that the controls have no real rendered sizes.
            Shown += (s, e) => AuditGear();
        }

        public void LoadProfile(string profilesDir, string profileName)
        {
            _dir = profilesDir;
            _profile = profileName;

            var path = Path.Combine(profilesDir, profileName + ".json");
            _tabs.TabPages.Clear();

            // A profile that doesn't exist yet is a NEW profile: start from an empty model so it can be built
            // and saved. Save vs Update text keys off this.
            if (!File.Exists(path))
            {
                _isNewProfile = true;
                _model = new ProfileModel();
                BuildTabs();
                _dirty = false;
                UpdateSaveText();
                UpdateHeader();
                SetStatus("New profile — add breakpoints, then Save Profile to create the file.", false);
                return;
            }

            _isNewProfile = false;
            var raw = File.ReadAllText(path);
            var validation = ProfileValidator.Validate(raw);
            if (!validation.Ok)
            {
                SetStatus($"Invalid JSON at line {validation.Line}, col {validation.Col}: {validation.Message}. Fix the file, then Reload.", true);
                _model = null;
                UpdateSaveText();
                UpdateHeader();
                return;
            }

            try
            {
                _model = ProfileModel.Load(raw);
                BuildTabs();
                _dirty = false;
                UpdateSaveText();
                UpdateHeader();
                if (Visible) AuditGear();   // a Reload rebuilds the cards; re-audit the fresh tree
                SetStatus($"Loaded E:{_model.Energy.Count} M:{_model.Magic.Count} R3:{_model.R3.Count} Gear:{_model.Gear.Count} Diggers:{_model.Diggers.Count} Beards:{_model.Beards.Count} breakpoints. Other systems preserved.", false);
            }
            catch (Exception e)
            {
                SetStatus($"Could not read profile: {e.Message}", true);
                _model = null;
                UpdateSaveText();
                UpdateHeader();
            }
        }

        private void BuildTabs()
        {
            AddResourceTab("Energy", _model.Energy, UiTheme.Energy, ResourceKind.Energy);
            AddResourceTab("Magic", _model.Magic, UiTheme.Magic, ResourceKind.Magic);
            AddResourceTab("R3", _model.R3, UiTheme.R3, ResourceKind.R3);
            AddGearTab("Gear", _model.Gear, UiTheme.Gear);
            AddListTab("Diggers", _model.Diggers, UiTheme.Diggers, SystemCatalog.Diggers);
            AddListTab("Beards", _model.Beards, UiTheme.Beards, SystemCatalog.Beards);
            AddWanDiffTab();
            AddMiscTab();

            UiTheme.ThemeInputs(_tabs); // dark-mode the input controls in the just-built panels
        }

        private void AddMiscTab()
        {
            var page = new TabPage("Misc") { BackColor = UiTheme.Ground, Padding = new Padding(UiTheme.S(2), UiTheme.S(7), UiTheme.S(2), UiTheme.S(2)) };
            var panel = new MiscEditorPanel(_model);
            panel.Changed += (s, e) => MarkDirty();
            page.Controls.Add(panel);
            _tabs.TabPages.Add(page);
        }

        private void AddWanDiffTab()
        {
            var page = new TabPage("Wandoos+Diff") { BackColor = UiTheme.Ground, Padding = new Padding(UiTheme.S(2), UiTheme.S(7), UiTheme.S(2), UiTheme.S(2)) };
            var panel = new WanDiffEditorPanel(
                _model.Wandoos, UiTheme.Wandoos, SystemCatalog.WandoosOS, "Wandoos OS",
                _model.NGUDiff, UiTheme.NGUDiff, SystemCatalog.Difficulty, "NGU Difficulty");
            panel.Changed += (s, e) => MarkDirty();
            page.Controls.Add(panel);
            _tabs.TabPages.Add(page);
        }

        private void AddGearTab(string title, System.Collections.Generic.List<ProfileModel.ListBreakpoint> data, System.Drawing.Color accent)
        {
            var page = new TabPage(title) { BackColor = UiTheme.Ground, Padding = new Padding(UiTheme.S(2), UiTheme.S(7), UiTheme.S(2), UiTheme.S(2)) };
            var panel = new GearEditorPanel(data, accent);
            panel.Changed += (s, e) => MarkDirty();
            page.Controls.Add(panel);
            _tabs.TabPages.Add(page);
            _gearPanel = panel;
        }

        // This window never ran the layout auditor — only SettingsForm did — so "UI AUDIT [Gear]" did not
        // exist to be checked. One diagnostic call, no behaviour: Audit() only measures and logs.
        private GearEditorPanel _gearPanel;

        private void AuditGear()
        {
            try { if (_gearPanel != null) UiLayout.Audit(_gearPanel, "Gear"); }
            catch (Exception e) { Main.LogDebug($"Gear audit: {e.Message}"); }
        }

        private void AddListTab(string title, System.Collections.Generic.List<ProfileModel.ListBreakpoint> data,
            System.Drawing.Color accent, System.Collections.Generic.IReadOnlyList<System.Collections.Generic.KeyValuePair<int, string>> options)
        {
            var page = new TabPage(title) { BackColor = UiTheme.Ground, Padding = new Padding(UiTheme.S(2), UiTheme.S(7), UiTheme.S(2), UiTheme.S(2)) };
            var panel = new ListEditorPanel(data, accent, options);
            panel.Changed += (s, e) => MarkDirty();
            page.Controls.Add(panel);
            _tabs.TabPages.Add(page);
        }

        private void UpdateSaveText()
        {
            _saveBtn.Text = _isNewProfile ? "Save Profile" : "Update Profile";
        }

        private void AddResourceTab(string title, System.Collections.Generic.List<ProfileModel.PriorityBreakpoint> data, System.Drawing.Color accent, ResourceKind kind)
        {
            var page = new TabPage(title) { BackColor = UiTheme.Ground, Padding = new Padding(UiTheme.S(2), UiTheme.S(7), UiTheme.S(2), UiTheme.S(2)) };
            var panel = new ResourceEditorPanel(data, accent, kind);
            panel.Changed += (s, e) => MarkDirty();
            page.Controls.Add(panel);
            _tabs.TabPages.Add(page);
        }

        private void MarkDirty()
        {
            _dirty = true;
            UpdateHeader();
        }

        private void UpdateHeader()
        {
            _header.Text = _model == null
                ? $"Profile: {_profile}  (not loaded)"
                : $"Profile: {_profile}{(_dirty ? "  *unsaved changes*" : "")}";
        }

        private void Save()
        {
            if (_model == null) { SetStatus("Nothing to save.", true); return; }
            try
            {
                var path = Path.Combine(_dir, _profile + ".json");
                var json = _model.ToJson();

                // Sanity check our own output before writing over the user's profile.
                var v = ProfileValidator.Validate(json);
                if (!v.Ok)
                {
                    SetStatus($"Refusing to save — generated JSON is invalid ({v.Message}). No changes written.", true);
                    Main.LogDebug($"ProfileEditor save aborted: invalid output for {_profile}");
                    return;
                }

                bool wasNew = _isNewProfile;
                File.WriteAllText(path, json);
                _dirty = false;
                _isNewProfile = false;
                UpdateSaveText();
                UpdateHeader();
                SetStatus($"{(wasNew ? "Created" : "Updated")} {_profile}.json. The advisor will hot-reload it.", false);
                Main.Log($"Profile \"{_profile}\" {(wasNew ? "created" : "updated")} from the Profile Editor.");
            }
            catch (Exception e)
            {
                SetStatus($"Save failed: {e.Message}", true);
                Main.LogDebug($"ProfileEditor save failed: {e.Message}");
            }
        }

        private bool ConfirmDiscardIfDirty()
        {
            if (!_dirty) return true;
            return MessageBox.Show("Discard unsaved changes?", "Profile Editor",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private void SetStatus(string msg, bool isError)
        {
            _status.Text = msg;
            _status.ForeColor = isError ? UiTheme.Danger : UiTheme.Ink;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (!ConfirmDiscardIfDirty())
                {
                    e.Cancel = true;
                    return;
                }
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }
    }
}
