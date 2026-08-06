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
                Height = UiTheme.SText(28),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(UiTheme.S(10), 0, 0, 0),
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Ink,
                Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold)
            };

            _tabs = new TabControl { Dock = DockStyle.Fill };

            // The bar is DERIVED from the buttons it holds - flooring a button height and scaling its
            // container alongside it is what clips silently (see ui-infra.md, SLines corollary).
            int btnH = UiTheme.SCtl(28);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = btnH + UiTheme.S(16), FlowDirection = FlowDirection.LeftToRight, BackColor = UiTheme.Surface, Padding = new Padding(UiTheme.S(8), UiTheme.S(8), 0, 0) };
            // "Save Profile" and "Update Profile" swap into the same button, so size it for the longer one.
            _saveBtn = new Button { Text = "Update Profile", Width = UiLayout.BtnWidth("Update Profile"), Height = btnH };
            var reloadBtn = new Button { Text = "Reload Profile", Width = UiLayout.BtnWidth("Reload Profile"), Height = btnH };
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
                Height = UiTheme.SText(24),
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
            Shown += (s, e) => AuditTabs();
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
                if (Visible) AuditTabs();   // a Reload rebuilds the cards; re-audit the fresh tree
                // Advice, not an error: the profile loads either way, so it shows in the status line and
                // in the log rather than blocking anything.
                var advice = ProfileValidator.Warnings(raw);
                if (advice.Count > 0)
                {
                    foreach (var warning in advice)
                        Main.Log($"Profile advice: {warning}");
                    SetStatus(advice.Count == 1 ? advice[0] : $"{advice[0]} (+{advice.Count - 1} more in the log)", false);
                }
                else
                {
                    SetStatus($"Loaded E:{_model.Energy.Count} M:{_model.Magic.Count} R3:{_model.R3.Count} Gear:{_model.Gear.Count} Diggers:{_model.Diggers.Count} Beards:{_model.Beards.Count} breakpoints. Other systems preserved.", false);
                }
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
            UiTheme.OwnerDrawTabs(_tabs); // after the pages exist - the strip is sized from the captions
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
        }

        // This window never ran the layout auditor — only SettingsForm did — so "UI AUDIT" did not exist for
        // it to be checked. Diagnostic only: Audit() measures and logs, it never moves a control.
        //
        // It audits EVERY tab, not just Gear. While only the Gear panel was audited, the log's issue count
        // was a floor: the ✕/↑/↓ row buttons and the numeric rows exist on all eight tabs, so a clipped
        // caption on Energy or Misc had nothing reporting it.
        private void AuditTabs()
        {
            try
            {
                foreach (TabPage page in _tabs.TabPages)
                    foreach (Control child in page.Controls)
                        UiLayout.Audit(child, page.Text);
            }
            catch (Exception e) { Main.LogDebug($"Profile editor audit: {e.Message}"); }
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
