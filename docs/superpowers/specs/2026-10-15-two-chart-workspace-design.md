# Two-chart workspace — design for review

Status: research and design only. Application implementation has not started. No release change.
Owner approved the concept, native theme, candle styling, PAX disabled on 1h/4h/Day, and LEFT stacked
charts / RIGHT existing DOM at roughly one-third/two-thirds with a draggable, persisted splitter.
The DOM is the main feature and must not be redesigned or simplified. This remains a design document,
not a claim of implemented application behavior.

## 1. Outcome and hard boundaries

Add two optional overview candle charts to the LEFT of the existing DOM, with tape/ticket on the
RIGHT. Preserve the current flight-panel design, ladder interaction, account/position strips, journal,
and order handling. Each chart uses the DOM's exact selected futures contract, its tick size and feed.
No second symbol picker, no orders from charts, no strategies, no automated signals, and no wholesale
copy of DOM levels. PAX OR is the explicitly requested optional chart overlay exception.

Native Avalonia controls and rendering, retaining .NET8/Avalonia11.2.3 and the existing NT8 net48/C#6
bridge requirements. No webview, chart-site accounts, external market-data services, Pine runtime,
or arbitrary third-party NinjaScript loader in this version. Do not change existing analytics/risk
semantics as a side effect of chart integration. Existing market/order subscriptions remain separate.

Low local latency is a core requirement, NOT a claim of exchange-level HFT. Feed/provider/NT delay
and timestamp precision are external constraints. Preserve the native Last events available to the
AddOn; distinguish original feed-event time from monotonic local timing. Chart rendering may coalesce
frames; calculation must not sample away the high/low/volume/path information needed for OHLC and PAX.

## 2. Theme and workspace

- Keep existing Pal surfaces/text/market tokens and Flight.Sans/Pal.Mono typography. No new theme.
- Keep the ENTIRE existing DOM workspace on the RIGHT: header, telemetry/account/position strips,
  heatmap, ladder columns, profile, tape, Outlook, ticket and trading buttons in their existing layout.
  Its header and strips remain above the DOM, not stretched across the left charts. Wrap that existing
  workspace with a left chart column and draggable vertical splitter; do not rebuild or simplify it.
  A compact CHARTS toggle shows/hides the chart column, returning its width to the DOM when hidden.
- Charts are OFF by default for existing installations; opening them is deliberate. Default choices
  when enabled: top3m, bottom15m. Remember the enabled state, individual chart visibility, width,
  divider ratio, timeframe, PAX preference, volume preference and viewport preferences.
- Initial allocation: LEFT charts32%, RIGHT existing DOM68%, approximating the owner's screenshot.
  Persist the proportional split, not a fixed chart width. Dragging the main vertical splitter resizes
  both regions using the existing DOM splitter interaction style. Window resizing preserves the chosen
  ratio where possible, clamping to readable minimums without overwriting the saved ratio.
  Vertical divider ratio starts0.5, clamped0.2–0.8. One hidden chart lets the other fill the column.
  Hiding both collapses the column; CHARTS restores the last nonempty configuration.
- Preserve chart-column minimum320 DIP, ladder minimum760 DIP and side minimum300 DIP while charts
  are visible. Keep the existing tape/DOM splitter independent of the new chart/DOM splitter. Below available
  width1390 DIP, temporarily collapse charts and explain via CHARTS tooltip; do not overwrite the
  saved preference or squeeze the ladder into unreadable columns. Reopen when sufficient width returns.
- Per-chart header: instrument, timeframe, Follow/Live, Auto scale and overflow menu. Local overflow
  contains Hide chart, Maximize/Restore, PAX overlay and Chart settings. No large drawing toolbar.
  Maximize fills the CHART COLUMN, not the trading workspace; ladder and emergency controls stay visible.
- PAX can be switched in chart settings/overflow. Its disabled HTF entry has a tooltip explaining the
  intraday-only support. No warning paragraph floating over candles.
- Current-price axis tag, restrained grid and crosshair OHLC/time readout. Optional volume OFF by
  default; volume and indicators must not force a second permanent chart-status row.
- The earlier interactive preview's candle styling is approved; its simplified DOM and full-width
  header are superseded by the preservation requirements above. It is not the exact application layout.

## 3. Timeframes and navigation

