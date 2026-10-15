# Two-chart workspace implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two optional native candle charts to the left of the unchanged DOM, with saved draggable proportions and independent timeframes.

**Architecture:** Wrap the existing DOM control tree rather than recreate it. A separate read-only NT chart AddOn and background chart client own bounded candle caches; chart history never enters the order connection. Native bars remain authoritative, with raw Last events used for provisional current-price rendering only.

**Tech Stack:** .NET 8, Avalonia 11.2.3; NT AddOn .NET Framework 4.8 / C#6.

**Spec:** `docs/superpowers/specs/2026-10-15-two-chart-workspace-design.md`

## Global constraints

- Work on `codex/two-chart-workspace`; preserve the existing DOM and order semantics.
- Initial charts32% / DOM68%; independent horizontal chart-height split50/50. Persist both.
- Exact frames1m/3m/5m/15m/30m/1h/4h/Day; PAX disabled on1h/4h/Day in model and UI.
- Maximum10000 bars/chart,20000 across slots; raw ring100000 events/instrument.
- Chart channel127.0.0.1:36972, version1; existing order channel36971/orderVersion2 unchanged.
- No live installation changes, orders, third-party source redistribution, or release publication during implementation.
- Run `dotnet test --nologo -v q` before every commit. Baseline254 passed on2026-10-15.

## Review focus

- Late native callbacks after contract/timeframe changes must not repopulate a different chart.
- Corrections and Last events must not double-count volume or overwrite authoritative OHLC.
- Missing opening windows and DST must not produce apparently complete PAX ranges.
- Hidden/maximized charts and reconnects must return resources to baseline.
- Narrow windows and corrupt saved proportions must not hide emergency trading controls.

## Task 1: Bounded candle model and viewport

**Files:** create `src/Core/Charts/ChartFrame.cs`, `ChartBar.cs`, `ChartStore.cs`, `ChartViewport.cs`; test `tests/Core.Tests/ChartModelTests.cs`, `ChartViewportTests.cs`.

**Interfaces:** `ChartFrame` owns supported choices/default counts/PAX eligibility. `ChartBar` owns native index, UTC time, trading date, integer-tick OHLC, volume, revision and forming flag. `ChartStore.Reset(long generation)` and `Apply(long generation, ChartBar bar)` reject stale generations/revisions; `Snapshot()` returns ordered bounded bars. `ChartViewport` owns visible range, pointer-anchored zoom, pan, independent Follow/Auto and flat-price padding.

- [ ] Write failing xUnit fixtures for out-of-order overlapping chunks, repeated revisions, corrections, generation reset and10001 appends retaining10000; verify `Assert.Equal(10000, store.Snapshot().Count)` and stale apply leaves snapshot unchanged.
- [ ] Run `dotnet test --filter ChartModelTests --nologo -v q`; expect failures before implementation.
- [ ] Implement keyed merge/eviction without whole-history copies per tick; maintain daily trading-date identity independently of intraday timestamp.
- [ ] Write/run failing viewport fixtures: empty/single/flat/negative prices, zoom pointer anchor, pan disabling Follow only, resize and oldest-bar eviction. Implement pure arithmetic, then verify both suites pass.
- [ ] Run full suite; commit model and tests.

## Task 2: Isolated native chart protocol

**Files:** create `nt8/AddOns/BookDepthIQChartBridge.cs`, `src/Core/Charts/ChartProtocol.cs`, `tests/Bridge.Tests/ChartBridgeTests.cs`; extend `tests/Bridge.Tests/NinjaTraderSimulation.cs`, `tests/Bridge.Tests/Bridge.Tests.csproj`, `nt8/BridgeCheck/BridgeCheck.csproj`.

**Interfaces:** newline JSON hello/subscribe/unsubscribe/reset/bars/last/error/complete; every response identifies slot/generation/instrument/frame/session. Bars chunks contain at most256 records. Protocol accepts no trading commands. AddOn lifecycle starts/stops only when NT itself activates it; tests invoke isolated handlers with in-memory output.

- [ ] Verify installed BarsRequest, TradingHours and market-data metadata before using members; retain C#6 compilation.
- [ ] Write failing isolated tests for unsupported requests, stale callbacks, complete updated-index ranges, duplicate request sharing, disposal and100 context switches. Assert no orders created in the simulation.
- [ ] Implement subscription ownership, bounded worker outbox and immediate idle drain; callbacks only snapshot fields. On overflow mark a gap and require fresh authoritative history rather than silently dropping correctness.
- [ ] Test native-timezone conversion/session identities and250/10000 count bounds. Verify hello has explicit capability/version and errors are local to charts.
- [ ] Run `dotnet test --nologo -v q` and `dotnet build nt8/BridgeCheck/BridgeCheck.csproj --nologo -v q`; expect success; commit.

## Task 3: Background client and shared caches

**Files:** create `src/Dom/Charts/ChartClient.cs`, `ChartCoordinator.cs`, `ChartSnapshot.cs`; test `tests/Core.Tests/ChartTransportTests.cs`.

**Interfaces:** coordinator consumes ChartProtocol records, owns ChartStore per deduplicated request key, publishes immutable render snapshots, and exposes subscribe/unsubscribe/dispose. Client owns only port36972; no reference to order sending APIs. Each reconnect increments generations.

