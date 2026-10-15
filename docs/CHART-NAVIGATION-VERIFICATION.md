# Chart navigation — beta5 development verification

Scope: improve the existing candle charts, keeping the DOM primary and the existing theme.
Branch: `codex/chart-navigation-polish`, based on beta4 display-only chart orders.
The installed app, native bridge and published beta3 release are unchanged.

## Behavior

- Raw Last integer ticks drive both current-price line and tag; native OHLCV remains authoritative.
- Instrument tick precision replaces fixed two-decimal labels. Grid steps align to valid ticks.
- Crosshair has price/time badges and native candle OHLCV; snapping is optional.
- Optional linked cursors map intraday time to the containing native bar and Daily to native trading date.
- CENTER/Space preserves time zoom, follows live bars and recenters price. LIVE/End follows;
  AUTO restores visible-candle autoscale; Home resets. Manual scale persists until Auto/Center.
- Plot drag pans time; wheel zooms around pointer. Price-axis drag/wheel scales; Shift-drag pans
  price. Shift-wheel scales price over the plot. Click the price tag to recenter.
- Existing emergency trading keys bubble unchanged. Chart interactions never send orders.
- Visual settings Apply does not reload history. Reload history is explicit in the chart menu.
- Text cache is bounded to256 entries per pane. Dense visual buckets preserve OHLCV and study
  extrema without modifying cached native data or analytics.

## Automated evidence (2026-10-15)

`dotnet test --nologo -v q`: 296 Core/DOM tests +33 bridge tests, zero failed/skipped.
Release app build, native AddOn net48/C#6 compile check and ChartPreview build: zero warnings/errors.
Tests include actual pointer/wheel/key events, right-click menu, settings Apply, both cursor mappings,
price-line pixel probes, offscreen/empty/negative ranges, minimum-width controls and dense-data extrema.

Regression checks: substituting candle close for raw Last makes the price-line test fail. High-DPI
tag tests reproduced missing glyphs at150%/200%; drawing axis labels before the nested plot clip
fixes the issue. These tests are retained.

## Actual isolated desktop execution

The standalone fixture renders real Avalonia controls with synthetic candles, studies and order
labels. It does not construct MainWindow, account state, bridges or feed clients. No native orders
or connections are made. Screenshots were inspected at800x1000 and320x800 logical pixels (200%DPI).

```powershell
dotnet build tools/ChartPreview/ChartPreview.csproj --nologo -v q
dotnet tools/ChartPreview/bin/Debug/net8.0/BookDepthIQ-ChartPreview.dll --snapshot dist/chart-polish-final.png --cursor --benchmark
dotnet tools/ChartPreview/bin/Debug/net8.0/BookDepthIQ-ChartPreview.dll --snapshot dist/chart-polish-final-2x.png --width 320 --height 800 --scale 2 --cursor --center
```

Synthetic two-pane full-window raster benchmark,60 renders,800x1000:

| Visible data | Implementation | p50 ms | p95 ms | Managed allocations/60 |
|---|---|---:|---:|---:|
| 10000 bars/pane | Before visual buckets |68.70|78.74|1556081840 bytes|
| 10000 bars/pane | Candle + study buckets |11.35|12.86|64262648 bytes|
| Normal view | Final implementation |5.27|6.61|29936792 bytes|

These are local isolated raster measurements, not native feed latency, order latency, an HFT
certification or a guarantee on other hardware. Native provider reconnect, bar/session parity,
live chart observation and callback-to-render/DOM scheduling measurements remain owner-test items.
No claims of native SIM execution or completed live trading tests are made.
