# Changelog

All notable changes to NGU Advisor are documented in this file.

## [1.2.15] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0.

### Fixed

- **Number fields, fourth attempt — and this time the log says which control was wrong.** The audit's ten
  `UpDownTextBox h=32 < 38` findings survived 1.2.7 (state an explicit height), 1.2.12 (measure the chrome)
  and 1.2.13 (turn AutoSize off), because all three sized the OUTER control — and the outer control was
  never the problem. The startup line proves it: `num chrome 4`, meaning the height was accepted and the
  client area is the full line. The inner edit box simply does not keep the height it is given, because
  Mono re-runs the control's own layout afterwards and there is no Resize to hang a re-apply on. The
  stretch is now re-applied on `Layout`, which fires after that pass.
- The startup `UI metrics:` line also reports `num inner`, the height the inner box keeps on a control the
  advisor owns. If a report ever shows a short field again, that number says whether the stretch failed or
  something else undid it — three of these rounds were spent inferring the mechanism from the audit alone.

## [1.2.14] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0.

### Added

- **Cube only.** A tick box on the Boosts page sends every boost to the Infinity Cube and parks the
  priority list. The Cube dropdown beside it only ever chose HOW the cube is fed once boosts reach it;
  there was no way to say that it should get them instead of your gear. Merges, filters and convertibles
  keep running — a switch labelled "cube only" turning those off would be a surprise. While it is on the
  live readout says the list is parked rather than showing an empty list next to a full one.

## [1.2.13] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0. Number fields, third attempt —
this time against the actual cause.

### Fixed

- **Number fields fit their text.** `AutoSize` was the answer all along. `UpDownBase.SetBoundsCore` runs
  `if (AutoSize) height = PreferredHeight`, so a `NumericUpDown` silently DISCARDS every height assigned
  to it and keeps the one it derives from `Font.Height` — the 96-DPI value. Both earlier attempts were
  writing to a property the control threw away: 1.2.7 stated an explicit height, 1.2.12 measured the
  chrome and stated a bigger one. The log said so plainly — `num chrome 4` where the real chrome is 9,
  because even the measuring probe had been resized behind its own back. `AutoSize` is now turned off
  before the height is set, on the control and on the probe.
- The IDs row on a Loadouts card no longer clips its text box by a pixel. The row was a scaled `S(28)`
  = 43px holding a box floored at the measured line, which ends at 44; it is now derived from where its
  children actually end, like the lists and cards already were. The box also states its height instead of
  letting Mono choose one.

## [1.2.12] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0. Number fields are the right
height on a scaled display — for real this time.

### Fixed

- **Number fields fit their text.** The layout audit had ten standing findings across Loadouts, Blood,
  Gold, Pit, Yggdrasil and Quests — every `NumericUpDown`'s inner edit box at 32px against a 38px line,
  so digits sat visibly off-centre and descenders clipped. 1.2.7 claimed this fixed and it was not: the
  control's height was set to the line plus a **guessed** 3px allowance, but Mono's chrome (border plus
  internal padding) spends about 9px, so a 41px control had only a 32px client area — and the inner box
  can never exceed that, however hard the advisor stretches it. The chrome is now MEASURED at startup
  from a probe control, like every other metric in the interface, and the field is sized to the line plus
  that. It is logged in the `UI metrics:` line so the next report can be diagnosed from the log alone.

  Number fields are therefore ~6px taller than in 1.2.11. Rows that contain one derive their height from
  it and follow automatically; if a fixed-height card somewhere no longer fits its row, the audit will now
  say so instead of clipping in silence.

## [1.2.11] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0. Follow-up to 1.2.10 from
in-game testing of the Boosts page.

### Fixed

- **The page no longer judders.** The priority list and the "will boost now" readout were rebuilt on
  every settings write anywhere in the advisor, coalesced to once a second — and each rebuild repaints a
  whole owner-drawn list, while the readout re-resolves every item through the inventory. Both now
  compare against what is already displayed and touch nothing when it matches.
