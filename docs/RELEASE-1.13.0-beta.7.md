# 1.13.0-beta.7 — Large trade bubbles, chart orders and navigation

**Beta/prerelease. Test on SIM before live use.** This includes the previously unreleased beta4–6
chart work plus beta7 live-trade bubbles. No trading orders were sent during agent verification.

## Upgrade — important

**Replace the executable AND `NinjaTrader/BookDepthIQBridge.cs`, then recompile NinjaTrader.**
The DOM bridge now adds exact-contract and chart-specific UTC timestamps to live print packets.
This fixes disagreement when NinjaTrader's configured time zone differs from Windows. Existing
analytics/execution timestamps and order behavior are unchanged. Older bridges still serve existing
functions but intentionally do not draw trade bubbles; chart settings explains the upgrade.

- From beta2 or newer: the separate `BookDepthIQChartBridge.cs` is unchanged.
- From beta1/1.12.x/new install: install BOTH packaged AddOns and compile with F5. Chart port36974
  remains separate from DOM/orders36971 and the unrelated Bubbles36972 AddOn. Leave Bubbles alone.
- Close NT safely before replacing source; this release does not modify a running installation.
  User settings, journal history and account state are not reset.

## New and improved

- Optional large executed-trade bubbles on intraday candle charts. Per-instrument contract thresholds,
  buy/sell/unknown colors, opacity, diameter and quantity labels. Find LARGE TRADES in chart settings;
  disabled initially. Apply/Cancel remain pinned while preferences scroll.
- One marker represents one reported print, not an inferred parent/institutional order. Capture is
  bounded to50000 prints/60minutes; shared between both charts. Density limits keep larger prints
  visible without summing hidden quantities. Settings/hover expose retained coverage and omissions.
- Display-only working entries, TP/SL and average position on both charts. Remaining quantities,
  offscreen arrows and account/contract filtering; no chart order entry, movement or cancellation.
- Tick-aligned current-price line/tag, crosshair price/time/OHLCV, optional linked crosshairs and
  CENTER/Space preserving horizontal zoom. Price-axis scaling/panning, LIVE/End, AUTO and Home/reset.
- Visual preferences apply without history reload; explicit Reload history remains in the chart menu.
- Fixes manual price scale surviving a contract switch and right-edge zoom moving beyond all candles.
- High-DPI label fix and bounded dense candle/study rendering preserving OHLCV and study extrema.

## Verification and limits

**356 isolated tests pass:322 Core/DOM +34 bridge-source**, zero failed/skipped. Release app and NT
AddOn net48/C#6 compile checks pass. Actual Avalonia controls rendered with synthetic data at normal
and high DPI. Independent review identified the timestamp mismatch; regression tests prove correction.

Historical bubble backfill is NOT included; it is planned as an opt-in follow-up using real tick data.
Daily/unproved first/session-gap intervals omit markers instead of guessing. Unknown side remains
neutral. Default thresholds are illustrative, not calibrated trading signals. Fully persistent manual
viewports, raw-tick replay diagnostics and detailed native scheduling telemetry remain deferred.

Native desktop-control initialization failed, so native provider/session parity, live reconnect
observation and runtime feed/DOM latency are not certified. Dense fresh-data synthetic raster stress
can exceed33ms;30fps is a throttle ceiling, not an HFT guarantee. No native trading execution testing
or claim of institutional identification is made.

[Actual preview, controls and measured evidence](https://github.com/autopilotaitech/BookDepthIQ-NT/blob/main/docs/LARGE-TRADES-VERIFICATION.md)
· [Full chart controls](https://github.com/autopilotaitech/BookDepthIQ-NT/blob/main/docs/CHART-WORKSPACE.md).

Assets: self-contained Windows x64 ZIP (app, both AddOn sources, install/license/disclaimer files)
and `SHA256SUMS.txt`. SHA256 covers the ZIP; the EXE product version records the release commit.
