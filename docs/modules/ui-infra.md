# UI infrastructure: SettingsIndex · Activity/ActivityRibbon · Destinations · SystemCatalog · PriorityCatalog · UiTheme/UiLayout · SystemControlBar · LogTail · PresetInstaller

## SettingsIndex (`Managers/SettingsIndex.cs`)

What exists in Settings, who owns it, and how to find it by any name it has ever had. Pure data +
matching — **no controls, no delegates, no SavedSettings**, so matching never depends on live state
(a search for "Blood" finds Blood whether blood automation is on, off, or mid-rebirth). This
independence is also why it's testable outside the game.

`IndexEntry`: stable `Id` (never the display title), `Kind`
(**System** = state lives in a panel, Settings points at the owner; **Setting** = Settings IS the
canonical owner and stays editable; **Reference** = a searchable concept owned elsewhere),
`Title`, `Blurb`, `Destination`, `Group`, `Fields`, `Aliases`.

**TWO MATCHING DOMAINS, deliberately not one haystack**: human text (`Human` = title + blurb +
group + aliases) matches by SUBSTRING; raw field identifiers (`Ids`) match EXACTLY. Mixing them let
the developer escape hatch leak into ordinary search — "managed" hit Diggers because the field name
`ManageDiggers` contains it. Identifiers are addresses, not words.