- **Del removes the selected item**, the keyboard equivalent of Remove.
- **Trashing an item in game takes it off the boost list.** The list is pruned of items you no longer own
  (equipped, inventory, daycare and MacGuffin slots all count as owned — a set in daycare is not gone),
  and every removal is logged. It never prunes while the inventory reads as empty, because at that moment
  everything would look unowned.

### Removed

- Drag & drop reordering. It was built as a layer over the buttons and does not work in the game's Mono
  runtime; the buttons and Alt+arrows are the reorder path.

## [1.2.10] - 2026-07-29

Existing settings and profile files remain compatible with version 1.1.0. Boosting now follows one list
instead of three implicit sources, and the Boosts page gets a picker and proper reordering.

### Boosts

- The priority list is now the **only** thing that gets boosted. Equipped and locked-inventory items are
  no longer boosted implicitly, and the boost blacklist is gone from the page — it is emptied once on
  startup, since with one source there is nothing left for it to exclude. On first run your list is
  seeded once from what you currently have equipped and locked, so nothing changes silently — check the
  log line and prune the list to taste.
- New **Add from inventory** picker: search, multi-select, and see level, remaining boosts and where each
  item is. No more typing item IDs.
- Reordering: multi-select, Top/Up/Down/Bottom, Alt+↑/↓ and Alt+Home/End. **The selection stays put after
  a move**, so you can click Up repeatedly to walk an item several places — previously the row deselected
  itself and you had to re-pick it every time. The page also shows a live "will boost now" readout.
- Merging is now governed only by the transform-chain toggles.

## [1.2.8] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. The scrollbar is now sized like
the rest of the interface.

### Changed

- **The advisor draws its own scrollbar.** `AutoScroll` draws the operating system's scrollbar, and its
  width is a system metric with no property behind it — so on a display at 200% scaling it stayed a thin
  sliver beside 38px text, as hard to grab as it looked, and nothing short of replacing it could change
  that. Scrolling surfaces now own a scrollbar whose width is derived from the measured text like every
  other dimension in the interface, beside a content panel they offset themselves.

  This also removes the last of the scroll faults inherited from `AutoScroll`, since the offsetting is
  ours: no blit, so no streaks left behind by children that paint outside it.

  One structural consequence, documented in `docs/modules/ui-infra.md`: children of a scrolling surface
  belong to its `Content`, not to the surface itself, which holds the scrollbar. A single indirection in
  `SettingsForm` keeps every placement call site unaware of it.

- Scrolling surfaces watch their children for size and visibility changes. A card that grows when a plan
  wraps, or the settings list shrinking to its search results, changes how far the page can scroll, and
  nothing else was in a position to notice.

## [1.2.7] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. Scrolling is fixed.

### Fixed

- **Touchpad scrolling works.** WinForms turns a wheel *notch* into scrolling, but a precision touchpad
  does not send notches — it sends a stream of deltas far smaller than one, each of which rounded to zero
  or to a whole notch, so a page either refused to move or jumped. Scroll delta is now accumulated and
  spent in measured line units, so a slow drag scrolls slowly.
- **Scrolling no longer leaves streaks across the window.** Scrolling blits the client area and invalidates
  only the newly-exposed strip, so children that paint outside the blit — the owner-drawn lists especially
  — left their old pixels behind. The scrolling surfaces are double-buffered now.
- **The wheel no longer gets stuck on a list.** A list consumes the wheel even when it is already at its
  end, which stopped the page dead as soon as the pointer crossed the priority list. A list now scrolls
  while it still can and hands the page back the moment it cannot.
- The TRANSFORMS column no longer scrolls independently inside the Boost page. A scroller inside a
  scroller sends the wheel to whichever region the pointer is over, which is why scrolling stalled when
  the cursor crossed into it; it grows to its content and lets the page scroll, one owner per screen.
- Number fields are no longer short. Setting the control's height left its *inner* edit box sized from
  `Font.Height` at 32px against a 38px line — the last seven findings in the layout audit, now zero.

### Known limitation

