# Large executed trades — beta7 test build

Developed on `codex/chart-large-trades`; approved live-first screenshot design.
Integrated into main and published as beta7 at the owner's request; native acceptance remains pending.

![Actual Avalonia controls with synthetic trade data](images/large-trades-preview.png)

## Use

Chart menu → Chart settings → LARGE TRADES. Enable, choose minimum contracts, buy/sell/unknown
hex colors, opacity, diameter range and quantity labels. Apply to This chart or Both charts changes
pane visibility; instrument profile values are shared. Known CME roots retain preferences across
rollover; NQ/MNQ and ES/MES remain separate. Unknown instruments use exact names.

Defaults are OFF,50 contracts,20% fill,12–42 DIP, labels on markers≥24DIP. To show quantities on
every marker like the preview, select All and increase minimum diameter (preview uses32–54DIP).
No setting is a calibrated trading signal. Hover reports quantity, price/time, side and coverage.

**Upgrade required for bubbles:** replace `BookDepthIQBridge.cs` from this package and recompile NT
when safe to close/restart it. The chart AddOn is unchanged. New additive `chartMs` and `inst` fields
give the candle overlay chart-aligned timestamps and exact contract identity. Legacy `ms`, analytics,
execution timestamps and order logic are unchanged. Older bridges leave bubbles empty and explain
the upgrade in chart settings; we do not guess timestamp alignment. No installed files were changed.

## What is and is not displayed

- One bubble represents one received reported Last print, not a reconstructed parent order.
- Ask-side cyan, bid-side orange, unknown gray by default; unknown is never guessed from candle color.
- Live-only while enabled on a visible chart. Both panes consume one underlying store, not tape scans.
- At most50000 prints/60minutes of receipt coverage, whichever expires first. History is memory-only.
  Disable-all clears capture; hiding all charts stops capture. Reconnect closes/reopens coverage.
- Largest500 eligible prints are candidates; pixel-density budgeting retains the largest print per
  diameter-sized cell. Omitted counts appear in settings/hover. Quantities are never merged/summed.
- Daily and intervals without proved boundaries (first loaded bar/first bar after a session gap) omit
  markers. This conservative omission is preferable to plotting against an unrelated candle.
- Historical backfill is a future OFF-by-default opt-in feature; no nonfunctional checkbox is shipped.
- Existing account TP/SL/entry/average overlays are separate. No bubble action submits, modifies,
  cancels, flattens or otherwise manages an order.

## Verification evidence — 2026-10-15

`dotnet test --nologo -v q`: **322 Core/DOM +34 bridge =356 passed**, zero failed/skipped.
Release app build, preview tool and native AddOn net48/C#6 compile check: zero warnings/errors.

Tests cover identical-print preservation,100000-print bounded capture, stale/wrong-contract input,
disconnect coverage, side normalization, native end boundaries, missing sessions/DST/Daily omission,
inclusive thresholds, fixed diameter math,500 cap and density selection, JSON/profile isolation,
malformed preferences, Cancel/Apply semantics, color/toggle rendering,100/150/200%DPI, hover with
crosshair off, actual pan/zoom pointer events, and an empty real isolated Ticket command sink.

Regression proof: all four initial render/hover cases failed before bubble rendering was added.
The50000-print projection allocation test failed at4467784 bytes/query before bounded tuple selection;
it now passes a500000-byte ceiling. Producer and ingress time-zone tests failed before the additive
metadata correction. A configured Eastern10:30 event is tested against14:30UTC while legacy analytics
conversion is preserved; chart ingress/projection uses chartMs instead of a deliberately hour-shifted ms.

One fresh independent whole-branch review found the time-zone mismatch; corrected in one fix pass
with the regression tests and full green suite. No other blocking findings were reported.
An additional UI regression reproduced Apply/CANCEL falling below the settings viewport (Apply at
Y1422 in a590DIP dialog). The action footer is now pinned and the visibility test passes.

## Actual desktop rendering and stress measurement

Standalone Avalonia fixture, synthetic data only, no feed/account/MainWindow connections:

```powershell
dotnet build tools/ChartPreview/ChartPreview.csproj --nologo -v q
dotnet tools/ChartPreview/bin/Debug/net8.0/BookDepthIQ-ChartPreview.dll --snapshot dist/large-trades-preview-final.png --bubbles
dotnet tools/ChartPreview/bin/Debug/net8.0/BookDepthIQ-ChartPreview.dll --snapshot dist/large-trades-preview-final-2x.png --bubbles --width 600 --height 1000 --scale 2
```

Two panes,10000 bars each,50000 captured prints,800x1000 pixels,60 full-window raster renders:

| Scenario | p50 ms | p95 ms | Managed bytes/60 renders |
|---|---:|---:|---:|
| Cached scene, bubbles off |11.37|13.81|62127336|
| Cached scene, density-limited bubbles on |14.32|16.68|75045736|
| Fresh store snapshots/20 new prints per render, bubbles off |11.66|28.76|197530664|
| Fresh store snapshots/20 new prints per render, bubbles on |27.77|54.21|218699560|

The original overlapping dense renderer reached p9577.18ms; density selection reduced static repaint
cost. Fresh-data worst-case stress can still exceed33ms. The30fps setting is a throttle ceiling, not
a guaranteed frame rate. These isolated raster numbers do not certify native callback/render latency,
broker/order latency, native DOM scheduling interference or HFT capability.

## Native limits and owner check

Desktop helper initialization still fails (`failed to write kernel assets: ... path specified`, OS3).
No blind fallback input, native orders, account manipulation or installed-app replacement was used.
Owner should compile the included bridge, enable bubbles on SIM, compare large prints with tape/NT,
change timeframes/contracts and settings, check reconnect gaps and observe responsiveness during busy
ticks. Provider timestamps/real native session parity remain unverified despite compile/model tests.

## Implementation decisions

Coverage metadata is bounded to256 reconnect segments; older detailed gaps can expire first.
Unproved Daily/session mapping is omitted. Pixel density can hide smaller overlapping prints rather
than aggregate them. Additive bridge metadata was necessary despite the original app-only assumption;
this makes beta7 a DOM-bridge upgrade for bubbles. Same-contract reconnect generation identity is not
provided by the market stream and is not certified; coverage is explicitly segmented. Historical mode
remains deferred. No minor review findings were deferred. Windows task bookkeeping used a local
ignored ledger rather than Bash helpers; product behavior is unaffected.
