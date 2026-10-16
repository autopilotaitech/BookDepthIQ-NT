# Session Zones Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for inline execution, or superpowers:subagent-driven-development if selected by the owner. Steps use checkbox syntax for tracking.

**Goal:** Deliver the approved Tokyo/London/New York range-box design, verified before branch push.

**Architecture:** Calculate immutable UTC session ranges from shared slot2 native30second bars, off
the UI thread. Persist editable definitions and per-pane visibility; paint clipped range rectangles
under candles without changing DOM, trading actions or autoscale. Reuse the existing chart socket.

**Tech Stack:** .NET8, Avalonia11.2.3, C#, xUnit/Avalonia.Headless; existing net48/C#6 AddOns unchanged.

**Spec:** `docs/superpowers/specs/2026-10-15-session-zones-design.md`

## Global Constraints

- No orders, cancellations, modifications, flattening, account changes or trading hotkeys.
- Feature branch `codex/chart-session-zones`; leave main/published beta7 untouched.
- Settings open separately; no permanent settings panel or new toolbar banner. Preserve theme/DOM.
- Source bound28calendar days/75000 bars; maximum27 rectangles. No30Hz full-history rescans.
- Session feature initially OFF; default8% fill,3 completed windows per session, configurable1–8.
- Time windows have named zones, HH:mm boundaries and independently converted UTC endpoints.
- Invalid DST windows are omitted with an explanation, not assigned guessed offsets.
- Full `dotnet test --nologo -v q` before every commit; current baseline356 tests.

## Review Focus

- A30second source bar on an exact opening/closing boundary must not leak outside prices (Task1).
- London and New York change DST on different dates; fixed-offset assumptions must fail tests (Task1).
- Slow old-contract/config tasks completing after a switch must never repaint stale zones (Task3).
- Disabling sessions while PAX stays enabled must retain the shared source, and vice versa (Task3).
- Dense session labels must not appear at an incorrect price or cover critical chart tags (Task4).

## File structure and shared interfaces

Create `src/Core/Charts/ChartSessionZones.cs`: validated session definitions, named-zone resolution,
window enumeration and native-bar aggregation. No Avalonia or account dependencies.
Create `src/Dom/Charts/SessionZoneSettings.cs`: display preferences and deep-draft persistence helpers.
Create `src/Dom/Charts/SessionZoneSettingsPanel.cs`: validated dialog fields and deferred Apply.
Create `src/Dom/Charts/SessionZoneWorker.cs`: versioned background aggregation and stale-result rejection.
Create `src/Dom/Charts/CandleChartControl.Sessions.cs`: clipped rendering, time mapping, hover facts.
Modify `ChartWorkspaceSettings.cs`, `ChartSettingsWindow.cs`, `ChartPane.cs`, `ChartWorkspace.cs`,
`MainWindow.Charts.cs`, `CandleChartControl.cs` and `.Rendering.cs` only at integration boundaries.
Modify `tools/ChartPreview/Program.cs` for deterministic actual-control previews/benchmarks.
Tests: `ChartSessionZoneTests.cs`, `SessionZoneSettingsTests.cs`, `SessionZoneIntegrationTests.cs`,
`SessionZoneRenderTests.cs` under `tests/Core.Tests`.

Core records:
```csharp
public sealed record SessionDefinition(string Id,string Name,bool Enabled,string Color,
    string Start,string End,string TimeZoneId);
public sealed record SessionZone(string Id,string Name,DateTimeOffset Start,DateTimeOffset End,
    DateTimeOffset Progress,DateTimeOffset ExtendUntil,long High,long Low,string State,string Color);
public sealed record SessionZoneSnapshot(IReadOnlyList<SessionZone> Zones,IReadOnlyList<string> Notes);
```
`ChartSessionZones.Build(IReadOnlyList<ChartBar> bars, IReadOnlyList<SessionDefinition> definitions,
int previous, DateTimeOffset now, bool sourceLive)` returns `SessionZoneSnapshot`.
`SessionZoneSettings` owns Enabled, Opacity, ShowLabels, ExtendLevels, Previous and Definitions.
Workspace owns one SessionZoneSettings; each pane owns ShowSessionZones. Both are required to render.