- The scrollbar itself is still thin on a scaled display. `AutoScroll` draws the OS scrollbar and its width
  is a system metric with no property behind it; widening it means replacing `AutoScroll` with a custom
  scrollbar, which is a much larger change and worth far less now that the wheel behaves properly.

## [1.2.6] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. Shortened values now tell you what
they hid, and three layout faults from the high-DPI work are fixed.

### Fixed

- **Shortened text carries its full value as a tooltip.** Where a value genuinely cannot be given more room
  it still gets ellipsized — but the complete string existed nowhere the user could reach, so
  `UNLOCK: 100 SEE…` and `WAIT — 1e15 i…` were dead ends. Every fitted value now hangs its full text on the
  control, and only when it actually had to shorten it, so hovering never shows a tooltip repeating what is
  already on screen. This covers the wrapped two-line captions as well, which were the last place a value
  could be cut silently.
- The Adventure panel no longer cuts off everything below COMBAT STYLE. It has no scrollbar of its own by
  design — one there eats client width and re-wraps the content it was added to reveal — and its pages size
  themselves from their last row, so the host was placing it at a height that no longer described it.
- `Mode` and its dropdown stay together in the COMBAT STYLE row. The row wraps in narrow columns, and with
  all five controls in one wrap the break landed between the caption and the dropdown it labels.
- The gold pipeline arrows are back in place. 1.2.5 widened the arrow to the width its glyph measures
  without widening the gap reserved between the chips or the step past each arrow, so the arrows sat over
  the following chip. There is now one measured width used everywhere it is spent, and each arrow is centred
  on the chip it joins rather than at an offset that only looked centred at the old line height.

## [1.2.5] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. Dropdowns, lists and checkboxes
now scale with the display, and the Boost priority editor gets the room it always implied it had.

### Fixed

- Dropdowns, lists and number fields are no longer left at their 96-DPI size. A ComboBox, ListBox and
  NumericUpDown take their height from `Font.Height` — the same DPI-unaware value that already had to be
  worked around for measurement — so on a 200% display every dropdown was a 21px box of small text beside
  38px labels, and every number field was 32px against a 38px line. They were not merely ugly: they were
  hard to hit. There is no property for it, so these are now drawn by the advisor, the same lever the tab
  strip has always needed under this Mono. The look is unchanged; only the size is.
- **The Boost priority editor has usable space.** Its two lists were specified in pixels — 90px and 56px —
  against rows that pitch from the rendered line, so they showed three entries and one, with a scrollbar,
  while hundreds of pixels sat unused below them. Lists are now specified in ROWS (priority 8, blacklist 4)
  and everything beneath them positions off the list's real bottom edge, so the buttons and the apply-order
  row follow instead of being overlapped.
- Checkboxes scale with the text. The check glyph is a fixed ~13px system metric that ignores both the font
  and the DPI, so it cannot be enlarged by any setting — these are now drawn, and the whole control is the
  click target. They remain ordinary checkboxes: toggling, keyboard, tab order and every binding are
  untouched.
- The TRANSFORMS notes no longer lose their ends ("…spare copies keep mergin"). They were auto-sizing
  labels running past the panel edge, which clips silently; they now wrap, per the no-ellipsis rule.
- The gold drain ledger no longer clips its rates ("1,000E+012/s · 5%" needed 205px in a 186px column). The
  value column is measured and the bar absorbs the difference — a shorter bar reads fine, a cut number does
  not. The pipeline arrows are measured too, instead of trusting a 16px slot.
- Rows that contain a number field are derived from it rather than scaled beside it, so the field no longer
  overhangs its row by a pixel or two at every scale.
- The log slivers read as many lines as they can actually show, now that list rows are pitched at the
  measured line height rather than an assumed one.

## [1.2.4] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. This finishes the high-DPI work
started in 1.2.3: the window scaled correctly there, but text still clipped inside individual boxes.

### Fixed

