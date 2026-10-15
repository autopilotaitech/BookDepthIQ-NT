# Chart reliability audit — 2026-10-15

Owner authorized the chart reliability/performance audit and fixes on `codex/chart-navigation-polish`.
No running app, saved user configuration, native feed, account or native order was changed.

## Confirmed bugs and regression proof

1. **Contract switch retained an unrelated manual price range.** NQ at120000 ticks, manually
   scaled119900–120100, then ES at24000 ticks left all ES candles outside the viewport. The new
   `Switching_contract_restores_visible_price_instead_of_retaining_previous_manual_scale` test failed
   before the fix. ChartPane now clears viewport/cursor when its instrument changes. A companion test
   ensures same-contract updates retain manual scale. Linked cursor clearing is also asserted.
2. **Right-edge zoom could move beyond the last candle.** For200 bars, pan to199 then zoom0.5 at
   the right edge produced Start259. The new `Zooming_at_right_edge` test failed with actual259,
   expected0–199. Zoom now applies the same right boundary as pan. Existing pointer-anchor tests pass
   for ordinary zoom; at the data boundary, retaining visible data takes priority over anchoring.

Both regressions were observed failing before production edits, then passing after minimal fixes.

## Additional audit evidence

- A real TCP test listener on an OS-assigned ephemeral localhost port exercises ChartClient, not a
  mock: handshake, initial Last, disconnect, resubscription with increased generation, cache reset,
  old-generation rejection, resumed Last/complete, equal-request sharing, and changing the shared
  pane to another instrument/timeframe. It never uses NinjaTrader ports. The test also passed three
  consecutive standalone runs.
- Settings JSON round-trip retains independent pane timeframes/lookbacks, display toggles,
  crosshair linking and split ratios.
- Actual chart menu actions exercise maximize/restore, hide both panes, DOM expansion and reenable.
  The same DOM control and saved split ratios survive.
- Existing order-overlay tests cover moves, partial fills, cancellation, flat state, account/contract
  filtering, disconnect and clearing both panes. The real isolated Ticket harness command sink stays
  empty. These are model/UI tests, not claims that broker execution was exercised.
- Existing transport stress test applies100000 raw Last packets without duplicating native volume.
  Cache bounds, stale revisions/generations and pixel-bucket extrema tests remain in the full suite.

Full suite: **302 Core/DOM +33 bridge =335 passed**, zero failed/skipped.

## Performance run

Executed the standalone desktop fixture again with10000 synthetic bars per pane at800x1000,
60 full-window raster renders: p50 **11.40ms**, p95 **14.67ms**, managed allocations **63965704 bytes**.
Screenshot: local ignored `dist/chart-audit-dense.png`.

```powershell
dotnet tools/ChartPreview/bin/Debug/net8.0/BookDepthIQ-ChartPreview.dll --snapshot dist/chart-audit-dense.png --bars 10000 --all --benchmark
```

This measures isolated rendering, not native callback-to-render latency, DOM scheduling interference,
broker latency or HFT capability. No new speed improvement is claimed for these two reliability fixes.

## Owner testing when home

- On SIM, show both charts and switch contracts after manually scaling/panning one chart.
- Pan far right, zoom in, then CENTER; check candles remain reachable and spacing is retained.
- Switch timeframes, hide/restore/maximize panes, resize dividers, restart and verify settings.
- Observe provider disconnect/reconnect, history availability and fresh price/status recovery.
- Compare chart TP/SL/position lines with NT during manual SIM management; fills/cancels/flat should
  clear the corresponding lines. Charts remain display-only; no chart drag modifies an order.
- Compare native session/Daily bars and PAX output, and observe DOM responsiveness during busy ticks.

Those native/provider observations remain pending. Automated/synthetic checks cannot certify them.
Local beta6 package is a test build; this audit does not merge main or publish a GitHub release.