### Task1: Accurate session calculations

**Files:** new core model + `tests/Core.Tests/ChartSessionZoneTests.cs`.
**Consumes:** native `ChartBar` with UTC end timestamps. **Produces:** above immutable snapshots.

- [ ] Write a literal boundary test before implementing the core:
```csharp
var start=new DateTimeOffset(2026,10,15,8,0,0,TimeSpan.Zero);
var definition=new SessionDefinition("london","LONDON",true,"#00BBD6","08:00","08:01","UTC");
ChartBar B(int seconds,long high,long low)=>new(seconds,start.AddSeconds(seconds),new(2026,10,15),100,high,low,100,1,1,false);
var result=ChartSessionZones.Build(new[]{B(0,999,-999),B(30,110,90),B(60,120,80),B(90,999,-999)},new[]{definition},3,start.AddMinutes(2),true);
Assert.Equal(120,Assert.Single(result.Zones).High);Assert.Equal(80,result.Zones[0].Low);
```
- [ ] Run `dotnet test tests/Core.Tests --filter ChartSessionZoneTests --nologo -v q`; observe RED.
- [ ] Implement zone resolution without local fallback. Resolve IANA/Windows aliases with existing
  .NET APIs; independently convert start/end. Overnight end uses next local date. Reject equal times,
  malformed zones and invalid/ambiguous DST endpoints with notes. Build only sessions containing data.
- [ ] Add literal London/NY/Tokyo tests around2026 March8/March29 and October25/November1, overnight
  windows, overlaps, negative prices, empty data and configured previous limits. Assert UTC instants,
  not expected values calculated by the same window helper.
- [ ] Aggregate whole30second intervals only. Include valid forming data only in developing windows;
  mark partial/stale conservatively. Test truncated starts, stale source, missing end coverage,
  developing progress and late native revision. Confirm later bars never modify earlier windows.
- [ ] Run targeted/full suite, then commit core+tests.

### Task2: Saved preferences and settings UI

**Files:** SessionZoneSettings, SessionZoneSettingsPanel, workspace/pane preferences, dialog integration;
test `SessionZoneSettingsTests.cs` with Fact and AvaloniaFact cases.
**Consumes:** SessionDefinition. **Produces:** validated deep-cloned settings, per-pane display flags.

- [ ] Write failing JSON/default/normalization tests: three independent definitions, stable colors/hours,
  master/pane OFF initially,8% opacity,3 previous, range clamps, invalid colors/times/zone handling.
  Example: change London color in a draft; assert original remains `#00BBD6` until Apply.
- [ ] Run targeted tests RED. Implement deep draft/validation; never use shallow shared definition lists.
  Defaults: Tokyo09–15 Asia/Tokyo; London08–16 Europe/London; NY09:30–16 America/New_York, labeled custom.
- [ ] Add SESSION ZONES section to existing dialog: master, individual toggles/color inputs, per-session
  start/end and zone selection, opacity, labels, extensions, previous count and This/Both charts.
  Keep Apply/CANCEL pinned. Session settings are shared; target selector changes only pane visibility.
- [ ] Test actual controls: invalid zone/equal hours blocks Apply, Cancel changes nothing, independent
  toggle/color edits survive JSON, Both/This affects intended panes and does not reload candle history.
  Include UI coverage notes and Day-chart explanation. Full suite; commit settings+tests.

### Task3: Shared native source and background lifecycle

**Files:** SessionZoneWorker, ChartWorkspace, MainWindow.Charts, pane integration;
test `SessionZoneIntegrationTests.cs`.
**Consumes:** `ChartCoordinator.Snapshot(2)`, definitions and pane activation.
**Produces:** `Task<SessionZoneSnapshot>` results guarded by exact instrument/config/generation/version.

- [ ] Write failing source-request cases using the existing isolated ChartClient test listener pattern:
  sessions-only adds slot2/frame-30/count75000; sessions+PAX still emits one subscription; disabling one
  retains it while the other needs it; disabling both unsubscribes it. No native endpoint is used.
- [ ] Extract a pure desired-request builder if needed so MainWindow wiring is behavior-tested without
  constructing native clients. Use the same builder from production RefreshCharts.
