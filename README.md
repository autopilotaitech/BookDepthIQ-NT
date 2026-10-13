# BookDepthIQ-NT

A standalone pop-out **DOM for NinjaTrader 8**, built with **Avalonia** (.NET 8). It is a port of the
[BookDepthIQ-Vest](https://github.com/autopilotaitech/BookDepthIQ-Vest) panel: same ladder, same look
and the same click behaviour. Risk is worked out in **CME contracts, ticks and tick value, with
commissions**, instead of perp units and leverage.

![BookDepthIQ DOM: ladder with bid/ask depth, last-price box, volume profile, tape, order log and the CME risk line](docs/images/dom.png)

```
┌──────────────── NinjaTrader 8 ────────────────┐          ┌──────── BookDepthIQ DOM (Avalonia) ────────┐
│ your connection (Rithmic / CQG / NT Brokerage)│          │ ladder · tape · order log · CME risk ticket│
│ BookDepthIQBridge AddOn ── depth, prints, ────┼─ 127.0.0.1:36971 ─▶ clicks ──▶ order commands ──┐      │
│   account, positions, orders                  │◀─────────┼───────────────────────────────────────┘      │
└───────────────────────────────────────────────┘          └────────────────────────────────────────────┘
```

The DOM runs in its own process. If it crashes, NinjaTrader and your orders are not affected. The
bridge is a thin AddOn: it streams data and executes the order commands the DOM sends from your
clicks. It never trades on its own.

> **Status: first build (M1). Test on Sim101 only.** It compiles against NinjaTrader 8.1.x and the
> DOM has run against a fake bridge. It has not been run inside NinjaTrader yet.

## Install

1. **Bridge (once):** copy `nt8/AddOns/BookDepthIQBridge.cs` to
   `Documents\NinjaTrader 8\bin\Custom\AddOns\`. In NinjaTrader open **New → NinjaScript Editor**
   and press **F5** to compile. The bridge starts with NinjaTrader and listens on `127.0.0.1:36971`
   only.
2. **DOM:** `dotnet run --project src/Dom` (needs the [.NET 8 SDK](https://dotnet.microsoft.com)).
   The header turns green, **● NinjaTrader bridge**, when it is connected.
3. Type the NinjaTrader instrument (for example `NQ 12-26`), press **Go**, and pick **Sim101**.

## Using it

- **Ladder:** click the **BUY/BID** side for a buy limit or the **SELL/ASK** side for a sell limit.
  Shift-click places a stop. Click a working order to cancel it; drag it to another row to move it.
  The yellow box is the last price, and the dashed amber row is your average entry price.
  **Space** re-centres the ladder; the mouse wheel scrolls it.
- **Ticket:** size by **contracts**, **$ risk @SL** or **% room @SL**, with TP and SL in **ticks**
  and **BRACKET ON/OFF**. When an entry fills, the bridge places the TP limit and SL stop as one OCO
  pair, priced in ticks from the fill.
- **Risk line:** contracts, $ per tick, $ lost at SL and won at TP (fees in), R:R, % of room,
  losers left, and the break-even move in ticks. It turns red when your stop is past the fail line.
- **Account rules (yours):** start balance, max drawdown, **static / trailing intraday / trailing
  EOD**, the balance where trailing stops (lock), daily loss limit, fee per side and max contracts.
  Room = equity − floor (or the daily limit, if that is tighter).
- **FLATTEN** (F) and **CANCEL ALL** (Esc) are never blocked.
- **Order log:** every click, what was sent, and NinjaTrader's answer. A click that can't send says
  why; nothing is silently ignored.

## Develop

```
dotnet build                       # Core, DOM, tests, fake bridge
dotnet test                        # risk, drawdown, book, protocol
dotnet run --project tools/FakeBridge   # random-walk NQ book on 127.0.0.1:36971, never sends orders
dotnet build nt8/BridgeCheck       # compiles the AddOn against C:\Program Files\NinjaTrader 8\bin
```

The protocol is in [docs/PROTOCOL.md](docs/PROTOCOL.md) and the port plan in
[docs/PORT-PLAN.md](docs/PORT-PLAN.md). Rules for changes are in [CLAUDE.md](CLAUDE.md).

Not financial advice. Manual trading only.