Exact chart choices: 1m,3m,5m,15m,30m,1h,4h,Day. These are independent of ADX's timeframe setting.
They map to native NT Minute1/3/5/15/30/60/240 and Day1 bars. Preserve provider/session bar definitions;
do not create 4h bars by dividing UTC milliseconds or derive Day bars from midnight buckets.

- Follow ON keeps the latest candle near the right with8 bars of right padding. It preserves the
  user's horizontal zoom; it does not force current price into the vertical center every tick.
- Auto scale ON fits the VISIBLE candle highs/lows with8% padding, not every loaded historical bar.
  On completely flat data use at least4 ticks of vertical span. Do not include offscreen PAX levels
  in autoscale by default; they must not flatten the candles into a narrow strip.
- Wheel over the plot zooms time around the pointer. Drag pans time; a horizontal pan turns Follow
  OFF but does not silently turn Auto scale OFF. Keyboard navigation mirrors these operations.
- Dragging the price axis moves/scales price and turns Auto scale OFF. Double-click price axis
  restores Auto scale. Double-click time axis resets candle spacing. Return to live restores Follow;
  Reset view restores Follow+Auto and the default120-bar visible window.
- Follow and Auto are independent. Never snap a manually panned/scaled viewport on a new tick.
- Crosshair is local by default. Optional time-linked crosshair uses the same UTC instant across
  the two timeframes; price-axis scales remain independent. No DOM recenter on chart interaction.
- Changing timeframe cancels the previous chart generation, shows the new timeframe immediately,
  and discards old bars/overlays; never relabel old3m data as15m while history loads.
- If oldest retained bars roll off during manual pan, keep the same visible time anchor while it
  remains loaded; otherwise clamp to the oldest retained bar without silently enabling Follow.

## 4. Historical lookback — explicit defaults and limits

Use HISTORICAL BAR COUNTS as the canonical setting, not an ambiguous calendar-day promise. A session
template changes bars per day; holidays, RTH/ETH, partial sessions and provider limits change coverage.
The user sees requested bars, loaded bars and earliest/latest loaded dates in Chart settings.

| Timeframe | Default historical bars | Approximate ETH trading sessions covered |
|---|---:|---:|
| 1m | 3000 | 2.2 |
| 3m | 2000 | 4.3 |
| 5m | 2000 | 7.2 |
| 15m | 1500 | 16 |
| 30m | 1500 | 33 |
| 1h | 1500 | 65 |
| 4h | 1000 | about167 |
| Day | 500 | about2 trading years |

Intraday estimates assume an approximately23-hour futures session;4h assumes up to6 bars/session.
They are explanatory estimates, not a provider-data guarantee. Default visible window is120 bars;
loaded history is not all forced onto screen.

- Each chart retains a lookback value for EACH timeframe. Settings offers250/500/1000/2000/5000 and
  a validated custom count250–10000. Loading500 daily bars must not become500 one-minute bars on switch.
- Hard cap10000 retained OHLCV bars per visible chart and at most20000 candle bars across both chart
  slots. Live append evicts the oldest bars. If both slots share a request key, deduplicate storage.
- Initial request uses the installed BarsRequest(Instrument,int barsBack) API. Provider responses may
  include a forming last bar; store its forming status explicitly. No invented empty candles.
- Do not download raw ticks to reconstruct months of candles. History uses native OHLCV; live uses
  the native bar engine/current Last data as described below. The existing DOM tick-history duration
  and ADX effective lookback are not silently increased by chart settings.
- No unbounded "infinite history" fetching. Pan to the oldest loaded bar stops there. Change lookback
  and Reload through settings/overflow for more. Any larger/full-history product can be designed later.
- Missing/unsupported history reports the provider's error and retained coverage, not fabricated bars.
  A disconnected chart freezes with a clear local state; it does not silently appear live.
- V1 caches are bounded IN MEMORY, not a new disk-cache database. Reconnect refreshes authoritative
  recent bars; no stale disk prices are trusted. Disk persistence is a separately reviewed enhancement.
- Session defaults to the instrument's native TradingHours template. Advanced settings may select
  an installed NT template by its verified name. Day bars remain provider-defined; do not present an
  intraday RTH filter as if it changes historical daily settlement bars. Include session in cache keys.

## 5. Low-latency data architecture

