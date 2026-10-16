# Tokyo / London / New York session zones — design for review

## Approved visual and purpose

Owner approved the screenshot-style design and requested bug testing/proof before pushing with a
clean tree. Show when Tokyo, London and New York sessions occurred, and each session's actual observed
high/low. Preserve the existing dark theme, cyan/orange candles, primary DOM layout and all trading
behavior. No orders, cancellations, modifications, flattening, account changes or trading hotkeys.

The image is a concept, not implementation evidence. This document specifies calculation and failure
behavior that a picture cannot prove. Product implementation begins only after review of this spec
and its subsequent implementation plan. Work on `codex/chart-session-zones`, based on released beta7.

## Appearance

- Time-bounded, high/low-bounded rectangles, not full-height background stripes. Purple Tokyo,
  cyan London, blue New York; user can change each color. Default fill8%, thin matching outline.
- Candles/studies/bubbles remain readable over session shading. Order lines, crosshair and current
  price tags stay above session graphics. Zone bounds do not expand candle autoscale.
- Short session title; compact H/L price labels at native instrument precision. Developing zones
  show LIVE only with fresh source data; stale or incomplete coverage must not masquerade as LIVE.
- Optional completed high/low dashed extensions, ending at the next start of that same session.
  Past-session lines do not accumulate indefinitely. Labels are collision-spaced or omitted with
  hover detail, never silently displayed at the wrong price.
- Settings open separately in the existing chart settings dialog. No permanent settings card,
  new toolbar banner or DOM-level projection. Apply/CANCEL stay pinned and visible.

## Controls and initial preferences

Master enable and independent Tokyo/London/New York toggles, each with editable #RRGGBB color,
start/end HH:mm and time zone. Shared session definitions, independent per-pane visibility;
Apply to This chart/Both charts changes pane visibility, not a separate copy of session hours.

Initial custom presets (examples, not an exchange calendar): Tokyo09:00–15:00 Asia/Tokyo,
London08:00–16:00 Europe/London, New York09:30–16:00 America/New_York. Each is editable. Zone-name
resolution must work on Windows; persist stable zone IDs and validate aliases explicitly, without
falling back silently to the computer's local time zone.

Other controls: fill opacity0–30% (default8), H/L labels on/off, completed H/L extensions on/off
(initially on), previous completed sessions1–8 per definition (default3), and This/Both charts.
Session feature starts OFF for existing/new configurations; individual definitions start selected.
Cancel leaves all saved settings unchanged; invalid input blocks Apply with a clear inline message.
Malformed saved settings normalize safely; equal start/end is rejected, not interpreted as24hours.
Definitions persist globally for the workspace and always calculate the current exact contract.

## Time windows and high/low calculation

Generate each window from its session-local start date and named time zone. End later than start is
same local date; end earlier than start is next local date. Convert each boundary independently to
UTC so DST cannot be replaced by a fixed offset. Overlapping sessions remain independent.
No session is invented just because it is a weekday; if source data is absent, no valid H/L exists.
Do not assume exchange holiday hours from these customizable visual presets.

Minute-aligned boundaries use native30second bars, whose end timestamps are already normalized to
UTC by the chart bridge. Include a closed bar only if its complete interval lies within the session:
bar end >session start and <=session end. A forming bar may contribute observed values only when its
start is inside the window and the current time is before that window's end; label range developing.
Never include the preceding session's bar at the start boundary or a bar crossing the end boundary.
High=max(native highs), low=min(native lows), in integer ticks. Do not derive these from DOM levels,
PAX opening-range logic, candle color, sparse tape retention or trade-bubble thresholds.

During a current session, box ends at current observed progress, not a forecast high/low. After the
end, freeze that window's result except authoritative native corrections. No future source data may
leak into a completed session. Display the current window plus at most the configured previous
completed windows per definition. Empty windows are omitted rather than filled with zero.

Boundary proof and coverage matter: a result with truncated start/history, stale/disconnected source
or missing end coverage is PARTIAL/STALE in labels/hover, not a certified complete session. Do not
interpolate missing highs/lows. Sparse no-trade bars are not fabricated. Conservative partial labeling
is acceptable when the existing source cannot prove full provider coverage.

If a configured wall time falls inside a DST skipped or repeated hour, omit that affected session
and report why in settings/hover; do not guess an offset. Ordinary sessions before/after transitions
continue using their correct offsets. Tests must pin these decisions.

## Data path and rendering isolation

Recommended: share the existing read-only slot2 native30second history used by PAX. Request it when
PAX OR session zones need it, not only when PAX is enabled. Keep the existing28calendar-day/75000-bar
bound. No extra socket, new native subscription type or changed port is expected. Disabling sessions
must not unsubscribe a source still used by PAX, and vice versa. Hidden charts stop repainting and
release unshared requests as they do now.

Alternatives considered: chart-timeframe bars would be simpler but can mix prices outside custom
session boundaries; live prints alone would miss loaded history and be coupled to capture gaps.
Neither supplies reliable historical session highs/lows for this feature.

Calculate immutable session snapshots off the UI thread on source-version/config/time-boundary changes.
Cache and share identical requests between both panes. Reject late task results after instrument,
settings, subscription generation, hide/dispose or context changes. Do not rescan75000 bars at30Hz
just because the chart repaints. Source errors produce a readable status, not an unobserved task fault.

Project UTC window boundaries into each pane's native time axis. Intraday frames through4h can show
the same precise source range independent of displayed candles. Day charts omit session boxes with
an explanation, since native daily timestamps cannot establish intraday x placement safely.
Clip to plot; do not draw into axes. Limit history to3×(8 completed+1 current)=27 rectangles, plus
their bounded labels/extensions. No rendering work is scheduled for disabled/hidden charts.

## Tests and proof required

Tests first, then implementation; each confirmed bug needs a reproducing failing test before its fix.
Pin literal high/low fixtures at start/end boundaries, minute windows, overnight dates, independent
overlaps, active sessions, completed freezing, late native revisions, negative prices and empty data.
Test London/NY DST changes on different weeks, Tokyo fixed offset, skipped/repeated wall times,
invalid zone IDs, truncated source, stale/disconnected data and recent-history limits.

Integration tests must cover session-only source requests, sharing with PAX, no premature unsubscribe,
timeframe switching, contract switching, late async rejection and hide/dispose. Settings tests cover
Apply/Cancel, per-session enable/colors, This/Both charts, JSON roundtrip and invalid input. Use actual
renderer pixel tests at100/150/200%DPI and narrow widths; prove layering, clipping, label behavior,
unchanged autoscale/navigation and zero commands in the real isolated Ticket command sink.

Run full suite (current baseline356), Release build and installed-NT-DLL compile check. Use the
standalone desktop fixture to capture the actual implemented UI and benchmark sessions off/on with
both panes, bubbles and dense candles. Report what was actually measured, not HFT/native parity.
Get one fresh independent read-only branch review; fix important findings with regression evidence.

If desktop control works, test only safe chart UI. Its current helper has failed initialization;
if still unavailable, disclose native runtime checks as pending rather than claim they passed.

## Delivery

After verification: commit, push the feature branch, leave a clean tracked tree, package the next
unused local beta version and provide actual screenshots, checklist, test counts and measured limits.
The owner authorized push once proved. This request does not explicitly request another GitHub
release/main integration; leave published beta7/main untouched until instructed to release.
Do not install into or restart the owner's running app/NT as a hidden packaging step.
