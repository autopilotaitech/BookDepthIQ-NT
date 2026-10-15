# Two-chart beta workspace

Release: `1.13.0-beta.3`, integrated into main after owner testing/approval.
Owner reports chart loading and compact layout working; broader testing continues. No automatic
installation changes are performed by this release workflow. See [release notes](RELEASE-1.13.0-beta.3.md).

Beta3 tightens the DOM telemetry to two compact rows: position/account, then market. Individual
readouts wrap at narrower widths instead of moving entire titled groups onto tall separate rows.
Fonts, values, tooltips and room bar remain unchanged. Chart and trading behavior are unchanged.
Upgrade from beta2 is app-only; the beta2 chart bridge on36974 is unchanged.

## Next build: beta4 chart orders (feature branch)

`codex/chart-order-overlays` adds display-only working entry/TP/SL lines and position-average lines
to both charts. Labels include remaining quantity and price. Equal-price/type/side orders share a
label with total remaining quantity and an order count. Stop-limit trigger and limit are distinct.
MIT uses NT's stop trigger field, verified against the
[official price-field documentation](https://ninjatrader.com/support/helpguides/nt8/price_selector.htm).
TP is green, SL red, average cyan/accent, other buys/sells follow existing bid/ask colors.
Offscreen levels use arrow tags; they do not force candles to zoom out. Crowded labels are spaced,
and excess levels have a count indicator rather than silently overlapping every label.

Only the selected account/instrument's fresh connected trading snapshot is projected. Account change,
disconnect, fill/cancel or flat position clears the corresponding display on the next chart refresh.
Pending cancellation remains displayed until the source snapshot confirms removal. No orders are
created, moved, cancelled or managed from a chart: existing chart dragging still pans the viewport.
Chart settings → Orders & position (display only) hides the overlay per pane; enabled by default.
No new native bridge/protocol is required. Beta4 is app-only from beta2/beta3.

Isolated tests cover projection, account/contract isolation, stale/disconnected state, remaining
quantity, moves/cancel/flat lifecycle, invalid prices, stop-limit dual levels, both-pane clearing,
render/toggle behavior and an empty real Ticket command sink. Native execution was not exercised.

## Next build: beta5 chart navigation (feature branch)

`codex/chart-navigation-polish` adds a tick-aligned live-price line/tag, crosshair price/time
badges and OHLCV readout, optional linked crosshairs, and a CENTER action that preserves horizontal
zoom. Settings can hide the last-price display or crosshair and disable candle-center snapping.
Visual settings apply without reloading history; the menu has an explicit Reload history action.
Dense views use pixel buckets that retain candle OHLCV and study extrema; native bars stay untouched.
See [verification and limitations](CHART-NAVIGATION-VERIFICATION.md). This is not a published release.

## Controls

- CHARTS toggles an optional left column. Initially off; top3m, bottom15m.
- Default32% charts /68% unchanged existing DOM. Drag the vertical divider to change the saved ratio;
  drag the horizontal divider to resize chart heights. Existing tape/ladder split remains independent.
- Charts temporarily collapse below readable width (at least1390DIP, higher for a wide saved tape).
  Maximize or widen the window to restore them. Collapsing charts returns their width to the DOM.
- Per-pane frame choices:1m/3m/5m/15m/30m/1h/4h/Day. Same exact instrument as the DOM.
- Wheel plot to zoom around pointer; drag plot to pan; LIVE/End resumes following. CENTER/Space
  recenters on the current price without resetting time zoom. AUTO or double-click price axis
  restores visible-candle scaling. Drag or wheel the price axis to scale; Shift-drag pans price,
  Shift-wheel scales price over the plot. Home/double-click plot resets view.
- Menu: chart settings, maximize/restore within chart column, hide. Settings contain per-frame lookback,
  optional volume, SMA/EMA period and optional PAX-style opening range (1–8 historical sessions).
- PAX uses shared DOM opening-time/timezone/step preferences; OFF initially and disabled on1h/4h/Day.
  This is a supported independent adapter, not a generic NinjaScript/DLL loader.
- History defaults:1m3000,3m2000,5m2000,15m1500,30m1500,1h1500,4h1000,Day500; custom250–10000.
  Gaps remain gaps. No automatic analytic DOM levels or chart order entry; beta4 adds optional
  display-only order/position overlays as described above.

## Install and data path

Package includes BOTH `BookDepthIQBridge.cs` and `BookDepthIQChartBridge.cs`.
Close NT and install both into its Custom/AddOns directory, then compile in NinjaScript Editor.
The existing order bridge stays on localhost36971; read-only charts use localhost36974.
Port36972 belongs to Bubbles and is not changed. Beta2 fixes the beta1 port collision and requires
a chartVersion1 handshake before sending subscriptions. Replace BOTH the beta1 app and chart AddOn;
updating only one leaves charts unavailable. Recompile NT after replacing the AddOn.
An old installation lacking the chart AddOn reports unavailable charts; the DOM remains independent.
No installer or test in this change updates a running NT installation automatically.

Native BarsRequest supplies history and authoritative live OHLCV. Raw Last events provide only the
current-price tag; they do not add native candle volume. Equal slot requests share the app cache and
one chart subscription. Generation/revision guards reject late data after context switches.
Chart rendering is capped at30fps independently of the existing20Hz management loop. Hidden charts
stop repainting; their unshared subscriptions are released. All local caches are memory-only.

## Verification and honest limitations

Isolated tests exercise cache bounds, corrections/generations, viewport math, HTF eligibility,
native history chunking/unsubscribe, chart-only socket subscription/reload, shared caches,
100000 synthetic Last messages without volume duplication, DST endpoints, missing opening windows,
headless chart rendering, settings-driven study repaint and narrow-window restoration.
The AddOn compiles with the installed NT DLLs as net48/C#6; the app builds in Release.

Still required before release: read-only native bar/session/daily settlement comparison, supplied
PAX indicator comparison, real provider reconnect checks and callback→model→render/DOM scheduling
benchmarks. HFT or measured latency improvements are NOT claimed. No native orders were submitted.
Full persistent manual viewport state, raw-tick diagnostic replay ring and detailed latency telemetry
from the design are not in this development build. Beta5 adds optional crosshair linking.

The chart OR adapter conservatively requires a completed native30second opening bar. Historical
extensions activate at the first native30second bar proving a crossing, not a claimed exact tick.
It currently loads a bounded28calendar-day native30second window (max75000 records), rather than the
proposed14→28day adaptive fetch, and retains those bars for subsequent corrections. Missing opening
bars omit that session's overlay; native parity is not certified. No third-party ZIP source is bundled.
