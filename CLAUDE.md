# BookDepthIQ-NT — agent rules

A standalone DOM for NinjaTrader 8: an **Avalonia** app (`src/Dom`, .NET 8) in its own process,
fed by a thin **NT8 AddOn bridge** (`nt8/AddOns/BookDepthIQBridge.cs`) over `127.0.0.1`. It is a
port of the BookDepthIQ-Vest panel (C:\Users\walls\OneDrive\Documents\github\BookDepthIQ-Vest):
same look and behaviour, with risk in CME contracts / ticks / tick value instead of perp units.

Separate project. Do not edit BookDepthIQ or BookDepthIQ-Vest from here; read them for logic.
The owner chose Avalonia (2026-10-13) — do not switch the UI to WPF or an in-NT window.

## Trading safety — absolute

1. **No order is sent without the owner's explicit go**, Sim101 included, by any test or script.
2. **Manual only.** Every order comes from a user click in the DOM. No algo, no auto-entry, no
   copier. The bridge never decides to trade: it only executes a command the app sends from a
   click. Exceptions follow the Vest panel's owner-approved list and must be re-approved here
   before they are built (brackets attached to an entry the user clicked are part of that entry).
3. **Brackets are the user's numbers** (ticks). No hard-coded TP/SL distances.
4. **Flatten and cancel are never gated.**
5. **No silent no-ops.** A click that can't send says why, in the result bar and the order log.
   No second-click confirms on entries (the owner removed them in the Vest panel).
6. Test on **Sim101** first; the account selector defaults to it.

## Engineering

- NinjaTrader API facts come from the installed DLLs (read their metadata; never guess a member).
  `nt8/BridgeCheck` compiles the AddOn against `C:\Program Files\NinjaTrader 8\bin`.
- The AddOn must compile inside NinjaTrader: .NET Framework 4.8, C# features NT's compiler
  accepts, only NT's default references (JSON via System.Web.Extensions' JavaScriptSerializer).
- Prices are integer ticks inside the app (`Core/Book.cs`). Instrument tick size and point value
  come from NT (`MasterInstrument.TickSize`, `.PointValue`); `CmeSpecs` is only a fallback.
- `dotnet test` must pass before a commit.
