# Large Trade Bubbles Implementation Plan

Implementation completed on `codex/chart-large-trades`; see `docs/LARGE-TRADES-VERIFICATION.md`
for tests, screenshots, measured limits and the necessary additive chart-timestamp bridge correction.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Build the approved live executed-trade bubble overlay with per-instrument preferences.

**Architecture:** Add a bounded store fed once by existing live print ingress. Project snapshots onto
native chart bars, render read-only bubbles and expose preferences in existing chart settings.
Historical backfill is a separate opt-in follow-up, not a fake enabled control in this live build.

**Tech Stack:** .NET8, Avalonia11.2.3, C#, xUnit/Avalonia.Headless, existing JSON configuration.

**Spec:** `docs/superpowers/specs/2026-10-15-large-trade-bubbles-design.md`

## Global Constraints

- No trades/orders, including cancellation, modification and flattening.
- Keep DOM primary, existing theme/layout, chart navigation and native order bridges unchanged.
- Individual reported prints only; integer ticks internally; unknown side stays unknown.
- Retain at most50000 prints and60 minutes; draw at most500 markers/pane at existing30fps cap.
- Defaults disabled,50 contracts,20% fill,12–42 DIP; do not claim optimized thresholds.
- Full `dotnet test --nologo -v q` before each commit. No merge/main/release without owner instruction.

## Review Focus

- Distinct prints sharing timestamp/price/size remain distinct (Task1).
- Late prints and missing/DST session boundaries never attach to unrelated candles (Task2).
- Malformed saved colors or reversed diameter limits cannot break rendering (Task3).
- Capture transitions/late contract packets do not label old trades as a new contract (Task4).
- Dense bubbles cannot cover axis tags or interfere with chart navigation (Task5).

## File map

New `src/Core/Charts/ChartTradeStore.cs`: bounded raw live records and coverage.
New `src/Core/Charts/ChartTradeProjection.cs`: interval matching, threshold, deterministic render cap.
New `src/Dom/Charts/LargeTradeSettings.cs`: profiles, normalization and color validation.
New `src/Dom/Charts/LargeTradeSettingsPanel.cs`: dialog fields and deferred Apply.
New `src/Dom/Charts/CandleChartControl.Trades.cs`: bubble drawing/hit testing.
Modify `DomState.cs`: single accepted-live-print hook, contract/disconnect capture boundaries.
Modify `ChartWorkspace.cs`, `ChartWorkspaceSettings.cs`, `MainWindow.Charts.cs`, `ChartPane.cs`,
`ChartSettingsWindow.cs`: integration and shared profiles; preserve existing constructor defaults.
Modify `CandleChartControl.cs`, `.Rendering.cs`, `.Input.cs`: snapshots, layer ordering and hover.
Modify `tools/ChartPreview/Program.cs`: synthetic bubble fixture and off/on benchmark.
Tests: new `ChartTradeStoreTests.cs`, `ChartTradeProjectionTests.cs`, `LargeTradeSettingsTests.cs`,
`ChartTradeIntegrationTests.cs`, `ChartTradeUiTests.cs` in `tests/Core.Tests`.

### Task1: Bounded print store

**Interfaces:** `ChartPrint(long Id,string Instrument,long Tick,long Quantity,char Side,long TimeMs)`;
`ChartTradeStore.Reset(string instrument)`, `SetCapture(bool enabled,long nowMs)`,
`Add(string instrument,long tick,long quantity,char side,long eventMs,long receivedMs)`,
`Disconnect(long nowMs)`, `Snapshot(long nowMs)` returning immutable records and coverage segments.
Constructor supports smaller limits for tests, default50000/3600000ms. Receipt IDs monotonic.

- [ ] Write real-store tests. Include a literal small-limit fixture:
```csharp
var store=new ChartTradeStore(2,1000);store.Reset("NQ TEST");store.SetCapture(true,0);
store.Add("NQ TEST",100,50,'b',100,100);
store.Add("NQ TEST",100,50,'b',100,101);
Assert.Equal(2,store.Snapshot(101).Prints.Count);
store.Add("NQ TEST",101,60,'?',200,200);
Assert.Equal(new long[]{50,60},store.Snapshot(200).Prints.Select(p=>p.Quantity));
Assert.Empty(store.Snapshot(1300).Prints);
```
- [ ] Run `dotnet test tests/Core.Tests --filter ChartTradeStoreTests --nologo -v q`; observe failure.
- [ ] Implement bounded receipt-ordered storage, event-time-sorted cached snapshots, positive-size
  validation, exact-contract rejection, enable/disable/reset/gap coverage. Never deduplicate equal prints.
- [ ] Add tests for late event times, wrong instrument, invalid size, disconnect/reconnect segments,
  disable clearing and100000 received prints respecting bounds. Run targeted then full suite; commit.

### Task2: Projection and marker math

**Interfaces:** `ChartBubble(ChartPrint Print,double BarPosition,double Diameter)`;
`ChartTradeProjection.Build(prints,bars,frame,fromMs,toMs,minContracts,minDiameter,maxDiameter)`
returns visible candidates sorted/capped500 plus omitted count. Inputs remain immutable.

- [ ] Test inclusive50 threshold using49/50/200 print quantities; assert two markers, diameters12/24
  with12–42 profile, and cap42 for very large volume. Expected literals, not helper-derived values.
- [ ] Run projection tests and observe missing behavior; implement fixed square-root size formula.
- [ ] Test native end-time mapping, exact boundaries, gaps, overnight/DST and Daily trading dates.
  Inspect `BookDepthIQChartBridge.cs` timestamp production first. When an interval cannot be proven,
  omit the print; no nearest-bar fallback across gaps. Do not claim Daily support without valid mapping.
