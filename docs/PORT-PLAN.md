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

## M2 — next, ported from the Vest panel

| Vest panel | Here |
|---|---|
| `ladderMath.ts` grouping, fit | price grouping (×2, ×4…) |
| `liquidity.ts` heat, pull/stack, absorb | HEAT 5M column, PULL/STACK, ABSORB |
| `footprint.ts` bought/sold, imbalances | SOLD / BOUGHT columns, imbalance marks |
| `profile.ts` volume profile, POC/VA | POC · VA in the position strip |
| `supertrend.ts` dual SuperTrend | ST1/ST2 lines and the scalp read (GO / wait / STRETCHED) |
| `vwap.ts` VWAP + σ bands | VWAP lines |
| `paxor.ts` opening range | RANGE lines |
| `protect.ts` daily limits, cooldown | Protect my day |
| B/E, trail, close half, TP2/TP3 | management row (each needs the owner's OK, as in the Vest panel) |
| RISK calculator page | RISK window: every CME contract, risk map |
| journal, order log copy | journal synced from NT executions |

## Rules carried over

No silent no-ops, no second-click confirms, FLATTEN/CANCEL never gated, brackets are the user's
numbers, and nothing sends an order without a click (see CLAUDE.md).