- Headings, card titles and button captions no longer clip on high-DPI displays. The layout sized many
  single-line labels at 18-22px for text that renders 25px, so descenders were already being shaved by a
  pixel or two — invisible at that size. Scaling those heights preserved the shortfall and multiplied it, so
  at 200% an 18px box became 27px for 38px text and lost 8px of every "g" and "y". Buttons showed the same
  fault differently: the caption looked vertically off-centre rather than short. Any control that holds text
  now takes its height through a floor that scales *and* guarantees what the renderer needs.
- The growth tiles no longer cut their third line, and the fruit, light, chip and priority cards no longer
  cut their contents. A fixed-height card is now derived from the lines it holds instead of being scaled
  alongside them — the two drifted apart as soon as the line heights were floored.
- Yggdrasil fruit labels no longer lose the end of their text ("UNLOCK: 100K SEEDS" arrived as
  "UNLOCK: 100K SEE"). The text was measured in the 7.5pt chip font but painted in the 9pt one, so the fit
  believed more fitted than did — and an overflowing fixed label paints cut, with no ellipsis to hint at it.
  This was wrong at every scale; high DPI only made it visible.
- The refresh (`↻`) and dismiss (`✕`) buttons size to their glyph instead of a fixed width they had already
  outgrown.

### Changed

- The layout auditor no longer skips hidden controls when checking whether text fits. A box too short for
  its own font is too short whether or not it is on screen, and panels that build their content hidden and
  reveal it on refresh — the Yggdrasil orchard builds every tile hidden — were consequently the only panels
  never checked. That is exactly where the clipping was reported while the audit reported "clean".
  Geometry checks still apply only to visible controls, since alternate views deliberately share coordinates.
- The auditor also reports content past a card's bottom edge (for containers that have no scrollbar to reach
  it with) and any section wider than its viewport. A horizontal scrollbar's only visible symptom is the
  scrollbar itself — whatever causes it is off-screen — so it is now named in the log.
- The measured DPI metrics are logged culture-invariantly, so the diagnostic line reads `scale 1.52` rather
  than `scale 1,52` on comma-decimal locales.

## [1.2.3] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. This release fixes the settings
window on high-DPI displays. Nothing about how the advisor plays the game changed.

### Fixed

- The settings window is now readable on displays with Windows scaling above 150%. Every position and size
  in the window was hand-placed in pixels against a display where 9pt text renders about 25px tall, but the
  game's Mono renderer sizes text from the real screen DPI — so at 200% scaling the text grew by half again
  while the boxes holding it did not. Headings were cut off, stacked lines overlapped each other, and the
  status strip along the bottom was clipped out of the window.

  The layout metrics are now measured at startup from the text the renderer actually produces, and every
  hand-placed dimension is derived from that measurement. On a display at the original scaling the measured
  values reproduce the previous numbers exactly, so nothing moves there. The measurement is written to
  `debug.log` as the first `UI metrics:` line, and it never scales below the previous values — an
  under-reporting renderer keeps the old layout rather than shrinking it.

## [1.2.2] - 2026-07-28

Existing settings and profile files remain compatible with version 1.1.0. This release is a performance
pass on the advisor's own overhead, plus one timing fix. Nothing about how the advisor plays the game
changed: the gear optimizer picks the same loadouts, just far faster.

### Fixed

- Titans are no longer wrongly given up on when the system clock shifts. The "titan still available after
  10 minutes" check measured elapsed time against local time, so a daylight-saving change (or any clock
  correction) jumped it by an hour and the advisor immediately lowered the titan's version or dropped it
  from your swap and gold targets — and saved that.

### Changed

- The advisor no longer repaints its window while that window is closed. Closing the window hides it, and
  the whole live-status pump — status strip, rail, sliver logs, growth graph, progress bar — kept running
  and force-repainting four times a second for the entire session. The growth graph still samples with the
  window closed, so its history is unaffected.
- Settings are written to disk at most once a second instead of once per changed setting. The advisor
  rewrites settings continuously (titan targets, snipe state, banked versions), and each one previously
  serialized and wrote the whole file. Pending changes are always flushed on Unload and on game exit.
