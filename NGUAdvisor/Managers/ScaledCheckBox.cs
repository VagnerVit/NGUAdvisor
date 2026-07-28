using System;
using System.Drawing;
using System.Windows.Forms;

namespace NGUAdvisor.Managers
{
    // A CheckBox whose BOX scales with the measured text, because WinForms' does not.
    //
    // The check glyph is a fixed ~13px no matter the font or the screen DPI — it is drawn from a system
    // metric, not from the control's font. On a 200%-scaling display that leaves a 13px box next to 38px
    // text: the reported "checkboxy jsou malé". There is no property for it, so the only fix is to paint
    // the control, which is why this subclass exists rather than a UiTheme.Style* helper like the ones for
    // ComboBox/ListBox (those expose DrawMode; CheckBox does not).
    //
    // The look is deliberately the app's own flat language (UiTheme: flat fills, 1px borders, no gradients
    // — all Mono-safe) rather than an imitation of the native themed glyph, which cannot be scaled anyway.
    // Behaviour is untouched: this is still a CheckBox, so Checked/CheckedChanged, keyboard toggling, tab
    // order and every existing handler work exactly as before.
    public class ScaledCheckBox : CheckBox
    {
        // Box side and the gap to the caption, both derived from the measured line so the control tracks
        // whatever the renderer is actually doing.
        private static int BoxSide => Math.Max(13, UiTheme.LineH / 2);
        private static int Gap => UiTheme.S(6);

        public ScaledCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            // The caller may still set AutoSize; GetPreferredSize below answers for it correctly. The
            // click target is the WHOLE control, so a full-line height is also a full-line hit area.
            Font = UiTheme.Ui;
            ForeColor = UiTheme.Ink;
        }

        public override Size GetPreferredSize(Size proposedSize)
            => new Size(BoxSide + Gap + UiLayout.MeasureText(Text ?? "", Font) + UiTheme.S(4),
                        Math.Max(UiTheme.LineH, BoxSide + UiTheme.S(2)));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // UserPaint means nothing else fills the background — and these sit on themed panels, so the
            // parent's colour is the correct one, not SystemColors.Control.
            using (var bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);

            int side = BoxSide;
            var box = new Rectangle(0, (ClientSize.Height - side) / 2, side, side);

            using (var fill = new SolidBrush(Enabled ? UiTheme.Surface : UiTheme.Ground))
                g.FillRectangle(fill, box);
            using (var pen = new Pen(Enabled ? UiTheme.BorderStrong : UiTheme.Border))
                g.DrawRectangle(pen, box);

            if (Checked)
            {
                // The tick is drawn as two strokes rather than a glyph: a font tick would be back to
                // sizing from Font.Height, which is the whole problem this class exists to avoid.
                using (var pen = new Pen(Enabled ? UiTheme.Accent : UiTheme.Faint, Math.Max(2f, side / 7f)))
                {
                    float l = box.Left + side * 0.22f, m = box.Left + side * 0.42f, r = box.Left + side * 0.78f;
                    float top = box.Top + side * 0.28f, mid = box.Top + side * 0.52f, low = box.Top + side * 0.72f;
                    g.DrawLine(pen, l, mid, m, low);
                    g.DrawLine(pen, m, low, r, top);
                }
            }

            var textRect = new Rectangle(box.Right + Gap, 0,
                Math.Max(0, ClientSize.Width - box.Right - Gap), ClientSize.Height);
            TextRenderer.DrawText(g, Text ?? "", Font, textRect, Enabled ? ForeColor : UiTheme.Faint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (Focused)
                ControlPaint.DrawFocusRectangle(g, textRect);
        }
    }
}