- [ ] Test501 candidates yield500 largest deterministically, no summed synthetic print, no duplicates
  from two pane projections, no input mutation. Run targeted/full tests; commit.

### Task3: Settings model and dialog

**Interfaces:** `LargeTradeProfile` holds MinContracts, BuyColor, SellColor, UnknownColor, Opacity,
MinDiameter, MaxDiameter, Labels; `Normalize()`, `Diameter(long volume)`. Workspace owns
`Dictionary<string,LargeTradeProfile> TradeProfiles`; pane owns `ShowLargeTrades`.
`LargeTradeSettingsPanel` edits a draft and applies on existing dialog Apply only.

- [ ] Write tests for NQ contract-rollover profile reuse, NQ/MNQ isolation and unknown exact-instrument
  keys; JSON roundtrip, invalid hex colors, nonfinite opacity and reversed size bounds. Observe failure.
- [ ] Implement documented defaults/ranges, robust normalization and profile lookup. Known root matching
  must use exact existing CME roots, not prefix match. Invalid colors fall back to theme defaults.
- [ ] Add existing-dialog section with enable, instrument profile, read-only Individual prints mode,
  threshold, three color swatches/validated hex inputs, opacity, min/max diameter and label policy.
  Apply-to changes pane enable only; explain profile values are shared. No disabled history placeholder.
- [ ] UI test Cancel preserves settings, invalid drafts cannot apply, Apply persists without candle reload,
  All/Large/Off labels and Both/This pane enable semantics. Full suite; commit.

### Task4: Live ingress and workspace snapshots

**Interfaces:** DomState owns the store. Existing OnPrint feeds it once only when live and valid.
MainWindow workspace wiring sets capture from active pane feature flags and exact selected instrument;
workspace provider exposes immutable captured snapshots to both panes at chart refresh cadence.

- [ ] Add integration tests using real DomState message ingress/TicketHarness. Inject:
```csharp
// After existing harness instrument/account setup and enabling capture:
using var packet=System.Text.Json.JsonDocument.Parse("{\"t\":\"trade\",\"p\":30000,\"v\":85,\"side\":\"b\",\"ms\":1000}");
state.Apply(packet.RootElement);
Assert.Single(state.ChartTrades.Snapshot(1000).Prints);
Assert.Empty(harness.Commands);
```
- [ ] Observe failure; implement hooks after existing live-print validation, not Tape scanning.
  Existing history messages must not enter the live store. Preserve historical analytics unchanged.
- [ ] Test contract switch/reset, disable-all, hidden charts, reconnect gaps and malformed packets.
  Existing protocol trade messages lack contract IDs: preserve original session/reset ordering and
  do not invent identity; record inability to distinguish delayed same-socket packets as a limit if
  transport cannot prove their context. Do not silently certify cross-generation isolation.
- [ ] Confirm native bridge sources unchanged and no command emitted for capture/settings/projection.
  Run targeted/full suite; commit.

### Task5: Render and hover without trading interactions

**Interfaces:** CandleChartControl receives immutable print snapshot/profile and exposes read-only
hover detail. Project/draw at existing chart cadence; cache by print version, profile, bars and viewport.

- [ ] Add failing bitmap tests for cyan/orange/neutral bubbles, numeric label modes and threshold hiding.
  Assert candle viewport/autoscale unchanged. Use controlled synthetic literal print positions.
- [ ] Implement `.Trades.cs`; draw transparent circles and contrast-safe text after candles but below
  crosshair/price/order labels. Draw axis tags before plot clipping to preserve existing DPI fix.
- [ ] Add real pointer tests: hover shows exact print facts and overlapping count, drag/wheel still
  navigate, trading/emergency shortcuts unchanged. No bubble click action sends commands.
- [ ] Render at100/150/200%DPI and320/600/1000DIP widths. Test deterministic500 cap and dense omission
  detail. Run targeted/full tests; commit.

### Task6: Desktop evidence and packaged test build

- [ ] Add synthetic bubble fixture switches in ChartPreview, preserving its zero native connections.
  Use live-looking85/120/65/210/95 prints and matching settings for approved screenshot comparison.
- [ ] Build app Release and preview tool, run full tests, capture actual controls at normal/high DPI.
  Benchmark identical two-pane10000-bar scenes with bubbles off/on; report p50/p95 and allocations,
  not feed/order latency. If capture time is dominated by sorting or full rebuilds, profile and add
  a failing allocation/behavior regression before optimizing.
- [ ] Required read-only whole-branch review via requesting-code-review; fix confirmed important issues
  with failing tests first. Native chart-only testing is allowed, no order actions/hotkeys; desktop
  helper failure is a reported limitation, not permission for blind UI automation.
- [ ] Document scope/history limitations, controls, proof and remaining native checks. Run full suite
  before final commit; package with next unused beta version using tools/package.ps1, verify EXE version,
  ZIP checksum and clean tree. Keep main/published release unchanged. Give owner screenshot + test ZIP.

## Separate historical option follow-up

Add an OFF-by-default historical checkbox only when backed by an isolated, bounded historical tick
request/cache path. The existing main history feed may infer side by uptick/downtick; it cannot be
silently reused as confirmed bid/ask. Historical mode needs source/coverage labeling, quote-vs-inferred
classification, unknown neutral color, cancellation/context generations and history/live cutoff tests
that preserve distinct identical prints without overlap duplication. This follow-up needs its own
data-path design; it is not part of the approved live-first implementation.
