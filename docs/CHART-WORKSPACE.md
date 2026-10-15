# Two-chart development build

Branch: `codex/two-chart-workspace`. Local package version: `1.13.0-beta.2`.
Not published, merged or installed into the owner's NinjaTrader instance.

## Controls

- CHARTS toggles an optional left column. Initially off; top3m, bottom15m.
- Default32% charts /68% unchanged existing DOM. Drag the vertical divider to change the saved ratio;
  drag the horizontal divider to resize chart heights. Existing tape/ladder split remains independent.
- Charts temporarily collapse below readable width (at least1390DIP, higher for a wide saved tape).
  Maximize or widen the window to restore them. Collapsing charts returns their width to the DOM.
- Per-pane frame choices:1m/3m/5m/15m/30m/1h/4h/Day. Same exact instrument as the DOM.
- Wheel plot to zoom around pointer; drag plot to pan; LIVE/End resumes following. AUTO or double-click
  price axis restores visible-candle scaling; drag price axis pans price manually. Home resets view.
- Menu: chart settings, maximize/restore within chart column, hide. Settings contain per-frame lookback,
  optional volume, SMA/EMA period and optional PAX-style opening range (1–8 historical sessions).
- PAX uses shared DOM opening-time/timezone/step preferences; OFF initially and disabled on1h/4h/Day.
  This is a supported independent adapter, not a generic NinjaScript/DLL loader.
- History defaults:1m3000,3m2000,5m2000,15m1500,30m1500,1h1500,4h1000,Day500; custom250–10000.
  Gaps remain gaps. No automatic DOM level/order/position overlays, no chart order entry.

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
Crosshair linking, full persistent manual viewport state, raw-tick diagnostic replay ring and detailed
latency telemetry from the design are not in this development build.

The chart OR adapter conservatively requires a completed native30second opening bar. Historical
extensions activate at the first native30second bar proving a crossing, not a claimed exact tick.
It currently loads a bounded28calendar-day native30second window (max75000 records), rather than the
proposed14→28day adaptive fetch, and retains those bars for subsequent corrections. Missing opening
bars omit that session's overlay; native parity is not certified. No third-party ZIP source is bundled.
