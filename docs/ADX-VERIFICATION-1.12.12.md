# ADX market state — 1.12.12

Approved defaults: 3-minute timeframe, ADX period 14, trend threshold 25. Options 1/3/5/10/15/30m;
no 60m. Period and threshold editable in Indicators settings, shared across instruments like the
existing indicators. Preserve existing theme and badge location. No extra status row or prediction.

## Calculation and data contract

The pure DOM-side calculation follows NT8's installed `Indicators/@ADX.cs`: first ADX value 50,
running TR/+DM/−DM sums before Period, Wilder smoothing thereafter, strict directional movement
comparison, and zero-DI fallback 50. Only closed, traded minute buckets are included. DI supplies
direction; ADX supplies strength. A zero/equal DI reading is CHOP, not a directional trend.
The bar clock is epoch-aligned; selected minute lengths divide an hour. This is not a native ADX
indicator instance. Equal scores require equal bars and history seeds. Custom chart sessions,
different history starts and forming-candle readings may differ.

The selected-frame cache is separate from the existing four-hour MinuteBars scalp/swing cache;
it holds at most 4096 aggregate OHLC bars, not a per-trade log. Timestamp ordering protects the close
when old history arrives after live prints. No synthetic bars during gaps. Closed-bar snapshots are
cached until the bar boundary or a closed candle changes. MarketView suppresses scores during
history loading, stale/disconnected feeds, and until at least 2*Period+1 complete traded bars exist.

The requested history is max(user hours, ceil((5*Period+2)*Minutes/60)+2), capped at 48h, using the
existing history protocol. Default 3m/14 uses the saved 8h; 30m/14 requests 38h. Larger periods and
weekends/provider gaps may not warm from 48h alone. Additional live bars are needed then; ADX —
is intentional, never substitute another timeframe or a partial score. History requests on connect,
settings save and instrument switch all use the same effective duration. Settings save/instrument
subscription rebuilds the cache. No order protocol, bridge, risk, scalp or management change.

## Automated and visual checks

248 isolated tests: 220 core/DOM, 28 bridge source. No native orders or appdata changes:

- Numeric mixed OHLC fixture: period3 ADX 39.554979002180104, +DI 7.500228665508097,
  −DI 17.60724412329644; checked against NT initialization/smoothing.
- Closed-form monotonic fixtures for periods1/14/100, flat/zero-DI fallback.
- All six timeframes, forming candle exclusion, no invented gap bars, late history ordering,
  closed-cache invalidation/reuse and 4096-bar storage bound.
- 30m/14 retains over four hours; requests expand/cap safely.
- Strength vs direction, threshold changes, cold/stale/disconnected display, analytics reset.
- Settings defaults/migration/JSON roundtrip, actual timeframe/period/threshold controls, Cancel.
- Existing badge renders score/arrow/EXTENDED and clears stale score; no outgoing orders.
- Headless settings render inspected for theme, readability and control layout.

## Native comparison still required

On a read-only SIM/feed setup, compare completed bars to NT ADX using the same instrument,
timeframe, period, trading hours, OHLC and history seed. Check all timeframes, threshold edits,
disconnect/reconnect and instrument switch. Confirm history gaps show ADX — rather than a stale
score. No native chart parity or Ticket SIM certification is claimed by the automated suite.
