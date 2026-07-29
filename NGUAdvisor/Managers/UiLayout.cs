using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NGUAdvisor.Managers
{
    // Layout engine + runtime overlap auditor (added after repeated hand-math overlap bugs; the
    // pre-flight is now ENFORCED, not a habit).
    //
    //  - Row(): places controls left-to-right from measured sizes — sibling overlap in a row is
    //    impossible by construction. Returns the y below the row.
    //  - Audit(): recursively checks every visible-or-not control tree for (a) intersecting sibling
    //    bounds, (b) fixed-size Labels/Buttons whose text measures wider than the control. Violations
    //    go to the log as "UI AUDIT" lines — after any UI deploy, the log must show zero of them.
    //    Panels tagged "exclusive" (alternate views sharing one area) are exempt from pairwise checks.
    public static class UiLayout
    {
        // Design canvas width for the custom panels (the old hardcoded ~664px assumption). Set ONCE
        // in the SettingsForm ctor BEFORE the panels are constructed: 920 when WideLayout, else the
        // legacy 664. Panels derive every full-width surface and column grid from this — including
        // content they rebuild at runtime — so the whole tab tracks the window width consistently.
        // (Not read from ClientSize: ctor-time reads are stale under Mono; Shown re-asserts 940.)
        public static int PanelW = 664;

        // MEASURE WITH THE RENDERER (round-3 root cause): Labels/Buttons paint via GDI
        // (TextRenderer); GDI+ Graphics.MeasureString reads NARROWER than what actually paints,
        // which cut strings mid-word with no ellipsis across the app. One engine for both.
        public static int MeasureText(string text, Font font)
            => TextRenderer.MeasureText(text ?? "", font).Width;

        public static int BtnWidth(string text) => Math.Max(UiTheme.S(42), MeasureText(text, UiTheme.Ui) + UiTheme.S(22));

        // Shared measured-ellipsis fit (the Mono blank-label law: a fixed label with overflowing
        // text renders NOTHING — every variable string goes through a Fit).
        // FIT AGAINST A SLIGHTLY NARROWER BUDGET THAN ASKED. The renderer paints wider than
        // TextRenderer measures (SystemControlBar.cs:166 documents the same shortfall), so a string that
        // measures as fitting can still be cut by the label's own edge — and that cut happens inside the
        // renderer, so NO ellipsis is added and the text just ends mid-word. Seen in the system index:
        // "…butter and the abando". The cushion is font-relative because the shortfall scales with the
        // glyphs, and it only ever costs a character or two of a string that was being truncated anyway.
        // Deliberately here and NOT in MeasureText: this shortens text, while widening the measurement
        // itself would move every AutoSize control in the app.
        public static string FitText(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int budget = width - Math.Max(2, font.Height / 4);
            if (budget <= 0) budget = width;
            if (MeasureText(text, font) <= budget) return text;
            while (text.Length > 1 && MeasureText(text + "…", font) > budget)
                text = text.Substring(0, text.Length - 1);
            return text + "…";
        }

        // TRUNCATION MUST NEVER LOSE THE TEXT. Where a value genuinely cannot be given more room, it gets
        // ellipsized — and then the full string exists nowhere the user can reach it, which is how
        // "UNLOCK: 100 SEE…" and "WAIT — 1e15 i…" became dead ends. FitInto is the one way to write a
        // fitted value: it measures with the font that PAINTS, sets the shortened text, and hangs the
        // complete string on the control as a tooltip — but only when it actually had to shorten it, so
        // hovering never shows a pointless tooltip repeating what is already on screen.
        //
        // ONE shared ToolTip for the whole app: WinForms tooltips are per-instance windows, and a
        // per-label instance would burn GDI handles in a process that already dies of GDI exhaustion when
        // controls leak (see DisposeChildren). It lives as long as the injected assembly, by design.
        private static readonly ToolTip _tips = new ToolTip();

        public static void FitInto(Control c, string full, Font font = null, int width = 0)
        {
            if (c == null) return;
            full = full ?? "";
            var f = font ?? c.Font;
            int w = width > 0 ? width : c.Width - UiTheme.S(4);
            string shown = FitText(full, f, w);
            if (c.Text != shown) c.Text = shown;
            Tip(c, shown == full ? null : full);
        }

        // Attach or clear a tooltip. Passing null clears it, so a value that stops being truncated does
        // not keep a stale tooltip from when it was.
        public static void Tip(Control c, string text)
        {
            if (c == null) return;
            try { _tips.SetToolTip(c, text ?? ""); }
            catch { }
        }

        // NO-ELLIPSIS rule (user): measure first — if the text fits, single line; if not, the label
        // GROWS vertically and the text word-wraps up to maxLines. Only past maxLines does the last
        // line ellipsize (and anything that long also lands in LOGS in full). Returns the label's
        // new bottom edge so callers can reflow the rows below it.
        public static int FitOrGrow(Label l, string text, int maxLines = 2)
        {
            text = text ?? "";
            int width = l.Width - UiTheme.S(4);
            if (MeasureText(text, l.Font) <= width)
            {
                l.Height = UiTheme.TextH;
                l.Text = text;
                return l.Bottom;
            }
            var lines = WrapLines(text, l.Font, width, maxLines);
            l.Height = lines.Count * UiTheme.LinePitch - UiTheme.S(4);
            string wrapped = string.Join("\n", lines.ToArray());
            l.Text = wrapped;
            // Growing is the preferred answer, but past maxLines the last line still ellipsizes — and at
            // that point the same rule as FitInto applies: whatever was cut has to stay reachable.
            Tip(l, Truncated(wrapped, text) ? text : null);
            return l.Bottom;
        }

        // Wrap into a fixed-height multi-line label (chip sub-captions): the label keeps its
        // pre-reserved two-line height, only the text wraps.
        public static string WrapText(string text, Font font, int width, int maxLines)
            => string.Join("\n", WrapLines(text ?? "", font, width, maxLines).ToArray());

        // Control-taking form of WrapText, so a wrapped-and-then-ellipsized caption carries its full text
        // too. The two-line sub-captions on the Gold and Pit chips are the reason this exists: they were
        // the last place a value could still be silently cut.
        public static void WrapInto(Control c, string full, int maxLines = 2, Font font = null, int width = 0)
        {
            if (c == null) return;
            full = full ?? "";
            var f = font ?? c.Font;
            int w = width > 0 ? width : c.Width - UiTheme.S(4);
            string wrapped = WrapText(full, f, w, maxLines);
            if (c.Text != wrapped) c.Text = wrapped;
            Tip(c, Truncated(wrapped, full) ? full : null);
        }

        // Wrapping inserts newlines, so "did it lose anything?" is not a string comparison — it is whether
        // an ellipsis got added that the source did not have.
        private static bool Truncated(string shown, string full)
            => shown.EndsWith("…", StringComparison.Ordinal) && !full.EndsWith("…", StringComparison.Ordinal);

        private static List<string> WrapLines(string text, Font font, int width, int maxLines)
        {
            var lines = new List<string>();
            var words = text.Split(' ');
            string cur = "";
            for (int i = 0; i < words.Length; i++)
            {
                string cand = cur.Length == 0 ? words[i] : cur + " " + words[i];
                if (cur.Length == 0 || MeasureText(cand, font) <= width)
                {
                    cur = cand;
                    continue;
                }
                lines.Add(cur);
                cur = words[i];
                if (lines.Count == maxLines - 1)
                {
                    // Last permitted line takes the whole remainder, ellipsized only if needed.
                    var rest = new System.Text.StringBuilder(cur);
                    for (int j = i + 1; j < words.Length; j++) rest.Append(' ').Append(words[j]);
                    lines.Add(FitText(rest.ToString(), font, width));
                    return lines;
                }
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }

        // Place controls left-to-right starting at (x, y), gap px apart. Sizes must be set BEFORE the
        // call (buttons via BtnWidth; AutoSize labels are measured directly). Returns bottom edge.
        public static int Row(int x, int y, int gap, params Control[] controls)
        {
            int cx = x, maxH = 0;
            foreach (var c in controls)
            {
                if (c == null) continue;
                int w = c.AutoSize && c is Label lb ? MeasureText(lb.Text, lb.Font) : c.Width;
                // Vertically center small controls on the row's first control baseline.
                c.Location = new Point(cx, y + (c is Label ? UiTheme.S(4) : 0));
                cx += w + gap;
                maxH = Math.Max(maxH, c.Height + (c is Label ? UiTheme.S(4) : 0));
            }
            return y + Math.Max(maxH, UiTheme.S(24));
        }

        // Left-to-right with wrapping at maxRight (chip strips): returns the y below the last row.
        public static int WrapRow(int x, int y, int gap, int maxRight, int rowPitch, IEnumerable<Control> controls)
        {
            int cx = x, cy = y;
            foreach (var c in controls)
            {
                if (c == null) continue;
                if (cx + c.Width > maxRight && cx > x)
                {
                    cx = x;
                    cy += rowPitch;
                }
                c.Location = new Point(cx, cy);
                cx += c.Width + gap;
            }
            return cy + rowPitch;
        }

        // THE ONLY SAFE WAY TO EMPTY A CONTAINER on this Mono: Controls.Clear() removes without
        // disposing, and the orphans keep their native handles until the process GDI budget runs out
        // (the form dies with GDI+ OutOfMemory and will not reopen). Remove-then-Dispose, back to
        // front. Idempotent — a container half-built by a failed rebuild is cleaned just the same.
        public static void DisposeChildren(Control host)
        {
            if (host == null) return;
            while (host.Controls.Count > 0)
            {
                var c = host.Controls[host.Controls.Count - 1];
                host.Controls.Remove(c);
                c.Dispose();
            }
        }

        public static void Audit(Control root, string context)
        {
            try
            {
                int issues = AuditNode(root, context);
                Main.LogDebug(issues == 0
                    ? $"UI AUDIT [{context}]: clean"
                    : $"UI AUDIT [{context}]: {issues} ISSUE(S) — see lines above");
            }
            catch (Exception e) { Main.LogDebug($"UI AUDIT [{context}] failed: {e.Message}"); }
        }

        private static int AuditNode(Control node, string context)
        {
            int issues = 0;
            var kids = node.Controls.Cast<Control>().ToList();

            for (int i = 0; i < kids.Count; i++)
            {
                var a = kids[i];

                // TEXT FIT IS VISIBILITY-INDEPENDENT, AND THAT IS THE WHOLE POINT. A box too short for
                // its own font is too short whether or not it is on screen right now, and the panels
                // that build their content hidden and reveal it on refresh (the Yggdrasil orchard is
                // built with every tile Visible=false) were therefore the ONLY panels the auditor never
                // checked — which is exactly where the high-DPI clipping was reported and where this
                // audit said "clean". So measure and recurse first, unconditionally.
                issues += TextFit(a, context);
                issues += AuditNode(a, context);

                // Geometry checks stay visible-only: hidden controls do not paint, cannot visually
                // overlap, and alternate views deliberately share coordinates.
                if (!a.Visible) continue;

                for (int j = i + 1; j < kids.Count; j++)
                {
                    var b = kids[j];
                    if (!b.Visible) continue;
                    if (Equals(a.Tag, "exclusive") && Equals(b.Tag, "exclusive")) continue;
                    // ONE authoritative overlap path (see Overlaps): canonical EffectiveBounds + the 1px
                    // tolerance — control chrome may touch; only real glyph collisions flag.
                    if (Overlaps(a, b))
                    {
                        Main.LogDebug($"UI AUDIT [{context}]: OVERLAP '{Desc(a)}' {EffectiveBounds(a)} x '{Desc(b)}' {EffectiveBounds(b)}");
                        issues++;
                    }
                }

                // Content must stay inside its parent's client area (an AutoSize label past the edge
                // CLIPS silently — the Adventure footer bug).
                var eb = EffectiveBounds(a);
                if (node.ClientSize.Width > 0 && eb.Right > node.ClientSize.Width && !(node is Form))
                {
                    Main.LogDebug($"UI AUDIT [{context}]: PAST PARENT EDGE '{Desc(a)}' right={eb.Right} parent={node.ClientSize.Width}");
                    issues++;
                }

                // Same rule downwards, but ONLY where there is no scrollbar to reach the overflow with.
                // A section canvas is AutoScroll and its content is MEANT to run past the fold; a fixed
                // card is not, and content past its bottom edge is simply cut off — which is how the
                // growth tiles lost their third line at high DPI, silently, with the audit clean.
                bool scrolls = node is ScrollableControl sc && sc.AutoScroll;
                if (!scrolls && !(node is Form) && node.ClientSize.Height > 0
                    && eb.Bottom > node.ClientSize.Height)
                {
                    Main.LogDebug($"UI AUDIT [{context}]: PAST PARENT BOTTOM '{Desc(a)}' bottom={eb.Bottom} parent={node.ClientSize.Height}");
                    issues++;
                }
            }
            return issues;
        }

        // DPI truth (learned from live audits): the game's Mono renders 9pt text ~25px tall — the
        // RENDERED AutoSize height is the real one; Font.Height (96-DPI based, ~15px) UNDERSTATES it.
        // Glyphs occupy roughly the rendered height minus ~4px of box padding.
        private static Rectangle EffectiveBounds(Control c)
        {
            int w = c.Width, h = c.Height;
            if (c is Label l && l.AutoSize)
            {
                w = Math.Max(w, MeasureText(l.Text, l.Font));
                h = Math.Max(l.Font.Height, h - UiTheme.S(4));
            }
            else if (c is CheckBox cb && cb.AutoSize)
            {
                w = Math.Max(w, MeasureText(cb.Text, cb.Font) + UiTheme.S(20));
                h = Math.Max(cb.Font.Height, h - UiTheme.S(4));
            }
            return new Rectangle(c.Left, c.Top, w, h);
        }

        // THE overlap authority for the whole app — Audit calls it, and so does anything else that needs
        // "do these two controls collide?" (BasicSettingsPanel's filtered-layout audit). The geometry is
        // NOT re-stated here: it is exactly EffectiveBounds (rendered height minus glyph padding) shrunk by
        // the same 1px tolerance Audit has always used, so a 1px chrome touch between AutoSize controls on
        // a tight row pitch does not read as a collision while a real glyph overlap does. Visibility and the
        // "exclusive" tag stay the caller's business, exactly as before.
        public static bool Overlaps(Control first, Control second)
        {
            if (first == null || second == null) return false;
            var a = EffectiveBounds(first);
            var b = EffectiveBounds(second);
            a.Inflate(-1, -1);
            b.Inflate(-1, -1);
            return a.Width > 0 && a.Height > 0 && b.Width > 0 && b.Height > 0 && a.IntersectsWith(b);
        }

        // HORIZONTAL OVERFLOW REPORT. README states the app scrolls vertically only — a horizontal
        // scrollbar means content is wider than the section that hosts it, and the scrollbar itself is
        // the only visible symptom (the offending control is off-screen to the right, so no amount of
        // looking at the window finds it). This names the widest child and by how much it overruns, so
        // the cause is in the log rather than in a guess.
        // RECURSES, and that is the fix rather than a refinement: this walked only the section's DIRECT
        // children, while every panel in the app nests its content (Boosts is section -> _boostPage ->
        // _manualView -> the labels). An overflowing label inside a nested panel therefore could not be
        // seen by this check, so the one rule that catches horizontal clipping reported clean on the pages
        // where it happens. Measured through EffectiveBounds, like every other rule here, because an
        // AutoSize label's Width understates the Mono render.
        public static void AuditWidth(Control host, string context)
        {
            try
            {
                if (host == null || host.ClientSize.Width <= 0) return;
                int limit = host.ClientSize.Width;
                Control widest = null;
                int right = 0;
                WidestRight(host, 0, ref widest, ref right);
                if (widest != null && right > limit)
                    Main.LogDebug($"UI AUDIT [{context}]: WIDER THAN VIEWPORT by {right - limit}px — '{Desc(widest)}' right={right} viewport={limit}");
            }
            catch (Exception e) { Main.LogDebug($"UI AUDIT [{context}] width check failed: {e.Message}"); }
        }

        // Child bounds are parent-relative, so the parent's Left accumulates on the way down — without it
        // a deeply nested control reports a right edge far short of where it actually paints.
        private static void WidestRight(Control node, int offsetX, ref Control widest, ref int right)
        {
            foreach (Control c in node.Controls)
            {
                if (!c.Visible) continue;
                int r = offsetX + EffectiveBounds(c).Right;
                if (r > right) { right = r; widest = c; }
                WidestRight(c, offsetX + c.Left, ref widest, ref right);
            }
        }

        // Once-per-context audit for lazily shown views (called from page/segment switchers).
        private static readonly HashSet<string> _audited = new HashSet<string>();

        public static void AuditOnce(Control root, string context)
        {
            if (root == null || !_audited.Add(context)) return;
            Audit(root, context);
        }

        private static int TextFit(Control c, string context)
        {
            string text = c.Text;
            if (string.IsNullOrEmpty(text) || text.Contains("…")) return 0;
            int needed = -1;
            if (c is Button btn) needed = MeasureText(text, btn.Font) + UiTheme.S(14);
            else if (c is Label l && !l.AutoSize) needed = MeasureText(text, l.Font);
            if (needed > 0 && needed > c.Width)
            {
                Main.LogDebug($"UI AUDIT [{context}]: TEXT CLIPPED '{Desc(c)}' needs {needed}px has {c.Width}px");
                return 1;
            }
            // Vertical clip: fixed-height single-line labels need >= UiTheme.TextH under this Mono's
            // ~25px 9pt rendering (16px boxes cut descenders).
            if (c is Label fl && !fl.AutoSize && fl.Height < UiTheme.TextH && fl.Font.Size >= 8.5f)
            {
                Main.LogDebug($"UI AUDIT [{context}]: TEXT MAY CLIP VERTICALLY '{Desc(c)}' h={fl.Height} < {UiTheme.TextH}");
                return 1;
            }
            // Same clip, one class wider: a Button/ComboBox/TextBox/NumericUpDown paints its text inside
            // its own chrome, so it needs the FULL measured line box — below that the glyphs are cut and
            // what you notice first is that the caption looks vertically off-centre, not that it is short.
            // (Under-height buttons were the visible half of the high-DPI report; UiTheme.SCtl is the fix.)
            // (The fix is UiTheme.StyleCombo / StyleNum / SCtl depending on the control — see ui-infra.md.
            // A NumericUpDown reports through its inner UpDownTextBox, which is why that name shows up in
            // the log rather than the control the panel actually created.)
            // EXCEPTION, AND WHY IT IS NOT A COVER-UP: a NumericUpDown's inner edit box is a single-line
            // TextBox, and neither WinForms nor Mono lets one take a height — the font decides it. Proven,
            // not assumed: UiTheme's startup probe stretches a box on a control it owns and reports the
            // result as `num inner` in the metrics line; it comes back 32 against a 38px line. Four
            // releases were spent sizing that box (1.2.7, 1.2.12, 1.2.13, 1.2.15) because this rule kept
            // reporting it. The box is not clipping its digits — it renders them smaller than the line box
            // — so the honest fix is to hold the OUTER control to the rule, where height is settable and
            // where the click target and the spin arrows actually live, and to keep the inner box centred
            // in it (UiTheme.StretchNumEdit). If a number field is hard to hit, that is UiTheme.NumH.
            if (c is TextBoxBase && c.Parent is NumericUpDown numParent)
            {
                if (numParent.Height < UiTheme.LineH)
                {
                    Main.LogDebug($"UI AUDIT [{context}]: CONTROL TOO SHORT FOR TEXT '{Desc(numParent)}' h={numParent.Height} < {UiTheme.LineH}");
                    return 1;
                }
                return 0;
            }
            if ((c is Button || c is ComboBox || c is TextBoxBase || c is NumericUpDown)
                && c.Height > 0 && c.Height < UiTheme.LineH && c.Font.Size >= 8.5f)
            {
                Main.LogDebug($"UI AUDIT [{context}]: CONTROL TOO SHORT FOR TEXT '{Desc(c)}' h={c.Height} < {UiTheme.LineH}");
                return 1;
            }
            return 0;
        }

        private static string Desc(Control c)
        {
            var t = c.GetType().Name;
            var txt = c.Text;
            if (!string.IsNullOrEmpty(txt) && txt.Length > 24) txt = txt.Substring(0, 24) + "…";
            return string.IsNullOrEmpty(txt) ? t : $"{t}:{txt}";
        }
    }
}
