using System;
using System.Drawing;
using System.Windows.Forms;
using NGUAdvisor.Managers;
using static NGUAdvisor.Main;

namespace NGUAdvisor
{
    // Systems > AT — the game's five Advanced Training level-target boxes, reachable from the advisor.
    // A slot the level planner rewrites every tick is shown but not editable (LevelPlanner.AtTargetOwner):
    // a write there would last one tick, or be restored over by the planner's snapshot later.
    // Writes are requested; Main.Update applies them on the Unity main thread.
    public class AtTargetsPanel : Panel
    {
        private const string Explain =
            "0 = no cap · -1 = paused (the slot takes no energy) · any other number stops the slot at that level. "
            + "These are the same targets the game shows on its Advanced Training page.";

        private readonly NumericUpDown[] _inputs;
        private readonly Button[] _sets;
        private readonly Label[] _states;
        private readonly bool[] _dirty;
        private bool _syncing;

        public int ContentHeight { get; private set; }

        public AtTargetsPanel(int canvasW)
        {
            int inner = canvasW - UiTheme.S(20);
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Ground;

            var head = new Label
            {
                Text = "LEVEL TARGETS", AutoSize = true, Font = UiTheme.ColHeader,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), UiTheme.S(10))
            };
            Controls.Add(head);

            var explain = new Label
            {
                AutoSize = false, Size = new Size(inner, UiTheme.TextH), Font = UiTheme.Ui,
                ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground,
                Location = new Point(UiTheme.S(10), head.Bottom + UiTheme.S(4))
            };
            Controls.Add(explain);
            UiLayout.FitOrGrow(explain, Explain);

            string[] names = AtPanel.SlotNames;
            int nameW = 0;
            foreach (string n in names) nameW = Math.Max(nameW, UiLayout.MeasureText(n, UiTheme.Ui));
            int setW = 0;
            foreach (string n in names) setW = Math.Max(setW, UiLayout.BtnWidth(SetCaption(n)));

            _inputs = new NumericUpDown[names.Length];
            _sets = new Button[names.Length];
            _states = new Label[names.Length];
            _dirty = new bool[names.Length];

            int y = explain.Bottom + UiTheme.S(10);
            for (int i = 0; i < names.Length; i++)
            {
                int slot = i;
                var name = new Label
                {
                    Text = names[i], AutoSize = false, Size = new Size(nameW, UiTheme.TextH), Font = UiTheme.Ui,
                    ForeColor = UiTheme.Ink, BackColor = UiTheme.Ground
                };
                var input = new NumericUpDown
                {
                    Minimum = -1, Maximum = LevelPlanner.MaxAtTarget, Width = UiTheme.NumWidthFor("2000000000"),
                    Font = UiTheme.Ui, TextAlign = HorizontalAlignment.Right
                };
                UiTheme.StyleNum(input);
                input.ValueChanged += (s, e) => { if (!_syncing) _dirty[slot] = true; };
                var set = new Button { Text = SetCaption(names[i]), Size = new Size(setW, UiTheme.SCtl(24)), Font = UiTheme.Ui };
                UiTheme.StyleFlat(set);
                set.Click += (s, e) =>
                {
                    Main.RequestAtTarget(slot, (long)input.Value);
                    _dirty[slot] = false;
                };
                var state = new Label
                {
                    AutoSize = false, Size = new Size(UiTheme.S(10), UiTheme.TextH), Font = UiTheme.Ui,
                    ForeColor = UiTheme.Muted, BackColor = UiTheme.Ground
                };
                Controls.Add(name);
                Controls.Add(input);
                Controls.Add(set);
                Controls.Add(state);
                int bottom = UiLayout.Row(UiTheme.S(10), y, UiTheme.S(8), name, input, set, state);
                state.Width = Math.Max(UiTheme.S(10), inner + UiTheme.S(10) - state.Left);

                _inputs[i] = input;
                _sets[i] = set;
                _states[i] = state;
                y = bottom + UiTheme.S(6);
            }

            ContentHeight = y + UiTheme.S(4);
            VisibleChanged += (s, e) => { if (Visible) SyncFromSettings(); };
        }

        private static string SetCaption(string slotName) => $"Set {slotName} target";

        // Called from SettingsForm's deferred main-thread pass. An edited, not yet applied value is never
        // overwritten by the live one — the refresh runs every second and would undo the typing.
        public void SyncFromSettings()
        {
            try
            {
                if (!Visible) return;
                Character c = Main.Character;
                if (c == null) return;

                _syncing = true;
                for (int slot = 0; slot < _inputs.Length; slot++)
                {
                    long level = c.advancedTraining.level[slot];
                    long target = c.advancedTraining.levelTarget[slot];
                    string owner = LevelPlanner.AtTargetOwner(slot);
                    bool editable = owner == null;

                    _inputs[slot].Enabled = editable;
                    _sets[slot].Enabled = editable;
                    if (!editable) _dirty[slot] = false;
                    if (!_dirty[slot] && !_inputs[slot].Focused)
                        _inputs[slot].Value = Math.Max(_inputs[slot].Minimum, Math.Min(_inputs[slot].Maximum, target));

                    string held = target == 0 ? "no cap" : target < 0 ? "paused" : $"stops at {NumberFormatter.Abbrev(target)}";
                    _states[slot].ForeColor = editable ? UiTheme.Muted : UiTheme.Cap;
                    UiLayout.FitInto(_states[slot], $"Lv {NumberFormatter.Abbrev(level)} · now {held}"
                        + (editable ? "" : $" · held by {owner} — turn the auto profile off to set it"));
                }
            }
            catch (Exception ex) { LogDebug($"AT targets refresh: {ex.Message}"); }
            finally { _syncing = false; }
        }
    }
}
