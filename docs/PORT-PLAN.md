# Port plan: BookDepthIQ-Vest → BookDepthIQ-NT

The source is `BookDepthIQ-Vest` (TypeScript/React). The pure logic in `src/panel/*.ts` ports to
`src/Core` (C#, unit-tested) and the screen to `src/Dom` (Avalonia). Perp units and leverage become
CME **contracts, ticks and tick value, with fees per side**.

## M1 — done (2026-10-13)

- NT8 bridge AddOn: depth (top 40), prints with aggressor, quote, account items (cash, realized,
  unrealized, net liq, NT trailing DD, daily limit), positions, working orders. Commands: market,
  limit and stop entries; OCO TP/SL on fill; cancel; move; flatten; cancel all. Compiles against
  NT 8.1.x (`nt8/BridgeCheck`).
- DOM: Vest look, with columns BUY | BID | PRICE | ASK | SELL | P&L | VOLUME. Click to trade,
  shift for a stop, click an order to cancel it, drag to move it. Calm follow, last-price box,
  average-price row, tape, order log, result bar.
- CME risk: contracts / $ risk / % room sizing, $/tick, fees, R:R, % of room, losers left,
  break-even ticks, stop-past-fail warning. Prop drawdown: static, trailing intraday, trailing EOD,
  lock, daily loss.

## M2 — done (2026-10-13): everything from the Vest panel

| Vest panel | Here |
|---|---|
| `ladderMath.ts` grouping, off-screen markers, spread window | `Core/LadderMath.cs` |
| `liquidity.ts` heat, pull/stack, absorb | `Core/Liquidity.cs` → HEAT 5M · PULL/STACK column |
| `footprint.ts`, `profile.ts` | `Core/Footprint.cs` → SOLD/BOUGHT, imbalances, stacks, walls, PROFILE, Δ |
| `cost.ts`, `checks.ts` | `Core/Cost.cs` → cost line, warnings (never block), FEED |
| `supertrend.ts` (trend.rs) | `Core/SuperTrend.cs` → ST1/ST2 lines, SuperTrend pill |
| `vwap.ts`, `indicators.ts` | `Core/Indicators.cs` → VWAP σ, per-instrument settings, stack zones |
| `paxor.ts` | `Core/PaxOr.cs` → OR H/L/MID/EXT lines |
| `targets.ts`, `manage.ts` | `Core/Targets.cs`, `Core/Manage.cs` → TP1–3 split, scale, B/E after TP1, trail R/ST, stop guard, weekend close |
| `structure.ts` | `Core/Structure.cs` → market state, scalp read, swing stop, RANGE lines, journal stats/CSV |
| `protect.ts`, `orderlog.ts` | `Core/Protect.cs` → protect my day, order log |
| `riskcalc.ts` + RISK page | `Core/CmeRisk.cs`, `Core/Drawdown.cs`, `Dom/RiskWindow.cs` |
| journal (LIVE: Vest history) | `Core/Trips.cs`: exact round trips from NT executions |
| record + replay | NinjaTrader's Playback connection (the DOM works on Playback101) |
| funding | not applicable to CME futures |

Tests: the Vest tests for each module, ported with the same cases and numbers (36).

## Next

- First run inside NinjaTrader on Sim101 (the owner): check depth, history, brackets, B/E, trail.
- Ladder colours and widths to taste once seen on real CME depth.

