# Beta.9 — live candle/session update correction

The owner screenshot on October 16 showed the current price around 30110 while the newest
candle remained near 30200, with STALE session ranges. Previous synthetic render tests did not
prove native runtime parity. They missed the distinct live-series path.

The chart bridge's Update callback used event indices but read `request.Bars` (history).
It now reads `BarsUpdateEventArgs.BarsSeries`, including count, time, OHLC and volume.
The installed NinjaTrader DLL metadata and official BarsRequest example establish this API.
No fixed timezone offset was added, and order/account code is unchanged.

Regression evidence: a frozen one-row historical snapshot plus a separate two-row live series
previously emitted no new bar. Three tests (3m, 15m, shared 30-second source) failed before the fix
and pass afterward. A second regression failed because current Last ticks could leave stale bars
labeled LIVE; it now checks authoritative bar-end times independently. Future endpoints beyond
one frame plus 90 seconds are TIME MISMATCH; endpoints older than 90 seconds are STALE.
Time-based checks do not rewrite bar timestamps, fabricate candles or trigger trading commands.
Daily charts retain their existing status behavior because trading-date stamps are not intraday clocks.

Full suite: 389 passed (352 Core/DOM +37 Bridge), no failures/skips. Installed-DLL net48/C#6 bridge
compile succeeds. This is isolated regression/build evidence, not a claim that the owner's running
instance has already been repaired or live-provider parity proven.

## Apply the correction

1. Close the DOM and NinjaTrader when safe; do not interrupt management of an open position.
2. Replace the installed `BookDepthIQChartBridge.cs` with the beta.9 package's
   `NinjaTrader/BookDepthIQChartBridge.cs`, then start NT and compile in NinjaScript Editor (F5).
3. Run the beta.9 app. Open charts and compare the newest candle's full date/time and OHLC with
   the matching native NT chart. Check both panes and developing London/session H/L.
4. Reconnect/reload history and verify candles continue advancing. Live-price movement alone
   does not establish live candle updates. Closed/quiet markets can legitimately show STALE.

Installed app/AddOn files were not replaced and no orders were sent. Desktop inspection remains
blocked by the computer-use helper's OS error 3. Native acceptance is still required after upgrade.

Reference: https://static.ninjatrader.com/support/helpGuides/nt8/barsrequest.htm
