using System;
using System.Drawing;
using System.Windows.Forms;
using NGUAdvisor.Managers;

namespace NGUAdvisor
{
    // Shared compact "challenge tag" picker for breakpoint card headers. A tagged breakpoint is
    // preferred by the runtime while that challenge is active (BaseBreakpoints challenge-aware
    // selection); untagged breakpoints form the normal timeline. First item = no tag.
    public static class ChallengeTagUi
    {
        public static ComboBox Attach(Control header, Func<string> get, Action<string> set)
        {
            // DEFAULT ComboBox style only (the gear-source picker proved it; FlatStyle.Flat is an
            // unproven Mono paint path). The CARD's SetWidth anchors this LEFT of the Delete button
            // (Left = _del.Left - Width - 10) so it can never overlap at any card width.
            var combo = new LineComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(UiTheme.S(332), UiTheme.S(5)),
                Width = UiTheme.S(112),
                Font = UiTheme.Ui,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Ink
            };
            UiTheme.StyleCombo(combo);
            combo.Items.Add("every run");
            foreach (var ch in SystemCatalog.Challenges)
                combo.Items.Add($"{ch.Code} only");

            string cur = (get() ?? "").Trim().ToUpperInvariant();
            int sel = 0;
            for (int i = 0; i < SystemCatalog.Challenges.Count; i++)
                if (SystemCatalog.Challenges[i].Code == cur) { sel = i + 1; break; }
            if (sel == 0 && cur != "")
            {
                combo.Items.Add($"{cur} only");   // unknown code from the file — selectable so it round-trips
                sel = combo.Items.Count - 1;
            }
            combo.SelectedIndex = sel;

            combo.SelectedIndexChanged += (s, e) =>
            {
                string item = combo.SelectedItem?.ToString() ?? "";
                set(combo.SelectedIndex <= 0 ? "" : item.Replace(" only", "").Trim());
            };

            header.Controls.Add(combo);
            return combo;
        }
    }
}
