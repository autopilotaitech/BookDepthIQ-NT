# 1.13.0-beta.3 — Native two-chart workspace and compact telemetry

Still beta/prerelease. Owner reports charts and the compact UI working; further testing continues.

## Changes

- Two optional native candle charts left of the existing DOM, default 3m/15m, same selected contract.
- Independent 1/3/5/15/30-minute, 1-hour, 4-hour and Day frames. Saved draggable workspace/height proportions,
  chart collapse/hide/maximize, pan/zoom, LIVE/Follow and AUTO visible-candle scaling.
- Per-frame bounded lookback of 250–10,000 candles; optional volume, SMA/EMA and intraday PAX-style OR.
  PAX disabled on 1h/4h/Day; no arbitrary vendor-indicator loading or chart order entry.
- Compact position/account row and market row, preserving existing readouts, number sizes and tooltips.
- Dedicated read-only chart port 36974, separate from DOM/orders 36971 and Bubbles 36972. Fixes beta1's
  Bubbles port collision; chartVersion 1 handshake is mandatory before any subscriptions are sent.
- Generation/revision guards, shared equal requests, explicit reload, bounded caches and chart-only
  repaint scheduling. Manual history view remains anchored when old bars are evicted.

## Upgrade

- **From 1.13.0-beta.2:** close DOM and replace the executable. Chart bridge unchanged; no NT recompile needed.
- **From beta1:** replace executable AND `BookDepthIQChartBridge.cs`, then recompile in NT with F5.
  App-only upgrade does not fix the port collision.
- **From 1.12.x/new install:** copy both packaged AddOns into NT's Custom/AddOns directory while NT is
  closed, then start NT and compile with F5. Existing DOM/order source is unchanged from the current
  compatible 1.12.x bridge; the chart AddOn is new. Leave the separate Bubbles AddOn alone.
- Charts are initially off. Click CHARTS and widen/maximize the window if the readable-width guard
  temporarily hides them. Existing user settings and recorded journal data are not reset.

## Verification

289 isolated tests pass: 256 core/DOM and 33 bridge-source. Native AddOns compile against installed
NinjaTrader DLLs under net48/C#6. Release executable/package builds successfully. Tests include
wrong-bridge rejection without sending commands, native history chunking, stale generations,
reload/unsubscribe, cache bounds, 100,000 synthetic Last messages without volume duplication,
DST OR endpoints, missing opening windows, rendered study changes and compact telemetry widths.

Not claimed: native broker execution testing, full supplied-PAX parity, all provider reconnect/session
and daily settlement cases, HFT certification or measured latency improvements. No native orders were
submitted by the agent. Crosshair linking, full persistent manual viewport state, raw-tick replay ring
and detailed latency telemetry remain outstanding. OR uses bounded 28-day native 30-second history;
historical extension activation is 30-second-bar resolution, not exact-tick parity.

Package includes self-contained Windows x64 app, both AddOn sources, install/license/disclaimer files.
Release assets include `SHA256SUMS.txt`. [Full controls and limits](CHART-WORKSPACE.md).
