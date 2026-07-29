using System;
using System.Drawing;
using System.Windows.Forms;

namespace NGUAdvisor.Managers
{
    // Shared design tokens for the advisor's WinForms UI (editor windows + a runtime theme pass on the
    // main settings form). Mirrors the approved mockup. Everything here is Mono-WinForms-safe: flat colors,
    // borders, the system Segoe UI font - no gradients/rounded corners.
    public static class UiTheme
    {
        // Light theme (original).
        public static readonly Color Ground = Hex("EEF0F3");
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Hex("C6CBD3");
        public static readonly Color BorderStrong = Hex("AEB4BF");
        public static readonly Color Ink = Hex("20242E");
        public static readonly Color Muted = Hex("6A7180");
        public static readonly Color Faint = Hex("9AA1AD");
        public static readonly Color Accent = Hex("3B5BA5");
        public static readonly Color AccentDark = Hex("2F4C8C");
        public static readonly Color AccentWeak = Hex("E4E9F4");
        public static readonly Color Cap = Hex("2F7A55");
        public static readonly Color CapBg = Hex("E6F1EA");
        public static readonly Color Danger = Hex("B23B3B");
        public static readonly Color Zebra = Hex("F7F8FA");
        public static readonly Color BtnFace = Hex("F5F6F8");

        // Per-system identity colors.
        public static readonly Color Energy = Hex("C0851F");
        public static readonly Color Magic = Hex("5B57A6");
        public static readonly Color R3 = Hex("2E8B8B");
        public static readonly Color Diggers = Hex("6E8B3D");
        public static readonly Color Beards = Hex("A0623A");
        public static readonly Color Gear = Hex("4A6D8C");
        public static readonly Color Wandoos = Hex("8C5A9E");
        public static readonly Color NGUDiff = Hex("B5504A");

        public static readonly Font Ui = new Font("Segoe UI", 9f);
        public static readonly Font Bold = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font ColHeader = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        public static readonly Font Chip = new Font("Segoe UI", 7.5f, FontStyle.Bold);

        // DPI-TRUE line metrics (root cause of every stacked-text overlap): the game's Mono renders
        // text at the REAL screen DPI while every hand-placed pixel stays fixed. The values below were
        // hand-tuned on a display where 9pt rendered ~25px tall and 7.5pt headers ~22px; on a 200%
        // display 9pt renders ~38px and the whole layout clipped. The pitches are therefore MEASURED
        // at startup and every hand-placed pixel goes through S() with the same ratio — at the 25px
        // baseline everything stays exactly as tuned (26/24/22, scale 1.0).
        public static readonly int LinePitch;    // between stacked 9pt lines
        public static readonly int HeadPitch;    // section header -> first content line
        public static readonly int TextH;        // min height for a fixed-size single-line 9pt label
        public static readonly int LineH;        // measured rendered height of one 9pt line
        public static readonly int HeadH;        // measured rendered height of one 7.5pt header line
        public static readonly float Scale;      // measured 9pt height / 25px tuning baseline
        public static readonly string CalibrationInfo;

        private const int BaseLineH = 25;   // 9pt rendered height the layout was tuned for
        private const int BaseHeadH = 22;   // 7.5pt header rendered height at the same tuning

        // Scale a hand-placed pixel dimension from the tuning baseline to the measured metrics.
        public static int S(int px) => (int)Math.Round(px * Scale);

        // HEIGHTS THAT HOLD TEXT ARE NOT FREELY SCALABLE. The tuned layout sized many single-line
        // labels at 18-22px for text that rendered 25px — the descenders were already being shaved by
        // 1-3px, invisible at that size. Scaling those heights by the same ratio as everything else
        // PRESERVES the shortfall and multiplies it: an 18px box became 27px for 38px text, so "g"
        // and "y" lost 8px and the clipping became the first thing you see. So a control that holds
        // text takes its height through one of these, which scales AND floors at what the renderer
        // needs — never below.
        //
        //   SText - one line of 9pt inside a Label (glyphs only, tight box is fine)
        //   SHead - one line of 7.5pt caption
        //   SCtl  - a Button / ComboBox / TextBox / NumericUpDown, which paints its text inside its
        //           own chrome and needs the FULL line box or the glyphs sit visibly off-centre
        public static int SText(int px) => Math.Max(S(px), TextH);
        public static int SHead(int px) => Math.Max(S(px), HeadH);
        public static int SCtl(int px) => Math.Max(S(px), LineH);