Two counting rules that are NOT the same: **one entry per SYSTEM** (Blood matching five terms must
produce ONE row — terms are how you find an entry, not entries themselves) and **one entry per
SETTINGS ROW** (a settings row has its own control, so it can't be aggregated).

**A BAND is one row too.** `ALWAYS EQUIP` (`PinnedGearIds`) is a `Set` entry in its own group even
though the control it points at is a whole `Panel` (list + Paste/Copy/Clear/Undo + status). The panel
registers the CONTAINER, not the pieces — one surface, one control — so filtering moves the band whole
and there is no way for the list to appear without its buttons, or a button without the list it edits.
The band's own layout rules are in ui-panels.md §ALWAYS EQUIP.

## Activity + ActivityRibbon (`Activity.cs`, `ActivityRibbon.cs`)

One-slot model of "what happened because of the thing I just clicked" (LOGS remains the history).
A newer outcome always replaces an older one — including success replacing failure, because the
user just acted again.

**The record owns WHEN, not the paint**: `ReportedAt` (UTC) is set at report time, so an outcome
reported while another section was open doesn't get a fresh lifetime when it finally paints.
Lifetimes: Completed 8 s, Queued 12 s, Warning 30 s, **Failure never expires** (stands until
dismissed or replaced). `Seq` is the ribbon's change gate. No locks by design — every caller is a
UI-thread click handler and the only reader is `SettingsForm.UpdateStatus` on the Unity main thread.

## Destinations / SystemCatalog / PriorityCatalog

- `Destinations` — string constants naming panels/pages; the indirection that lets SettingsIndex
  point at an owner without referencing UI types.
- `SystemCatalog` — the systems list behind the rail/category strip and the Systems index page.
- `PriorityCatalog` — the allocation-token vocabulary (names + index ranges) the Profile Editor
  offers and `ProfileValidator` checks against. Keep in sync with `AllocationProfiles/Breakpoints`
  and README's Allocation section.

## UiTheme / UiLayout / SystemControlBar

Shared WinForms styling (colors, fonts, chip/card drawing) and layout helpers so panels look like
one app; `SystemControlBar` is the reusable per-system header (toggles + advisor state). Panels
should compose these rather than styling controls inline. Note the **Overview page scrolls
vertically only** — no horizontal scrolling anywhere (a documented expected behavior in README).

### DPI calibration — every hand-placed pixel goes through `UiTheme.S()`

The whole UI is hand-placed in pixels, and the game's Mono renders text at the **real screen DPI**
while those pixels stay fixed. The layout was tuned on a display where 9pt renders ~25px tall; on a
200%-scaling display it renders 38px, and every heading clipped, every stacked line overlapped, and
the status strip fell out of the window (v1.2.3 bug report).

So the metrics are **measured at startup**, not assumed. `UiTheme`'s static ctor measures the
rendered height of 9pt and 7.5pt text (two oracles: `TextRenderer.MeasureText` and a live AutoSize
label's height, whichever is larger) and derives `LineH`, `HeadH`, `LinePitch`, `HeadPitch`, `TextH`
and `Scale` from it. **Rules:**

- Any hand-placed dimension is written `UiTheme.S(n)`, where `n` is the value tuned at the 25px
  baseline. At that baseline `Scale == 1.0` and `S(n) == n`, so the original machine renders
  byte-identically — that is what makes the change safe to make everywhere at once.
- Calibration **never scales below the tuned baseline**. A renderer that under-reports (the known
  `Font.Height` 96-DPI trap) keeps the old layout rather than shrinking it.
- The measurement is logged to `debug.log` as the first `UI metrics:` line. Read it before
  diagnosing any layout report — it says which branch was taken.

### Heights that hold text are NOT freely scalable

The subtle half of the same bug, and the one that survived the first fix. The tuned layout sized
single-line labels at 18-22px for text rendering 25px — descenders were **already** being shaved by
1-3px, invisibly. Scaling those heights by the same ratio preserves the shortfall and multiplies it:
an 18px box becomes 27px for 38px text, so "g" and "y" lose 8px and clipping is the first thing you
see. Under-height buttons show it differently — the caption looks vertically off-centre rather than
short.

A control that holds text therefore takes its height through a **floor**, never raw `S()`. Note that
unlike `S()`, these are **not** no-ops at the tuning baseline: `SText(18)` is 22 there, because an
18px box was too short for 25px text on that display too — the shortfall was simply invisible. The
floors are a correctness fix at every scale, so a baseline machine sees small, deliberate growth in
exactly the boxes that were shaving their own descenders.

| Helper | For |
|---|---|
| `SText(n)` | non-AutoSize `Label`, 9pt (`Ui`/`Bold`) — floors at `TextH` |
| `SHead(n)` | non-AutoSize `Label`, 7.5pt (`ColHeader`/`Chip`) — floors at `HeadH` |
| `SCtl(n)` | `Button` / `ComboBox` / `TextBox` / `NumericUpDown` — floors at the full `LineH`, because these paint text inside their own chrome |
| `SLines(n, pad)` | a fixed-height card holding `n` stacked lines — **derive** the card from the lines it holds, never scale it alongside them (this is what clipped the growth tiles' third line) |

Corollary: flooring a child height can push it past a fixed-height parent, which clips **silently** —
a card has no scrollbar to reach the overflow with. Raising a child means re-deriving its container.

### Native controls do not follow the measured DPI — we paint them

Labels and Buttons render at the real screen DPI, but a **ComboBox, ListBox and NumericUpDown size
themselves from `Font.Height`** — the 96-DPI value, ~15px, the same trap `UiLayout` documents for
measurement. The live audit on a 200% display found every dropdown at `h=21` and every
NumericUpDown's inner text box at `h=32` against a 38px line. The result was not merely ugly: the
controls were genuinely hard to hit, and a list showed a third of the rows its height implied (the
Boost priority list was specified as 90px and displayed **three** entries).

There is no property for it, so these are owner-drawn — the same lever `OwnerDrawTabs` already uses
for a Mono problem of the same shape. The drawing reproduces the flat native look (surface fill,
accent-weak selection, Ink text) because the ask was size, not restyling.

| Helper | Applies to | What it does |
|---|---|---|
| `StyleCombo(c)` | `ComboBox` | `OwnerDrawFixed` + `ItemHeight = LineH`, and states the closed `Height` (Mono will not recompute that one) |
| `StyleList(l)` | `ListBox` | `OwnerDrawFixed` + `ItemHeight = LinePitch`; honours `SelectionMode.None` |
| `StyleNum(n)` | `NumericUpDown` | no `DrawMode` exists, but it *does* honour an explicit `Height` — so state it |
| `ListH(rows)` | list heights | **specify lists in ROWS.** A pixel height silently means a different row count at every scale |
| `OwnerDrawTabs(tc)` | `TabControl` | owner-draws the strip AND states `SizeMode.Fixed` + `ItemSize`: height from `SCtl`, width from the widest caption measured in **`Bold`** (the selected tab draws bold, so `Ui` would ellipsize whichever tab is active). **Call it after the pages exist** — the width is derived from them. A TabControl sizes its own strip from `Font.Height` like the controls above, so the captions paint at the real DPI into a band built for a third of it and the strip shows a horizontal slice of its own labels |

`ScaledCheckBox` (`Managers/ScaledCheckBox.cs`) exists for the same reason and is the one case that
needs a subclass: `CheckBox` exposes no `DrawMode`, and its glyph is a fixed ~13px system metric that
ignores both font and DPI. It is still a `CheckBox`, so `Checked`, `CheckedChanged`, keyboard toggling
and tab order are unchanged; only the painting is ours, and the whole control is the click target.

### Truncation must never lose the text

Two rules govern text that does not fit, and they are different:

- **Prefer growing.** `FitOrGrow` implements the standing no-ellipsis rule: measure, and if the string
  does not fit on one line, the label grows and word-wraps rather than ellipsizing. Use it for prose.
- **When a value genuinely cannot be given more room**, write it through `UiLayout.FitInto(control,
  full)`. It measures with the font that *paints* (the control's own `Font`), sets the shortened text,
  and hangs the complete string on the control as a tooltip — but only when it actually shortened it,
  so hovering never shows a tooltip repeating what is already on screen. Without this the full value
  existed nowhere the user could reach: `UNLOCK: 100 SEE…` was a dead end.

`FitInto` defaults both the font and the width to the control's own, which is the point: a measure/paint
font mismatch is a real bug this codebase has already shipped (the Yggdrasil tiles measured in 7.5pt and
painted in 9pt, so the fit believed more fitted than did and Mono cut the rest with no ellipsis at all).
Pass an explicit font or width only when the text is painted into a *different* control than the one
measured. There is ONE shared `ToolTip` for the app — tooltips are per-instance windows, and a
per-label instance would burn GDI handles in a process that already dies of GDI exhaustion when
controls leak.

A fixed-width label that is never fitted at all is the worst case of the three: it is cut with no
ellipsis, so nothing on screen even hints that there is more.

### Scrolling: `ScrollPanel`, and ONE scroll owner per screen

Section canvases and sub-pages are `ScrollPanel` (`Managers/ScrollPanel.cs`), not `Panel`. **It does not
use `AutoScroll`** — it owns a `VScrollBar` beside a content panel it offsets itself.

That is the only way to get a scrollbar sized like the rest of the UI: `AutoScroll` draws the *OS*
scrollbar, whose width is a system metric with no property behind it, so on a scaled display it stays a
thin sliver beside 38px text — as hard to grab as it looks. A `VScrollBar`'s `Width` *is* settable, so the
bar is derived from the measured line like every other dimension.

**Children go into `Content`, never into the ScrollPanel** — the panel itself holds the scrollbar, so
anything added directly to it sits beside the bar, outside the surface that moves. `SettingsForm.Host()`
is the one indirection that keeps `Place()` call sites unaware of this. Both docked children (Settings
stacks two `Dock.Top` panels) and absolutely-positioned ones work.

`Scrollable = false` is for a host whose *sub-pages* scroll (Advisors, Systems): its content is given the
viewport exactly, because sizing content to a `Dock.Fill` child is circular — the child's bottom would
define the content height which defines the child's bottom — and collapses to nothing.

The container also watches its children's `SizeChanged`/`VisibleChanged`, because a card that grows when a
plan wraps, or a settings panel that shrinks to its search results, changes the scroll range and nothing
else would notice.

Abandoning `AutoScroll` fixed three faults it had besides the width, on a scaled display with a precision
touchpad:

- **Tearing.** Scrolling blits the client area and invalidates only the newly-exposed strip, so children
  painting outside the blit — owner-drawn lists especially — left their old pixels behind as streaks.
  `ScrollPanel` is double-buffered; `Panel.DoubleBuffered` is protected, which is the only reason the
  subclass has to exist.
- **Touchpad deltas.** WinForms converts a wheel *notch* into scrolling. A precision touchpad sends a
  stream of deltas far smaller than a notch, each rounding to zero — so the page refused to move, then
  jumped. `ScrollPanel` accumulates delta and spends it in `UiTheme.LinePitch` units.
- **The wheel going to the wrong control.** A `ListBox` consumes the wheel even at its own end, stopping
  the page dead. `UiTheme.StyleList` therefore wires `ScrollPanel.ForwardWheel`, which hands the wheel
  back to the nearest `ScrollPanel` once the list has nowhere left to go. It uses a **named** handler so
  `-=`/`+=` is genuinely idempotent — a lambda there stacks subscriptions and scrolls by a multiple.

**ONE scroll owner per screen.** A scroller inside a scroller sends the wheel to whichever region the
pointer is over, which is how scrolling the Boost page stalled when the cursor crossed into TRANSFORMS.
A page nested in a scrolling host **grows to its content** instead of scrolling itself (`_xformPage`,
`BasicSettingsPanel`, `AdventurePanel`). This is the same rule the Settings migration already states.

### The auditor is the oracle — and it has two hard-won rules

`UiLayout.Audit` walks a control tree and logs `UI AUDIT` lines for overlapping siblings, clipped
text, controls too short for their font, and content past a parent's edge. After any UI change, the
log must show zero of them. Two rules exist because breaking them made the audit lie:

- **Text-fit checks ignore visibility; geometry checks do not.** A box too short for its font is too
  short whether or not it is on screen. Panels that build content hidden and reveal it on refresh —
  the Yggdrasil orchard builds every tile `Visible = false` — were the only panels never checked,
  which is precisely where the high-DPI clipping was reported while the audit said "clean".
- **`PAST PARENT BOTTOM` applies only to non-`AutoScroll` parents.** A section canvas is *meant* to
  run past the fold; a fixed card is not.

`AuditWidth(host, ctx)` reports horizontal overflow separately, because the app scrolls vertically
only: a horizontal scrollbar's only visible symptom is the scrollbar itself — the control causing it
is off-screen to the right, so no amount of looking at the window finds it. It names the widest
child and the overrun.

- **`AuditWidth` must recurse, accumulating the parent's `Left`.** Until 1.2.17 it walked only the
  section's direct children, and every panel nests its content (Boosts is section → `_boostPage` →
  `_manualView` → the labels), so the one rule that catches horizontal clipping could not see the
  controls it exists for and reported clean on the pages where clipping happens. Child bounds are
  parent-relative: without the accumulated offset a nested control reports a right edge far short of
  where it paints. Measure through `EffectiveBounds`, like every other rule — an `AutoSize` label's
  `Width` understates the Mono render.

## LogTail (`Managers/LogTail.cs`)

Tails the on-disk logs for the LOGS section (Advisor / Loot / Session). In-memory mirrors exist for
some feeds (e.g. `Main.LootFeed`, newest-first ring capped at 400) — file writes are unchanged
either way.

## PresetInstaller (`Managers/PresetInstaller.cs`)

Copies the embedded `Presets/*.json` (resource names `NGUAdvisor.Presets.<file>.json`) into the
runtime profiles directory on first run, so `Goal-*`/`Normal-*` presets exist for
`ProgressionAnalyzer`/`StageDetector` to recommend. Plain JSON streams read fine under Mono —
unlike WinForms resx (see CLAUDE.md build notes).
