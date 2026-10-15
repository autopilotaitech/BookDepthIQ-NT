# Ticket verification — 1.12.9 (still beta)

## Automated evidence

Run from the repository root on Windows:

```
dotnet test --nologo -v q
dotnet build nt8/BridgeCheck --nologo -v q
```

Result: **189 core/DOM tests + 11 bridge tests = 200 passed**, zero failures/skips.
BridgeCheck compiles the production bridge against installed NinjaTrader assemblies, .NET Framework
4.8 / C# 6, with zero warnings/errors. The packaging script runs both test suites before publishing.

The audit's original nine failed cases (eight distinct defects) are now durable regression tests.
Further regressions cover incomplete snapshots, switch-away-and-back, disconnect/reconnect entirely
between UI ticks, individual ladder cancellation, exact reversal size independent of size input,
refused flatten, delayed/duplicate confirmations, expiration, dynamic-stop risk drift after a realized
loss, old-bridge rejection, BRACKET OFF visibility, and actual TP1 execution identification/deduplication.

The 11 bridge tests link **the actual production bridge source** with test-only NinjaTrader API doubles.
They run under .NET Framework 4.8 with its real JavaScriptSerializer, not a replacement JSON parser.
They check both directions' partial-fill TP/SL prices and quantities, OCO identifiers, global SCALE target
names, protection after account selection changes, duplicate executions, close-half trimming, emergency
exit dispatch, and stop-only/target-only/unbracketed configurations. The simulator implements its own
fill/cancel behavior; it does **not** certify NinjaTrader's or a broker's OCO/callback guarantees.

The DOM tests execute the real Ticket and command/management handlers into an in-memory writer.
They never start BridgeClient or its socket, load user settings, or submit an order to NinjaTrader.
No actual SIM or LIVE orders were sent. Your running installation, settings, journal and positions
were not modified for these tests.

## Required upgrade

1. Start with no pending test orders or positions you intend this update to manage.
2. Close the old DOM. Install the new DOM executable from the ZIP.
3. Replace `Documents\NinjaTrader 8\bin\Custom\AddOns\BookDepthIQBridge.cs` with the ZIP's
   `NinjaTrader\BookDepthIQBridge.cs` in your actual NinjaTrader data folder, then compile with F5.
4. Restart/reconnect the DOM. New entries are blocked until the updated bridge advertises its order
   capability and fresh balances, selected-instrument position and order snapshots have arrived.
5. Verify SIM account and intended instrument in both NinjaTrader and DOM. Check BRACKET ON.
   We do not automatically turn your saved BRACKET setting on.

## Behavior to expect

- SCALE divides total size, not size per child. With 4 contracts, 4 children and two targets split
  50/50, the first two contracts belong to TP1, the next two to TP2. A one-contract child is not
  incorrectly split into multiple fractional targets. TP/SL distances remain your ticket's ticks.
- A target's fills use each actual entry fill price. Per-slice TP/SL legs share one OCO identifier.
- REVERSE captures the current position's absolute size, independent of the ticket size field.
  It first flattens; only acknowledged, freshly confirmed flat/empty state can cause one protected
  opposite entry. Eight seconds without confirmation aborts it. FLATTEN, CANCEL ALL, individual ladder
  cancel, B/E, CLOSE HALF, KILL, account/instrument changes, ticket edits and session loss abort intent.
- Reverse checks the **actual captured stop** against current equity/fees/day limits after flatten;
  it does not validate a different, newly smaller ATR/swing stop and then send the larger one.
- Trailing and B/E OFF disarm existing watches. Turning ON applies to a subsequent ticket entry.
- B/E after TP1 waits for identified TP1 executions covering its complete allocated quantity.
  CLOSE HALF and cancelling a target are not TP1 executions.
- Disconnect/account changes clear app-managed watches; working native TP/SL remain native orders.
  Verify and reconfigure management after reconnect rather than assuming an old watch resumes.
- BRACKET OFF means no attached TP/SL, and the warning remains visible with the Ticket hidden.

## Owner's native SIM checklist — NOT yet certified

Use SIM only, modest explicit quantities and tick distances, and keep native NinjaTrader emergency
flatten available. Do not use funded/live accounts for this checklist. Confirm each resulting order
in NinjaTrader's Orders tab as well as the DOM; a DOM acknowledgment alone is not a fill or protection.
Use one target for one contract; two/three targets require enough whole contracts for the split.

- [ ] BUY/SELL market, ladder limit and Shift-stop: expected fill size, TP and SL immediately appear.
- [ ] BRACKET OFF intentionally sends no legs and shows a warning; switching ON restores attachments.
- [ ] One, two and three targets: whole-contract totals, fees and risk readout match the ticket.
- [ ] Fixed contracts, $ risk and % room; cap; invalid/too-small target splits refuse clearly.
- [ ] TICKS, warmed ATR/R and SWING/buffer stops: intended side and distances.
- [ ] Partial fills at different prices: every filled contract gets correct TP/SL; no duplicated legs.
- [ ] SCALE BUY/SELL, Start/Even/End skew: total quantity, prices, target totals and protection.
- [ ] TP fill cancels only its paired stop; SL fill cancels only its paired TP; no orphan exits.
- [ ] Manual B/E, B/E after actual TP1, CLOSE HALF, R/ST1/ST2 trails and stop guard.
- [ ] Turning management OFF stops further app moves; manual CLOSE HALF does not fake TP1.
- [ ] Delayed limit fill and flat interval between SCALE fills retain intended management.
- [ ] REVERSE: confirmed flat first, exactly one opposite entry, correct size and fresh brackets.
- [ ] Cancel/timeout/reject/disconnect during reverse never causes a delayed surprise entry.
- [ ] Drag/right-click/grouped working orders: valid moves/cancels, visible rejection reasons.
- [ ] FLATTEN, CANCEL ALL and KILL leave no unintended working orders or open test position.
- [ ] Account/instrument changes and reconnect: fresh snapshots, no cross-account management.
- [ ] Pending entry on prior SIM account still receives native brackets if it fills after selection changes.
- [ ] SAFE/ARMED hotkeys and focused text inputs; daily-loss/max-trade/cooldown entry gates, exits usable.

Optional time/equity-driven day-limit and weekend management must be validated separately under
controlled simulated conditions before relying on it. Keep beta status until native SIM acceptance
is complete; passing automated tests alone is not a profitability or live-trading safety guarantee.
