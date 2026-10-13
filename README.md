# BookDepthIQ-NT

A modern standalone pop-out **DOM for NinjaTrader 8**, built with **Avalonia** (.NET 8). It is a full
port of the [BookDepthIQ-Vest](https://github.com/autopilotaitech/BookDepthIQ-Vest) panel, with the
same ladder, look, reads and click behaviour. Risk is worked out in **CME contracts, ticks and tick
value, with commissions**, and against **your prop firm's drawdown rules**.

![BookDepthIQ DOM on NQ: footprint, heatmap with walls, volume profile, delta, P&L column, SuperTrend, VWAP σ, scalp read, bracket bar and CME risk line](docs/images/dom.png)

```
┌──────────────── NinjaTrader 8 ────────────────┐                ┌──────── BookDepthIQ DOM (Avalonia) ────────┐
│ your connection (Rithmic / CQG / NT Brokerage)│                │ ladder · reads · tape · journal · order log │
│ BookDepthIQBridge AddOn: depth, prints,       │── 127.0.0.1 ──▶│ CME risk ticket · RISK · settings           │
│   history, account, positions, orders, fills  │◀── commands ───│ every order = your click                    │
└───────────────────────────────────────────────┘                └─────────────────────────────────────────────┘
```

The DOM runs in its own process. If it crashes, NinjaTrader and your orders are not affected. The
bridge is a thin AddOn: it streams data and executes what the DOM sends. It never decides to trade.

> **Status: beta, test on Sim101 first.** The bridge compiles against NinjaTrader 8.1.x. The DOM has
> run against a simulated feed. Run it on Sim101 before any funded account.

## Install

1. **Bridge:** copy `nt8/AddOns/BookDepthIQBridge.cs` to
   `Documents\NinjaTrader 8\bin\Custom\AddOns\`. In NinjaTrader open **New → NinjaScript Editor**
   and press **F5**. It listens on `127.0.0.1:36971` only. After an update, copy it again and press F5.
2. **DOM:** `dotnet run --project src/Dom` (needs the [.NET 8 SDK](https://dotnet.microsoft.com)).
   The header shows **● NT** when it is connected, with the median time NinjaTrader takes to answer.
3. Type the instrument (for example `NQ 12-26`) and press **Go**, then pick **Sim101**. The DOM
   loads up to 48 h of NinjaTrader tick history so SuperTrend, VWAP, the opening range and the
   profile are right from the start.

## Layout

The ladder runs the full height on the left. The right column holds, from top to bottom:

- **TAPE**, with **journal** and **log** buttons that swap the view (click again for the tape), plus
  **heat** and **ST** toggles.
- **TICKET:** size, stop, targets, bracket, B/E, trail, stop guard, setup, preview, scale and
  hotkeys, then the risk, warning, cost and result lines.
- **Buttons** in three columns: **BUY MKT · SELL MKT · FLATTEN**, then **REVERSE · CANCEL ALL**,
  then **B/E · CLOSE HALF**.

## The ladder

Columns (each can be turned off in **SETTINGS → Ladder**): **HEAT 5M · PULL/STACK | BUY | SOLD |
BID | PRICE | ASK | BOUGHT | SELL | P&L | PROFILE | Δ**. The heatmap sits at the far left, next to
BUY.

- **Click** the buy side (BUY, SOLD, BID) for a buy limit, the sell side (ASK, BOUGHT, SELL) for a
  sell limit. **Shift-click** places a stop. **Click** a working order to cancel it, **drag** it to
  move it.
- **Price:** best bid and ask are coloured, the yellow box is the last price, and the dashed amber row
  is your average. The yellow bar marks the POC; the shaded rows are the value area.
- **BID/ASK:** depth bars. A **wall** (3× the average size on that side) shows yellow.
- **SOLD/BOUGHT:** footprint. Diagonal **imbalances** light up, and stacked ones are brighter and
  drawn as **BUY/SELL STACK** lines.
- **HEAT 5M:** resting size over the last 5 minutes in 10-second bins, time-averaged and coloured
  by the side it was on. Yellow means a wall. **PULL/STACK** shows the net size pulled or added in
  the last minute. **ABS** means absorption: one side kept hitting a level for more than was showing,
  and it held.
- **PROFILE / Δ:** drawn as BookDepthIQ's ladder draws them. The profile is a neutral grey volume
  histogram (POC near-white, value area mid-grey, outside it dim grey), with the value area as a
  faint magenta band on PRICE. Δ is a signed bar from the column centre, cyan for + and orange for −.
  **Visible range:** the POC, value area and bar scale come from the rows on screen, so they follow
  scrolling and zoom. Δ scales to the visible rows too. Both update on every print. They count the
  CME trading day (reset at 17:00 Chicago), from NinjaTrader's history plus live prints.
- **P&L:** what each price is worth to your open position.
- **Lines:** VWAP with ±σ bands, ST1/ST2, the RTH opening range (NQ/ES, BookDepthIQ PAXOR),
  **NO CHASE** (ST1 ± 2 ATR), RANGE H/L in chop, your TP/SL legs with their $, **FAIL** (where the
  account fails at this size) and the preview. Lines off the screen are pinned to the top or bottom
  with their distance.
- **Zoom on the PRICE column:** wheel over PRICE (or drag it up/down) zooms in and out. Row height
  first, then ticks per row (1 → 2 → 4 → 8 → 20 → 40 → 100). The price under the mouse stays put.
  **Double-click PRICE** (or **Home**) goes back to 1 tick per row, centred. **+/−** zoom around the
  middle; a badge shows the scale.
- **Wheel elsewhere** scrolls (Shift = a page) and holds the view for 15 s. **Space** re-centres.
  Otherwise the ladder holds still unless price drifts.

## Header reads

- **SuperTrend:** dual ST, a port of BookDepthIQ's `trend.rs`, for example **▲▲ STRONG LONG +75**.
- **Scalp read:** **GO — pullback to ST1** (within 2 ATR of the line and pulling back), **wait for
  the dip**, or **STRETCHED — don't chase**.
- **Market state:** **TREND / CHOP**, read from price (15-minute efficiency and the move in ATR),
  plus **EXTENDED**.
- **FEED:** milliseconds since NinjaTrader's last book or print.

## Ticket and risk (CME)

- **Size:** **contracts**, **$ risk @SL** or **% room @SL**, with quick buttons. Contracts are whole;
  a $ risk too small for one contract says so.
- **Stop:** **ticks**, **ST1 ATR × mult** or **swing** (past the N-minute high/low). **TP1–TP3** are in
  ticks, or in R in ATR/swing mode, with a **split** such as 50/30/20.
- **Brackets, like NT's ATM:** each fill is split across your targets. Each slice gets its own TP and
  SL as one OCO pair, so a partial fill is protected at once.
- **Risk line:** contracts, SL, $/tick, risk and win with fees, R:R, % of room, losers left and
  break-even ticks. It turns red when the stop is past the fail line.
- **Warnings** (size × your normal, far from the market, spread wide, stop past the fail line, over
  your max contracts) never block. One click sends.
- **Cost line:** spread gate against this market's own recent spreads, your fill for this size,
  slippage, all-in break-even, and TP as a multiple of cost.
- **PREVIEW LONG/SHORT:** the stop and targets drawn on the ladder while flat. Drag them to set the
  distances.
- **SCALE:** a ladder click sends 2–10 limits from that price away from the market.
- **Buttons:** **BUY MKT**, **SELL MKT**, **B/E**, **CLOSE HALF** (bracket legs trimmed),
  **REVERSE**, **CANCEL ALL** (Esc), **FLATTEN** (F), **KILL** (Ctrl+K: every position and order on
  the account). Exits are never blocked.
- **Hotkeys** B / S / E / R work only while **HOTKEYS ARMED** is on. On SAFE they say so.

## Account rules and protect my day

- **SETTINGS → Account:** start balance, max drawdown, **static / trailing intraday / trailing
  EOD**, where trailing stops (lock), and the prop daily loss. Room = equity − floor, or today's
  limit if that is tighter. A bar shows the room left.
- **SETTINGS → Protect my day** (all off by default): daily loss limit, max trades, cooldown after a
  loss, close everything at the limit, and close before the Friday CME halt. Limits apply to NEW
  entries only. The day starts 17:00 Chicago.
- **Management that acts without a click** (each off until you turn it on, and none ever opens a
  position): **B/E after TP1**, **trailing stop** (after N R, or on ST1/ST2), **stop guard** (puts a
  vanished stop back), and the two closes above.

## RISK window

**RISK** lists every CME contract (NQ, MNQ, ES, MES, YM, MYM, RTY, M2K, GC, MGC, SI, SIL, HG, CL,
MCL, NG, 6E, ZB, ZN). For your account or a custom plan, each row shows the tick size, $/tick,
contracts for your rule, $ lost at the stop and won at the target with fees, % of room and ticks to
fail. It also draws a risk map for the market on screen. **reset** returns to your account and the
ticket's numbers. **Use in DOM** writes the plan into the ticket. It never sends an order.

## Journal and order log

- **Journal:** every round trip, built from NinjaTrader's executions. Entry, exit, size, $ after
  commissions and fees, R, setup tag and slippage against the price you clicked. Totals by setup.
  **copy CSV**.
- **Order log:** every click, what was sent, NinjaTrader's answer with its time, rejections with
  NT's reason, warnings and blocks. **copy** it for a bug report.
- **Replay:** use NinjaTrader's own **Playback** connection. The DOM shows it like any other feed,
  on the Playback101 account.

## Develop

```
dotnet build                                   # Core, DOM, tests, fake bridge
dotnet test                                    # 36 tests: risk, drawdown, SuperTrend, PAXOR, heat, footprint, trips …
BDIQ_PORT=36972 dotnet run --project tools/FakeBridge -- --position   # simulated NQ, never sends orders
BDIQ_PORT=36972 dotnet run --project src/Dom   # the DOM against the fake (NinjaTrader is not touched)
dotnet build nt8/BridgeCheck                   # compiles the AddOn against C:\Program Files\NinjaTrader 8\bin
```

The protocol is in [docs/PROTOCOL.md](docs/PROTOCOL.md) and the port plan in
[docs/PORT-PLAN.md](docs/PORT-PLAN.md). Rules for changes are in [CLAUDE.md](CLAUDE.md).

Not financial advice. Manual trading only.