        // Height for n stacked 9pt lines inside a fixed-height card, plus its own padding. The growth
        // tiles clipped their third line because the card height was scaled while the three lines it
        // holds are each floored at the measured pitch — the card has to be derived from them, not
        // scaled alongside them.
        public static int SLines(int n, int padding = 0) => n * LinePitch + S(padding);

        static UiTheme()
        {
            int lineH = BaseLineH, headH = BaseHeadH;
            string info;
            try
            {
                lineH = Math.Max(TextRenderer.MeasureText("Ag", Ui).Height, AutoSizeHeight(Ui));
                headH = Math.Max(TextRenderer.MeasureText("AG", ColHeader).Height, AutoSizeHeight(ColHeader));
                info = $"measured 9pt {lineH}px, 7.5pt {headH}px";
            }
            catch (Exception e) { info = $"measure failed ({e.Message}), tuned baseline kept"; }
            // Never shrink below the tuned baseline — smaller measurements mean the measure understates
            // the renderer (the known Font.Height trap), not that the layout has room to spare.
            if (lineH < BaseLineH) lineH = BaseLineH;
            if (headH < BaseHeadH) headH = BaseHeadH;
            Scale = lineH / (float)BaseLineH;
            LineH = lineH;
            HeadH = headH;
            LinePitch = lineH + 1;
            HeadPitch = headH + 2;
            TextH = lineH - 3;
            // A NumericUpDown's usable height is its CLIENT area, and its chrome is a renderer constant we
            // must not guess. NumH used to be LineH + S(2), i.e. a 3px allowance — but Mono's UpDownBase
            // spends ~9px, so a 41px control had a 32px client area and its inner edit box could only ever
            // be 32px against a 38px line. That was ten standing "UpDownTextBox h=32 < 38" audit findings
            // across six pages (reported 2026-07-29), and no amount of stretching the inner box could fix
            // it while the box it lives in was too small. Measure the chrome, then size the control so the
            // CLIENT area fits the line.
            NumChrome = 0;
            try
            {
                using (var probe = new NumericUpDown { Font = Ui })
                {
                    probe.Height = LineH;
                    NumChrome = Math.Max(0, probe.Height - probe.ClientSize.Height);
                }
            }
            catch (Exception e) { info += $"; num chrome probe failed ({e.Message})"; }

            // InvariantCulture: this line is read back from debug.log when diagnosing layout, and a
            // comma-decimal locale wrote "scale 1,52" (project rule — pin culture on number paths).
            CalibrationInfo = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "UI metrics: {0} => pitch {1}/{2}/{3}, line {4}, head {5}, scale {6:F2}, num chrome {7}",
                info, LinePitch, HeadPitch, TextH, LineH, HeadH, Scale, NumChrome);
        }

        // Measured chrome of a NumericUpDown (border + internal padding): outer height minus client height.
        public static readonly int NumChrome;

        // Second oracle for the rendered line height: what an AutoSize label actually sizes itself to
        // (the audit history was recorded against these, not against raw MeasureText).
        private static int AutoSizeHeight(Font f)
        {
            using (var l = new Label { AutoSize = true, Font = f, Text = "Ag" })
                return Math.Max(l.Height, l.PreferredHeight);
        }

        // ---- Native controls do not follow the measured DPI, so we paint them ourselves ----
        //
        // Labels and Buttons paint through the same renderer as the rest of the app, but a ComboBox and a
        // ListBox size their rows from `Font.Height` — the 96-DPI value, ~15px, the exact trap UiLayout
        // documents for measurement. So on a 200% display every dropdown stayed a 21px box of small text
        // beside 38px labels: not just ugly, genuinely hard to hit (the reported "malý manipulační
        // prostor"). Owner-draw is the only lever that fixes BOTH the row height and the text, and this
        // codebase already reaches for it under Mono (see OwnerDrawTabs below, for the same class of
        // reason). The drawing deliberately reproduces the flat native look — surface fill, accent-weak
        // selection, Ink text — because the ask was size, not restyling.
        public static void StyleCombo(ComboBox c)
        {
            if (c == null) return;
            try
            {
                c.DrawMode = DrawMode.OwnerDrawFixed;
                c.ItemHeight = LineH;
                c.DrawItem -= ComboDraw;
                c.DrawItem += ComboDraw;
                // Mono computes the CLOSED height from Font.Height too, and unlike ItemHeight it will not
                // recompute it for us — state it, with room for the border the box draws itself.
                c.Height = LineH + S(6);
            }
            catch { }
        }