The current bridge sends all DOM messages through one bounded outbox and one UI batch pump. Do not
inject chart history into that FIFO: priority UI draining alone cannot bypass a big packet already
in the TCP stream. Propose a separate, read-only CHART CHANNEL on127.0.0.1:36972. Keep the existing
DOM/order channel36971 and orderVersion2 unchanged. No trading commands exist in the chart protocol.

Native endpoint is a separately packaged BookDepthIQChartBridge.cs AddOn, compiled against the same
installed DLLs. It owns chart subscriptions only. Package/install steps must include BOTH AddOn files;
the current package script copies only BookDepthIQBridge.cs and must change deliberately at release.
No source replacement or running NT installation changes occur during implementation/testing.

Proposed chart protocol v1:

- hello advertises chartVersion1, capabilities, NT application timezone and session metadata.
- subscribe names a chart slot, generation ID, exact instrument, frame, requested bar count and
  session mode. State messages repeat the request identity. An old bridge/no chart endpoint gives
  "charts bridge unavailable" locally; DOM/order handling continues normally.
- bars packets contain native bar index/key, UTC timestamp/trading date, OHLC integer ticks, volume,
  forming status, chunk/sequence identifiers and complete/reset markers. Daily identity is a TRADING
  DATE, not a guessed midnight UTC timestamp. Maximum256 bars per history packet.
- Raw Last events carry instrument, price ticks, size, source timestamp, local receipt timestamp and
  sequence number. No added fixed50ms tick-sampling timer. Writer drains immediately when idle and
  groups queued events into bounded packets under load. Original ticks remain ordered inside batches.
- Native BarsRequest.Update is authoritative for session-aligned bar construction, realtime OHLC and
  corrections. Live Last events give the low-latency current-price path. Do not double-add volume to
  native OHLC bars: Last-derived provisional candle data and authoritative bar versions have distinct
  sequence/watermark state. Verify this reconciliation before claiming native-candle parity.
- unsubscribe, reset, error and snapshot-complete messages are explicit. Reconnect and every context
  change create a new generation. Late request callbacks cannot populate the wrong chart or instrument.

Cache/thread boundaries:

1. Native callback snapshots the required event fields only; no file I/O, blocking network write,
   full serialization, indicator scan or per-candle UI work on the native trading callback.
2. Chart transport worker handles encoding/read/parsing and a bounded100000-event raw-tick ring per
   linked instrument, separate from candle storage. It is a short-term replay/diagnostic buffer, not
   the historical database. Overflow/gaps are explicit and require an authoritative resnapshot;
   never silently label incomplete OHLC/PAX calculations complete. Do not claim to restore lost ticks.
3. Background chart model merges history, realtime deltas and corrections into keyed/versioned bar
   stores. Reuse immutable snapshots/buffers; apply changes by index instead of copying the whole
   history on every tick. Deduplicate equal subscriptions across both charts.
4. UI consumes at most one latest render snapshot per frame. Target30fps for CHARTS only; do not
   speed up the existing20Hz workspace timer. Use a separate render schedule without changing
   Tick()/Manage() cadence. Render only visible candles and cache axes/text/brushes. No UI controls
   per candle. Hidden charts stop rendering and unsubscribe their unshared history/bar streams.
   Temporary maximize-hidden panes retain their saved visibility but follow the same resource rule;
   restoring them requests fresh data before a live badge is shown.

Local latency measurements (no fabricated exchange timestamps): callback→enqueue, enqueue→socket,
socket→model, model→render; queue depths, gap/resnapshot count and frame duration. Distinguish feed
event time from local monotonic intervals; wall clocks across machines cannot measure microseconds.
Candidate acceptance budgets on the owner's machine: chart model p95≤5ms after local receive,
native chart callback p95≤0.5ms;0 chart-induced changes to order handling semantics. Compare DOM
UI scheduling and order-command staging p95 with charts OFF versus two charts+history ON; target
≤2ms additional local staging. Treat these as measured acceptance targets, never pre-release claims.
No remote broker latency benchmark or actual orders are required for the isolated tests.

## 6. PAX OR support and boundaries