- The gear optimizer is substantially faster. Scoring no longer allocates per candidate evaluation, and the
  "keep one respawn item" pass no longer runs a complete optimization for every respawn item you own — only
  for the ones that can actually win. Both rewrites were verified to select the same result as before.
- Live log views (Combat, Economy, and the Logs SESSION tab) read only the end of their log file instead of
  the whole file on every refresh. This mattered most for the pit/spin and cards logs, which are appended
  to across sessions and so grew without limit.
- Smaller overhead reductions on the per-frame path: the in-game overlay rebuilds its text ten times a
  second rather than several times per frame, loot-log scanning no longer re-processes lines it has already
  reported, and repeated inventory and titan scans that recomputed the same answer were consolidated.

### Build

- The build finds NGU Idle's `Managed` folder automatically and can be pointed anywhere with
  `-p:NGUManagedDir=...` or a `Directory.Build.props`, instead of requiring seven hardcoded paths to be
  edited. A missing install now fails with that as the error.
- `SettingsForm.resources` is regenerated from the `.resx` automatically when it is out of date; this was a
  manual step that silently embedded stale form resources when forgotten.

## [1.2.0] - 2026-07-22

Existing settings and profile files remain compatible with version 1.1.0. This release is a large
correctness and robustness pass from a full external review, plus the first automated test coverage.

### Fixed

- Profiles no longer risk silent number corruption on comma-decimal system locales, and large integers
  round-trip exactly (culture-invariant JSON number handling).
- The EXP planner no longer overflows at high (Evil-scale) values.
- Boost-farm zone values were corrected — Evil zones were dramatically undervalued against ITOPOD, which
  suppressed zone recommendations; the advisor now compares them on the true boost-value scale.
- The final Sadistic titan zone (THE TRAITOR) is now reachable, and Sadistic zone-unlock thresholds are
  corrected (a missing zone had shifted several later zones).
- Iron-pill blood advice now matches what the caster will actually do (no more "cast now" for a pooled cast).
- Money-pit lock, inventory transform-chain protection, settings-form resilience, and numerous smaller
  correctness fixes across combat, gear, diggers, quests, wishes, and consumables.

### Added

- An automated test project (53 tests) guarding JSON round-trip, number formatting, and the titan tables.
- A single consistent large-number formatter across all panels.

### Changed

- The two progression-chapter engines are now documented as distinct, non-interchangeable concepts.

## [1.1.0] - 2026-07-15

Existing settings and profile files remain compatible with version 1.1.0.

### Added

- Two-level navigation with Overview and Priorities.
- Dedicated Profile page for allocation source, profile selection, editing, and file access.
- Searchable Settings interface.
- Persistent eight-cell status strip.
- Redesigned Loadouts interface covering Titan, Gold, Quest, Yggdrasil, Cooking, Loot Hunter, and Shockwave.
- Configured, WILL EQUIP, and CURRENTLY EQUIPPED snapshot displays.
- Contextual activity feedback for supported user actions.

### Changed

- Advisor home workflow is split between Overview and Priorities.
- Automatic Money Pit actions use a single configured owner, preventing competing automatic throw paths.
- Public release builds no longer embed local build-machine paths.
- Current-equipment snapshots update explicitly through REFRESH STATE rather than implying a live feed.
- The advisor now holds automatic Iron Pill casts until the pill has been available for at least 30 minutes and would add at least 10% of current base adventure power.

### Fixed

- A failure in one advisor operation no longer prevents later operations from running.
- Temporary Money Pit, equipment-lock, Yggdrasil, and MacGuffin state is restored after failures.
- Repeated faults are reported without flooding the log.
- Settings filtering and layout restoration no longer produce false overlap reports.
- Profile Editor paste operations validate before replacing the current loadout and retain the accepted undo behavior.
- Profile, Loadouts, status, and other updated views received layout and audit corrections.

### Removed

- Obsolete mode-loadout UI infrastructure.
- Superseded legacy Profile selector controls.
- Repetitive Yggdrasil fruit-state debug output.