- [ ] Write failing tests with injected in-memory transport: rapid3m→Day→1m, contract change mid-history, disconnect/reconnect, malformed/oversized packet, cancellation during read and disposal with queued work.
- [ ] Implement bounded decoding/worker application and raw ring; raw Last changes provisional display but never increments authoritative native volume. Add sequence/revision fixtures that interleave ticks and native corrections.
- [ ] Stress100000 synthetic ticks and two10000-bar histories. Assert ordered extrema/volume, explicit overflow state, bounded storage and no chart messages on the existing bridge stream.
- [ ] Record local timing and queue-depth measurements without claiming external feed latency; run full suite and commit.

## Task 4: Native rendering and unchanged DOM wrapper

**Files:** create `src/Dom/Charts/CandleChartControl.cs`, `ChartPane.cs`, `ChartWorkspace.cs`, `ChartSettingsWindow.cs`, `src/Dom/MainWindow.Charts.cs`; modify `src/Dom/MainWindow.cs`, `src/Dom/Settings.cs`; test `tests/Core.Tests/ChartWorkspaceTests.cs`, `ChartInteractionTests.cs`.

**Interfaces:** workspace receives existing DOM root as one child on RIGHT, chart coordinator and validated chart settings. Pane owns timeframe/menu; renderer consumes snapshots/viewport only. No trading handlers attach to chart controls.

- [ ] Write failing headless tests for32/68 default, persisted drag ratios, existing SideWidth independence, collapse/restore, individual Hide/Maximize and width1390 fallback. Invalid saved ratios normalize without overwriting valid preference during temporary narrow layout.
- [ ] Wrap existing Build result; do not reconstruct header/strips/ladder/tape/ticket. Add CHARTS toggle and independent splitters. Defaults top3m/bottom15m, charts initially off for existing users.
- [ ] Implement native candle rendering using existing Pal/Flight tokens, visible-candle culling, cached text/brushes and chart-only30fps schedule. Hidden panes stop rendering/unshared subscriptions; existing20Hz Tick/Manage is unchanged.
- [ ] Write/run failing pointer and keyboard tests for wheel anchoring, drag pan, price-axis scale, double-click reset, Follow/Auto independence and crosshair time linking; implement these interactions.
- [ ] Add per-frame lookback defaults and250–10000 custom validation, reload and coverage/status display. Settings cancel must leave live preferences unchanged.
- [ ] Render isolated previews at1280/1920/2560 and100/150/200%DPI; inspect axes, clipping and all trading controls. Run full suite, commit; request owner visual inspection before release.

## Task 5: Historical opening-range overlay and basic studies

**Files:** create `src/Core/Charts/ChartStudies.cs`, `ChartOpeningRanges.cs`, `src/Dom/Charts/ChartOverlay.cs`; extend chart bridge/coordinator/settings; test `tests/Core.Tests/ChartStudyTests.cs`, `ChartOpeningRangeTests.cs`.

**Interfaces:** studies consume selected-frame ChartBar snapshots; historical OR adapter consumes verified Second30 bars plus existing DOM opening-range settings. Overlay records carry per-session endpoints and extension activation timestamps; no DOM engine behavior changes.

- [ ] Write failing fixtures for SMA/EMA period20, forming corrections, insufficient samples; implement optional initially-off SMA/EMA/volume.
- [ ] Write failing OR fixtures for exact30sec window, missing/partial data, DST/holiday, gap jumps/exact touches and8-session retention. Verify HTF never requests or renders PAX while preserving saved LTF preference.
- [ ] Implement separate bounded Second30 history:14calendar days, expand28max,75000 temporary bars then retain summaries. Share time/step/midpoint preferences with existing DOM settings.
- [ ] Clip historical levels per session and avoid label collisions. Document PAX-style adapter rather than arbitrary vendor-DLL compatibility; do not copy ZIP source.
- [ ] Run full suite/native compile and commit. Record actual read-only NT candle/PAX comparison separately before claiming parity; no order testing is authorized by this task.

## Task 6: Packaging, performance and whole-branch verification

**Files:** modify `tools/package.ps1`, `README.md`, protocol/verification documentation; add chart tests to existing isolated harnesses.

- [ ] Verify package includes both AddOns and install instructions; use repository version conventions without publishing an unverified release. Test archive manifest/checksums against actual contents.
- [ ] Compare charts OFF versus two visible charts with history burst: model p95 target≤5ms, native callback≤0.5ms, additional local DOM/order staging≤2ms. Report misses, not inferred passes. Use synthetic order staging only, never submit orders.
- [ ] Verify100 context switches return subscriptions/resources to baseline; shutdown/reconnect/bridge-unavailable must leave DOM operational.
- [ ] Run full test suite, Release build, native BridgeCheck, diff whitespace checks and visual fixtures. Record exact counts/commands and distinguish isolated checks from native provider comparisons.
- [ ] Perform fresh whole-branch review under requesting-code-review; reproduce important findings with failing tests before fixes. Keep feature branch unmerged/unpublished until owner review and native comparison gates are satisfied.

## Plan self-review

Coverage: spec sections1–3→Task4; section4→Tasks1/2/4; section5→Tasks2/3/6;
section6→Task5; section7→Task5; delivery/testing/release→Task6. Review-focus conditions are
assigned explicitly to Tasks1–6. No application implementation has started at plan creation.