Owner-provided PAXOR30UpdateShare.zip contains Indicators/PAX30OpeningRange.cs and Info.xml. Source
inspection: v1.1, fixed30-second OR, separate Second30 series, NT Draw.Line/Draw.Text objects,8-day
display, dynamic extensions (NQ/MNQ65 points, ES/MES15), minute/second-only validation. It is not a
plain AddPlot indicator. ZIP has a copyright header but no explicit redistribution licence; do not
bundle its source or advertise a generic third-party indicator loader.

- Use our independently maintained opening-range engine behind a chart overlay adapter, verified
  against the supplied indicator behavior; don't execute the supplied code. It is PAX-style support,
  not a guarantee that a third-party DLL was loaded or pixel-identical source rendering was reproduced.
- PAX supports ONLY1m/3m/5m/15m/30m. It is DISABLED on1h/4h/Day, including when switching from an
  enabled lower timeframe. Remember the lower-timeframe preference but render/request no PAX on HTF.
- PAX chart toggle is OFF initially. Its levels are deliberately opted in per chart. No other automatic
  DOM levels, positions, orders, IB, nPOC, GEX, SuperTrend or no-chase overlays in v1.
- Share opening-time/timezone, step sizes and midpoint preferences with existing Opening range
  settings to avoid two different OR prices. Add per-chart PAX display enable and history-day count.
  Existing default08:30 America/Chicago must be verified against the ZIP's09:30 local-time setting
  and the user's actual NT timezone; comments in third-party code are not timezone evidence.
- Current30-second OR must come from a complete native Second30 bar with a timestamp covering the
  requested opening window, or verified complete raw events in that exact window. A1-minute candle
  cannot supply a30-second range; don't infer it from an entire minute's high/low.
- Chart PAX history defaults to8 completed eligible trading sessions (setting1–8) plus current
  session. Load native30-second history separately, starting with14 calendar days and expanding to
 28 calendar days if necessary; if8 sessions are unavailable show available sessions and mark partial.
  Shared cache holds up to75000 Second30 bars during rebuild, then only bounded session summaries,
  extension activation timestamps and current-day state. No8-day raw tick replay.
- PAX disabled/hidden on both charts releases this CHART-ONLY30-second subscription; existing DOM
  OR data flow remains unchanged. Eight days of chart candles are not mandatory for eight PAX sessions.
- Historical PAX lines have per-session start/end and dynamic extension activation times. Clip to
  visible time/price range. Do not extend all prior-day levels across the whole chart. Keep labels
  restrained, off candle bodies, and omit optional labels on collision rather than overlap them.
- Our current PaxOr stores a current session and extensions but not8 sessions/activation times.
  Add a separate historical adapter/model; don't silently alter existing DOM semantics. Gap jumps,
  exact rung touches, missing opening bars, holiday sessions and DST require comparison fixtures.

## 7. Basic indicators / extensibility

V1 native chart studies: optional SMA and EMA, one configurable period each (default20), optional
volume, plus the PAX adapter above. All initially OFF. Compute on the selected chart's bars; don't
reuse3m values on15m. No unsupported "Load any NT indicator" button. One bounded overlay interface
accepts timestamps, plot values and render metadata for later supported indicators. Adding arbitrary
NinjaScript execution, vendor licensing, custom rendering and multi-series hosting is out of scope.

## 8. File responsibilities and delivery order

Detailed implementation plan follows review of this spec; these are architectural boundaries:

1. Core chart records/store/viewport math in new focused src/Core/Charts/ files. Pure session/bar
   identities, normalization, bounds, zoom/pan/follow/autoscale and history merge tests first.
2. Read-only native chart AddOn and independent chart protocol/client. Verify API signatures against
   installed DLLs and net48/C#6 compile; extend the isolated NT test doubles to exercise subscriptions,
   events and cleanup. Never start the native listener or connect to the owner's NT during tests.
3. Worker-fed chart state, reset generations, bounded queues/snapshot publication, raw tick/bar
   reconciliation and performance instrumentation. Prove no chart history enters the order stream.
4. Native CandleChartControl/ChartPane/ChartWorkspace and MainWindow.Charts partial. Settings own
   visibility, per-frame history counts/session preferences and viewport defaults. Current order
   methods must not acquire chart model locks or depend on chart readiness.
5. Historical PAX adapter and basic studies behind the chart-only overlay interface. Verify expected
   values independently and enforce HTF disable in model AND UI, not only a disabled button.