        private static void ComboDraw(object sender, DrawItemEventArgs e)
        {
            var cb = sender as ComboBox;
            if (cb == null) return;
            if (e.Index < 0 || e.Index >= cb.Items.Count) { e.DrawBackground(); return; }
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? AccentWeak : Surface))
                e.Graphics.FillRectangle(bg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, e.Bounds,
                sel ? AccentDark : Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        // Same fix for list rows, which is where the Boost priority list lost its usable space: a row
        // pitched at Font.Height fits far fewer entries than the list's own height implies, and the text
        // inside it is cut. Pitch the rows at the measured line pitch instead.
        public static void StyleList(ListBox l)
        {
            if (l == null) return;
            try
            {
                l.DrawMode = DrawMode.OwnerDrawFixed;
                l.ItemHeight = LinePitch;
                l.DrawItem -= ListDraw;
                l.DrawItem += ListDraw;
                // A ListBox eats the wheel even when it is already at its end, which stops the page it
                // sits on dead. Hooked here rather than at each call site so no styled list can be
                // forgotten — and so it is attached exactly once per list.
                ScrollPanel.ForwardWheel(l);
            }
            catch { }
        }

        private static void ListDraw(object sender, DrawItemEventArgs e)
        {
            var lb = sender as ListBox;
            if (lb == null) return;
            if (e.Index < 0 || e.Index >= lb.Items.Count) { e.DrawBackground(); return; }
            bool sel = (e.State & DrawItemState.Selected) != 0 && lb.SelectionMode != SelectionMode.None;
            using (var bg = new SolidBrush(sel ? AccentWeak : Surface))
                e.Graphics.FillRectangle(bg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, lb.GetItemText(lb.Items[e.Index]), lb.Font, e.Bounds,
                sel ? AccentDark : Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        // Number of whole rows a StyleList list shows at a given height — so a list can be asked for
        // "eight rows" instead of a pixel count that silently means three.
        public static int ListH(int rows) => rows * LinePitch + S(4);

        // NumericUpDown sizes itself from Font.Height as well (the live audit found its inner text box at
        // 32px against a 38px line). It has no DrawMode to take over, but unlike ComboBox it does honour
        // an explicit Height — so the height must be the LINE PLUS THE MEASURED CHROME. Stating LineH + a
        // guessed 3px left the client area 6px short of the line on Mono, which is what the inner edit box
        // is limited to; see the NumChrome probe. Every row helper below derives from this, so they all
        // move together.
        public static int NumH => LineH + NumChrome;

        public static void StyleNum(NumericUpDown n)
        {
            if (n == null) return;
            try
            {
                n.Height = NumH;
                // Belt and braces on the height: NumChrome comes from a probe built with UiTheme's own
                // font, and a panel may hand us a control with a different one. If this instance spends
                // more chrome than the probe did, grow it until its CLIENT area fits the line — otherwise
                // the inner box below is capped short no matter what we do to it.
                int chrome = n.Height - n.ClientSize.Height;
                if (n.ClientSize.Height < LineH && chrome > 0) n.Height = LineH + chrome;
                // …and the INNER edit box, which is a separate control sized from Font.Height on its own.
                // Setting the NumericUpDown's height alone left it at 32px against a 38px line (the audit
                // reports it as 'UpDownTextBox', which is why that name appears in the log rather than the
                // control a panel actually created). Re-applied on Resize because the control re-runs its
                // own layout and would otherwise put the short box back.
                StretchNumEdit(n);
                n.Resize -= NumResized;
                n.Resize += NumResized;
            }
            catch (Exception e) { Main.LogDebug($"StyleNum: {e.Message}"); }
        }

        private static void NumResized(object sender, EventArgs e) => StretchNumEdit(sender as NumericUpDown);

        private static void StretchNumEdit(NumericUpDown n)
        {
            if (n == null) return;
            try
            {
                foreach (Control c in n.Controls)
                {
                    if (!(c is TextBoxBase)) continue;
                    int h = n.ClientSize.Height;
                    if (c.Height < h) { c.Top = 0; c.Height = h; }
                }
            }
            catch (Exception e) { Main.LogDebug($"StretchNumEdit: {e.Message}"); }
        }

        // A row panel that CONTAINS a numeric is the tallest-child case again: NumH is the tallest thing
        // such a row holds, so the row has to be derived from it rather than scaled next to it (a tuned
        // 26px row scales to 40px around a 41px numeric — 1px of overhang, every row, silently).
        public static int SNumRow(int px) => Math.Max(S(px), NumH + S(2));

        private static Color Hex(string h) => ColorTranslator.FromHtml("#" + h);

        // Wide-layout chokepoint fix: legacy resx layouts use ABSOLUTE table columns, so a wider
        // window just grows dead space on the right. Converting Absolute column styles to Percent
        // (weighted by their designed width — TLP normalizes the sum) makes the grid redistribute,
        // and anchoring the large content controls lets them actually fill their cells. Only called
        // when WideLayout is on; narrow mode keeps the original layouts byte-for-byte.
        public static void MakeStretchy(System.Windows.Forms.Control root)
        {
            if (root is System.Windows.Forms.TableLayoutPanel tlp)
            {
                // The legacy grids are almost entirely AUTOSIZE columns (size-to-content, never
                // stretch) — convert every non-Percent column to Percent, weighted by its CURRENT
                // rendered width so the existing proportions carry over and only the surplus space
                // redistributes. Must run after layout (Shown), when GetColumnWidths is real.
                int[] widths = null;
                try { widths = tlp.GetColumnWidths(); } catch { }
                for (int i = 0; i < tlp.ColumnStyles.Count; i++)
                {
                    var cs = tlp.ColumnStyles[i];
                    if (cs.SizeType != System.Windows.Forms.SizeType.Percent)
                    {
                        float w = widths != null && i < widths.Length && widths[i] > 0
                            ? widths[i]
                            : System.Math.Max(cs.Width, 30f);
                        cs.SizeType = System.Windows.Forms.SizeType.Percent;
                        cs.Width = w;
                    }
                }
                foreach (System.Windows.Forms.Control c in tlp.Controls)
                {
                    if (c is System.Windows.Forms.ListBox || c is System.Windows.Forms.ListView ||
                        c is System.Windows.Forms.TextBox || c is System.Windows.Forms.GroupBox ||
                        c is System.Windows.Forms.FlowLayoutPanel || c is System.Windows.Forms.Panel)
                    {
                        c.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom |
                                   System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
                    }
                }
            }
            foreach (System.Windows.Forms.Control ch in root.Controls)
                MakeStretchy(ch);
        }

        private static int Clamp(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);
        private static Color Shift(Color c, int d) => Color.FromArgb(Clamp(c.R + d), Clamp(c.G + d), Clamp(c.B + d));

        // Set EXPLICIT hover/press colors. Mono's default FlatAppearance.MouseOverBackColor (Color.Empty)
        // renders a light-grey hover that never clears on mouse-leave (sticks until reload); giving it a
        // concrete value avoids that buggy path. Hover = slightly lighter, press = slightly darker.
        private static void Hover(Button b)
        {
            b.FlatAppearance.MouseOverBackColor = Shift(b.BackColor, 14);
            b.FlatAppearance.MouseDownBackColor = Shift(b.BackColor, -10);
        }

        // State styling for toggle/segment buttons: sets colors AND the explicit hover/pressed colors
        // derived from the new background. Without this, Mono's default hover tint STICKS after the
        // mouse leaves (user-reported on the sub-tab bars) — every dynamic BackColor change must go
        // through here.
        public static void ApplyState(Button b, Color bg, Color fg)
        {
            b.BackColor = bg;
            b.ForeColor = fg;
            Hover(b);
        }

        public static void StylePrimary(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = AccentDark;
            b.Font = Bold;
            b.UseVisualStyleBackColor = false;
            Hover(b);
        }

        public static void StyleFlat(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = BtnFace;
            b.ForeColor = Ink;
            b.FlatAppearance.BorderColor = BorderStrong;
            b.UseVisualStyleBackColor = false;
            Hover(b);
        }

        // Small icon/ghost button (reorder, remove, add).
        public static void StyleIcon(Button b, bool danger = false)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Surface;
            b.ForeColor = danger ? Danger : Muted;
            b.FlatAppearance.BorderColor = Border;
            b.UseVisualStyleBackColor = false;
            Hover(b);
        }

        public static void StyleGhost(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Surface;
            b.ForeColor = Accent;
            b.FlatAppearance.BorderColor = AccentDark;
            b.UseVisualStyleBackColor = false;
            Hover(b);
        }

        // Recolor ONLY the input controls (combo/text/numeric/list) of a form we built with UiTheme tokens,
        // so they don't stay light islands in dark mode. Leaves panels/accent strips/labels alone (they are
        // already themed at construction). Use on the code-built editor windows.
        public static void ThemeInputs(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is ComboBox || c is TextBoxBase || c is NumericUpDown || c is ListBox || c is ListView)
                {
                    c.BackColor = Surface;
                    c.ForeColor = Ink;
                }
                ThemeInputs(c);
            }
        }

        // Recolor an existing form (e.g. the localized settings form) to this palette WITHOUT touching
        // fonts or layout - color changes can't clip text or shift controls. Safe to run once after the
        // form is built. Containers take the ground tone; inputs take Surface; buttons go flat.
        public static void ApplyTo(Control root)
        {
            if (root is Form f) { f.BackColor = Ground; f.ForeColor = Ink; }
            foreach (Control c in root.Controls)
                Theme(c);
        }

        private static void Theme(Control c)
        {
            bool recurse = true;

            if (c is Button b)
            {
                StyleFlat(b);
                recurse = false;
            }
            else if (c is ComboBox || c is TextBoxBase || c is NumericUpDown)
            {
                c.BackColor = Surface; c.ForeColor = Ink; recurse = false;
            }
            else if (c is ListBox || c is ListView || c is DataGridView || c is TreeView)
            {
                c.BackColor = Surface; c.ForeColor = Ink; recurse = false;
            }
            else if (c is Label || c is CheckBox || c is RadioButton || c is LinkLabel)
            {
                c.BackColor = Color.Transparent; c.ForeColor = Ink; recurse = false;
            }
            else if (c is TabControl)
            {
                c.ForeColor = Ink; // leave the strip to the OS; recurse to theme the pages
            }
            else
            {
                // Panels, GroupBox, TableLayoutPanel, FlowLayoutPanel, TabPage, SplitContainer, etc.
                c.BackColor = Ground; c.ForeColor = Ink;
            }

            if (recurse)
                foreach (Control child in c.Controls)
                    Theme(child);
        }

        // Owner-draw a TabControl's tabs so the SELECTED tab is obvious in dark mode (the OS renders the tab
        // strip dark-on-dark otherwise). Selected = accent fill + white bold; others = surface + muted.
        public static void OwnerDrawTabs(TabControl tc)
        {
            try
            {
                tc.DrawMode = TabDrawMode.OwnerDrawFixed;
                tc.DrawItem -= TabDraw;
                tc.DrawItem += TabDraw;
            }
            catch { }
        }

        private static void TabDraw(object sender, DrawItemEventArgs e)
        {
            var tc = (TabControl)sender;
            if (e.Index < 0 || e.Index >= tc.TabPages.Count) return;
            bool selected = e.Index == tc.SelectedIndex;
            var g = e.Graphics;
            using (var bg = new SolidBrush(selected ? Accent : Surface))
                g.FillRectangle(bg, e.Bounds);
            var rect = e.Bounds; rect.Y += 2;
            TextRenderer.DrawText(g, tc.TabPages[e.Index].Text, selected ? Bold : Ui, rect,
                selected ? Color.White : Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
