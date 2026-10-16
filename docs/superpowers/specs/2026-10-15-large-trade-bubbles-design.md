# Large executed-trade bubbles — design for approval

## Intent and approved appearance

The owner approved the cyan/orange bubble mockup on 2026-10-15. Add readable large executed-trade
markers to the existing two candle charts, with adjustable per-instrument colors and thresholds.
Keep the DOM primary, current chart layout/theme, navigation and existing order overlays unchanged.
Settings open in the existing chart settings dialog; the mockup's settings card is not permanently
placed beneath the chart. No permanent legend or toolbar banner is required.

This specification adds explicit data/coverage constraints that were not conveyed by the picture.
Owner approved live-first scope and suggested optional history in settings. Product implementation
has not started. Historical capture is planned as a separate opt-in enhancement, not silently enabled.

## First build scope

- Individual reported Last prints only. Quantity is the reported contract volume of one print,
  not an inferred parent order. Minimum threshold is inclusive.
- Cyan ask-side, orange bid-side, gray unknown; all customizable. Unknown stays unknown: no
  directional guess from candle color. Aggressor classification is not institutional identification.
- Live collection while the feature is enabled on at least one visible chart. No startup historical
  backfill in this first build. Existing candle history must never be used to fabricate prints.
  Future settings will offer historical backfill OFF by default, with bounded lookback and explicit
  coverage status. Do not ship a nonfunctional toggle. Actual tick records are required; unknown or
  tick-rule-inferred historical sides must not be presented as confirmed bid/ask classifications.
- Retained prints can be projected onto either chart timeframe. Same source print is stored once;
  two charts do not double-count it. Switching timeframe preserves retained instrument data.
- Working account orders remain existing TP/SL/entry lines. Resting market-depth orders, sweeps,
  iceberg detection, alerts, predictions and grouped volume-at-price are outside this build.

## Architecture and alternatives

Recommended: reuse the existing DOM's classified trade ingress, feeding a dedicated bounded
chart-trade store. Do not scan the newest-first Tape list on every repaint, and do not attach another
native market-data subscription. No trading commands or native bridge protocol changes are needed.

Alternatives considered: extending the chart bridge supplies a separate independent print stream
but requires new side classification/protocol and bridge deployment; tape-list scanning is simpler
initially but loses history at tape truncation and repeats filtering on the UI thread. Neither is
needed for the first live-only build.

The store accepts exact contract identity, integer price ticks, positive contract volume, event time,
known/unknown side and a local monotonically increasing identity for each received event. Identical
time/price/size prints are not deduplicated: they can be separate legitimate trades.
Instrument changes must not relabel old prints. Clear the active store on contract change.
Disconnect preserves already captured markers but ends coverage; reconnect starts a new coverage
segment, never implying missing trades were captured. Late events are ordered by event time for
projection; receipt identity provides a deterministic tie-breaker.

Store at most 50000 prints and at most 60 minutes of received coverage, whichever limit is reached
first. These are initial safety bounds, not a promised full session. Store all received prints within
those bounds while enabled so lowering a threshold can refilter captured data. Clear capture on
disable-all; reenable starts new coverage. Candle history/lookback is independent of print retention.
Coverage start/end and evictions appear in chart settings/hover detail, not a new toolbar banner.

## Settings and persistence

Profiles are keyed by normalized instrument root: NQ and MNQ are separate; NQ contract rollover
retains the NQ profile. Unknown roots use exact instrument identity rather than an unsafe guess.
Capture always uses the exact active contract, never the profile root as its data key.

Each profile persists minimum contracts, buy/sell/unknown colors, opacity, minimum/maximum diameter
and quantity-label policy. Pane enable state is independent. Apply to This chart changes that pane's
enable state; Both charts changes both. Editing the shared instrument profile affects both panes
that show that instrument; the dialog explicitly explains that distinction.

Initial defaults: disabled, minimum50 contracts, cyan/orange/gray,20% fill opacity,12–42 DIP diameter,
labels on large markers. These are illustrative generic defaults, not instrument-calibrated signals.
Minimum contracts validates1–1000000; opacity0–100%; diameter6–80 DIP with max>=min.
Labels: Off, Large markers only (diameter>=24 DIP), All. Size uses a fixed clamped square-root ratio
to the profile threshold: diameter=clamp(minDiameter*sqrt(volume/minContracts),minDiameter,maxDiameter).
Panning/zooming never renormalizes marker sizes against visible trades.
Color input must validate and provide usable foreground contrast. Cancel must not mutate settings;
Apply persists without reloading candles or modifying any trading setting.

## Rendering and interaction

Project prints by timestamp into authoritative native candle intervals; use the existing native
end-time convention, and session/trading-date information where necessary. Do not snap across missing
sessions or silently assign a print to a later unrelated bar. If a containing interval cannot be
established, omit it rather than inventing a candle. Dedicated boundary/gap/DST/Daily tests are required.

Draw circles at trade time and native price, clipped to the plot. Transparent fill, thin outline,
legible quantity; do not change autoscale to include bubbles. Preserve price/crosshair tags and order
labels above bubbles. Bubbles never become order handles and never consume trading shortcuts.
Hover shows price, event time, quantity, classification and captured coverage context.

Query only visible time/price ranges. Limit drawn markers to500 per pane, prioritizing largest
quantities then receipt identity deterministically. Never sum overlap into a purported individual
trade; dense-view omission count is available in settings/hover. If markers overlap, hover identifies
the top selected print and overlapping count. Render cap remains30fps; hidden charts do not repaint.
No new formatted text, geometry or full-history rebuild per incoming trade on the rendering thread.

## Verification and delivery

Test first: threshold boundaries, unknown side, identical legitimate prints, exact-contract clearing,
capture coverage gaps/eviction, bounded memory, setting validation/JSON rollover profiles, both-pane
toggle semantics, timestamp/session mapping, quantity/size math and deterministic render limits.
UI tests cover readable numbers at100/150/200%DPI, clipping, hover, navigation, unchanged candle
autoscale and no order commands. Stress with100000 synthetic prints and benchmark both panes with
the bubble layer off/on. Report measurements honestly; no HFT or native latency certification.

Run full suite and Release build, independent read-only review, desktop synthetic preview, and
native read-only chart observation if desktop access is available. Owner permits running-app UI
interaction but forbids all trades/orders, including cancellation, modification and flattening.
Do not bypass desktop-control failure with unobserved UI inputs.

Deliver on a feature branch based on current chart work, unique local test version, verification
notes and screenshot. No merge to main, published release or forced installed-app restart in this
scope. Historical backfill/grouped mode require a separate follow-up design and verification.

## Implementation correction

Independent review found legacy DOM ms and chart-bar timestamps use different timezone conversions.
The build therefore adds chart-only chartMs/inst metadata to live trade packets, preserving legacy
analytics/execution timestamps and all order behavior. Exact instrument metadata is required; older
bridges do not draw bubbles and explain the needed upgrade in settings. This supersedes the original
app-only assumption. No installed bridge is automatically replaced. Density budgeting may show fewer
than500 markers; hidden quantities are not summed and omission counts are available.
