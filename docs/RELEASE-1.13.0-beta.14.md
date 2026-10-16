# 1.13.0-beta.14 — Session zones, clearer trades, safer ladder entry and PAX fixes

**Beta/prerelease. Test on SIM before funded use.** Cumulative beta.8–14 work; beta.11–14 were
previously feature-branch test packages, not published releases. Main application source remains
beta.10 until a separate code-merge approval; this release is built from the beta.14 tag/branch.

## Required upgrade

Replace the executable and **BOTH** packaged NinjaTrader AddOn source files:
`BookDepthIQBridge.cs` and `BookDepthIQChartBridge.cs`. Recompile in NinjaScript Editor (F5),
activate/reconnect the updated AddOns, then reload chart history. Do this when safe—not while
restarting would interrupt management of an open position. An EXE-only upgrade is insufficient.

DOM order capability3 is required by the new app; older bridges refuse new Ticket orders.
Emergency cancel/flatten remains available. The chart bridge completes the previous bar at rollover,
so cached forming flags no longer freeze PAX extension calculation. No fixed timezone offset added.
Settings/journal/account state are not reset. This release does not install itself into a running app.

## Included updates

- Two optional timeframe charts with navigation, linked crosshair and read-only order/position overlays.
- Optional Tokyo/London/New York H/L zones with named time zones, editable hours/colors, shading,
  prior sessions and extensions. Initially OFF; Day charts omit intraday zones. Partial/stale coverage shown.
- Larger high-contrast trade markers with exact quantity badges. Old default styling migrates once;
  thresholds/custom colors remain unchanged and custom styles are retained. Capture remains live-only,
  bounded and density-limited—not inferred parent orders or institutional identification.
- Manual stop and target exits have independent position-sized coverage budgets. Both placement orders,
  multiple targets, partial fills and cleanup covered by regressions. No new brackets on reductions.
- BUY/SELL are the only new-entry ladder columns, with subtle cyan/orange tint. Left-click release
  submits; scroll/drag/button chords cancel pending entry. Shift-stop, existing order drag/right-cancel,
  Ticket buttons and armed hotkeys remain. Column hover help can be disabled in Ticket → ENTRY.
- PAX follows one extension step per side per completed30second bar, strict breaks and same-day plot
  endpoints. Zero previous sessions means today only. Today/date labels distinguish prior ranges.
- Native live updates read the event series; rollover republishes the previous completed endpoint.
  No six-extension cap was found; selloff regression reaches EXT21.

## Safety and verification limits

**416 isolated tests pass:370 Core/DOM +46 Bridge**, zero failed/skipped. Release app and installed-DLL
net48/C#6 AddOn compilation clean. Actual-control synthetic render and pointer tests included.
No native SIM/live orders were submitted by the agent. Native provider/parity/reconnect/soak acceptance
remains pending; owner previously approved beta.10 testing. Do not interpret tests as HFT certification.

**Separately clicked manual exits are not broker-native OCO pairs.** Cleanup/resizing follows native
position/order events in the running bridge. Disconnects or multiple fills before cancellation can
race; no broker-level reduce-only or atomic cancellation guarantee is made. Use native OCO/ATM where
that guarantee is required, and test this workflow on native SIM before funded use.

Assets: self-contained Windows x64 ZIP with app, both AddOns and install/license/disclaimer files;
`SHA256SUMS.txt` covers the ZIP. The executable product version identifies the release commit.

[PAX rollover evidence](https://github.com/autopilotaitech/BookDepthIQ-NT/blob/v1.13.0-beta.14/docs/PAX-LIVE-ROLLOVER.md) ·
[Manual exit limits](https://github.com/autopilotaitech/BookDepthIQ-NT/blob/v1.13.0-beta.14/docs/MANUAL-EXIT-COVERAGE.md) ·
[Ladder input proof](https://github.com/autopilotaitech/BookDepthIQ-NT/blob/v1.13.0-beta.14/docs/EXPLICIT-ORDER-COLUMNS.md)
