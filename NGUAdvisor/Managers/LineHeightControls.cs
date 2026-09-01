using System;
using System.Windows.Forms;

namespace NGUAdvisor.Managers
{
    // A ComboBox that actually KEEPS the height it is given, and a NumericUpDown that records why it
    // cannot be made to.
    //
    // UiTheme.StyleCombo/StyleNum set a height and Mono discards it. This is not a race to be won by
    // hooking the right event: an instrumented pass over the live form assigned the wanted height three
    // times to every dropdown and spinner on it and logged the outcome — 70 controls, not one moved
    // (2026-09-01). Both re-derive from the 96-DPI `Font.Height`, the same trap UiLayout documents for
    // measurement, so the boxes sit ~2px under the measured line at the tuning baseline and around a
    // third of it on a 200% display, where this was first reported as dropdowns being hard to hit.
    //
    // Same reasoning as ScaledCheckBox: where Mono owns a metric outright, a subclass is the honest fix
    // and a styling call is not. Behaviour is untouched; only the height floor is ours. Max, not assign,
    // so a panel that deliberately wants a taller control still gets one.

    // ComboBox — Mono's SnapHeight throws the height away for every style except Simple:
    //
    //     private int SnapHeight(int height) {
    //         if (DropDownStyle == ComboBoxStyle.Simple && height > PreferredHeight) { integral }
    //         else { height = PreferredHeight; }          // PreferredHeight => Font.Height + 8
    //         return height;
    //     }
    //
    // — but ComboBox.SetBoundsCore only CALLS SnapHeight when the write specifies Height (or the box is
    // anchored top+bottom, or docked). A Location-only write passes the height straight through.
    //
    // That gap is the whole difference between the dropdowns that came out right and the ones that did
    // not, and it explains a split that looked random: the boxes UiLayout.Row repositions AFTER styling
    // got the floor in for free on Row's Location write, while the boxes carrying their Location in their
    // own initializer never got a second write, so StyleCombo's Height write had the last word and was
    // snapped back. Two dropdowns in BoostsPanel ended at 31 and the third at 24 for no other reason.
    //
    // So the floor is re-issued deliberately through a Location write rather than depending on the order
    // a panel happens to lay its controls out. It has to call SetBoundsCore directly: routed through
    // SetBounds it would be dropped by SetBoundsInternal, which returns early when the position is
    // unchanged and the write does not specify Height — exactly this case.
    public class LineComboBox : ComboBox
    {
        private bool _flooring;

        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            base.SetBoundsCore(x, y, width, Math.Max(height, UiTheme.ComboH), specified);
            if (_flooring || Height >= UiTheme.ComboH) return;
            _flooring = true;
            try { SetBoundsCore(x, y, width, UiTheme.ComboH, BoundsSpecified.Location); }
            catch (Exception e) { Main.LogDebug($"LineComboBox floor: {e.Message}"); }
            finally { _flooring = false; }
        }
    }

    // NumericUpDown — NOT FIXABLE, and this type exists to say so at the call site instead of leaving the
    // next person to rediscover it. UpDownBase overrides the INTERNAL entry point, one level below the
    // protected one a subclass can reach:
    //
    //     internal override void SetBoundsCoreInternal(int x, int y, int width, int height, BoundsSpecified specified)
    //     {
    //         base.SetBoundsCoreInternal(x, y, width, Math.Min(width, PreferredHeight), specified);
    //     }
    //
    // The requested height is discarded unconditionally — no `specified` gate to slip through, unlike
    // ComboBox above — and `internal override` cannot be reached from outside System.Windows.Forms. A
    // NumericUpDown under this Mono is `Font.Height + 7` and nothing else; the only lever left is the
    // FONT. The floor below is honest about being a no-op today: it costs nothing, and it is what holds
    // if the control is ever swapped for one whose height is settable.
    //
    // Standing consequence, also in ui-infra.md: the audit reports these and no code change clears them.
    // Do not "fix" it by assigning heights — that has now been measured, after four releases of trying.
    public class LineNumericUpDown : NumericUpDown
    {
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            base.SetBoundsCore(x, y, width, Math.Max(height, UiTheme.NumH), specified);
        }
    }
}