6. Headless interaction/rendering tests, burst/reconnect/resource cleanup tests, performance comparison,
   owner visual approval, then read-only native candle/PAX comparisons. Native order testing requires
   separate explicit authorization; charts cannot send a trading command.
7. Package both native AddOn files and install instructions, update README/protocol/verification,
   verify version/hash/install contents, commit/review and release only after actual gates pass.

## 9. Acceptance matrix

- All8 frames: requested bar count, sorted/deduplicated data, OHLC/time/session identity, forming
  candle replacement, finalization, provider corrections, gaps, fewer-than-requested history.
-1h/4h/Day: correct native session boundaries, settlement close semantics documented, no PAX
  subscription or overlay even if lower-frame preference was ON.
- Pan/zoom/Follow/Auto/reset: pointer anchoring, independent axes, flat prices, one candle, no bars,
  high-DPI scaling, negative coordinates, chart resize, splitters and hidden/maximized states.
- Generations: rapid3m→Day→1m changes, switch contract while requests are pending, disconnect during
  history, stale callbacks after unsubscribe, same contract reconnect, shutdown with queued packets.
- Performance:10000 retained bars per chart; at least100000 synthetic ticks per stress run with
  adversarial batching/history interleaving; tick order/high/low/volume conservation; ring overflow
  visibly invalidates/resnapshots; hidden charts have no repaint loop; resources/subscriptions return
  to baseline after100 context switches. Baseline DOM/order tests must remain green.
- PAX: first30sec only; default-off, eligible-frame opt-in, HTF disable; timezone/DST/holidays;
  eight-session visibility; gap jumps/touches/activation times; partial OR not labeled complete;
  native comparison must be recorded before claiming supplied-indicator parity.
- Visual:1280/1920/2560 physical desktop examples with100/150/200% DPI; no overlapped headers,
  buttons, axes or labels. Chart collapse restores the existing DOM. Existing trading buttons,
  account selection, tape, journal and emergency actions remain reachable.

## 10. Research and verified facts

Read-only metadata inspection of installed NinjaTrader.Core.dll confirmed BarsRequest constructors
(Instrument,int) and(Instrument,DateTime,DateTime), Bars/TradingHours/BarsPeriod, Update event,
Request(Action<BarsRequest,ErrorCode,string>), Dispose(); BarsUpdateEventArgs.MinIndex/MaxIndex and
BarsSeries; Bars.GetTime/GetOpen/GetHigh/GetLow/GetClose/GetVolume; GeneralOptions.TimeZoneInfo;
Second/Minute/Day enum members. This confirms API existence only, not runtime timing/provider behavior.

Official references checked2026-10-15:

- https://ninjatrader.com/support/helpguides/nt8/barsrequest.htm — history+live updates, full updated
  index ranges, forming last bar, unsubscribe/dispose, and indicator-input restriction.
- https://ninjatrader.com/support/helpguides/nt8/bar_types.htm — session-defined intraday bars;
  provider-defined daily bars and possible settlement closes.
- https://ninjatrader.com/es/support/helpGuides/nt8/application_timezoneinfo.htm — application timezone.
- https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering — native DrawingContext.
- https://github.com/LuxAlgo/Vela — web chart core and independent scripting engines; would require
  browser hosting and does not solve NinjaScript compatibility.
- https://github.com/tradingview/lightweight-charts — alternative web renderer, with attribution
  requirements. Neither web renderer is selected for this native-theme-focused version.

No benchmark, new native subscription, third-party source redistribution, app implementation, trading
test, or release is claimed by this research/design document. Low-latency budgets and exact native
PAX/candle parity remain acceptance work, not facts established by the mockup.

## 11. Design-stage checks and next gate

Baseline application suite:254 passed (226 core/DOM +28 isolated bridge-source tests); application
files are unchanged on this design branch. Interactive mockup uses illustrative prices, no network
market-data calls and disabled trading buttons. Browser checks exercised PAX opt-in, HTF disabled
states, chart-column collapse/restore, settings cancellation and widths1024/736/360/320 with no
horizontal overflow or JavaScript errors. This is mockup QA, not application/chart performance proof.

Review the layout and this written spec first. After approval, create the detailed implementation
plan with explicit tests/file interfaces and review its execution approach before application code.