- [ ] Implement one background worker with immutable job keys, injected calculator for deterministic
  completion-order tests, bounded one running +latest pending calculation, exception-to-status result.
  Source/config/contract version changes schedule work; redraw alone does not. Dispose invalidates jobs.
- [ ] Add deterministic TaskCompletionSource tests: jobA blocked, switch instrument/config/hide/dispose,
  releaseA; assert old result never reaches either pane. Assert source errors surface as notes.
- [ ] Add a calculation-count test:100 idle refreshes call the calculator once; source revision change
  triggers a fresh result. Clock changes schedule only when a window boundary/status requires update.
- [ ] Verify source30second freshness and coverage are separate from chart-native bars and PAX state.
  Run targeted/full suite and native compile check; commit integration+tests.

### Task4: Render approved boxes, labels and extensions

**Files:** CandleChartControl.Sessions, control/rendering integration, SessionZoneRenderTests.
**Consumes:** immutable SessionZoneSnapshot/settings. **Produces:** clipped read-only chart layer/hover.

- [ ] Add failing image tests: an identical chart with zones OFF/ON differs in plot pixels; geometry
  Low/High remains equal; current-price/axis pixels remain unchanged. Repeat at1/1.5/2 scaling.
- [ ] Implement time-boundary x mapping through native end-stamped candle intervals, interpolating
  within a proved interval but not across missing bars. Intraday through4h uses shared precise highs/lows;
  Day returns no rectangles and settings explanation. Do not substitute source prices from pane bars.
- [ ] Draw low-opacity fills beneath candles; edges/labels below order/crosshair/current-price overlays.
  Keep existing high-DPI rule: axis glyphs before nested plot clip. Extension ends at next same-session
  start. Rectangle ends at progress for active windows. Show PARTIAL/STALE visibly and in hover.
- [ ] Test bounded label collision handling: if labels cannot fit at true price, omit rather than shift
  them to false prices; hover still identifies exact range/status/time window. Test dense/narrow panes,
  gap clipping, overlapping sessions and offscreen values. No overlap management may send commands.
- [ ] Use real pointer pan/zoom/recenter tests and isolated Ticket harness with empty command sink.
  Run targeted/full suite; commit renderer+tests.

### Task5: Proof, review and delivery

**Files:** ChartPreview tool, actual preview PNG, `docs/SESSION-ZONES-VERIFICATION.md`, README feature note.

- [ ] Add `--sessions` deterministic fixture using three actual calculated ranges and custom UTC sample
  definitions so displayed ranges/time labels agree. Add settings preview if useful; no native clients.
- [ ] Run actual preview at800x1000 and narrow200%DPI; inspect candles, colors, labels and pinned actions.
  Benchmark dense candles/bubbles with sessions OFF/ON using identical fixtures. Report p50/p95,
  allocations and measured calculation counts; no native/HFT certification.
- [ ] Run full tests, Release app build and `dotnet build nt8/BridgeCheck --nologo -v q`. Check unchanged
  native sources and no trading handlers modified. Native UI test only if Computer Use initializes;
  otherwise record its exact failure and native checks pending.
- [ ] Dispatch one fresh read-only whole-branch reviewer with this plan/spec and ledger rulings.
  Fix important findings in one test-first pass; verify whole suite afterward. Record declined native
  checks and any deferred minor findings honestly.
- [ ] Update verification evidence with actual counts/screenshots, defaults, partial/stale rules and
  owner checklist. Full test run before commit; package next unused beta through tools/package.ps1.
  Verify EXE version/commit, ZIP hash and included native source hashes.
- [ ] Push `codex/chart-session-zones` only after proof; verify remote SHA and clean tracked tree.
  Preserve main/published beta7 and installed app. Give owner branch link, actual screenshot and ZIP.

## Self-review / execution handoff

All spec sections map to Tasks1–5; the five Review Focus items have explicit tests above. Defaults,
type names and limits are consistent. This plan does not authorize a release/main merge or native
order actions. Recommended method: inline implementation with one final independent review; tasks
share one session model and worker/render interfaces, so a single implementer avoids interface drift.
