# Ticket verification — 1.12.10 (beta)

## Automated evidence

219 isolated tests: 191 core/DOM and 28 tests linking actual bridge source against test-only API
doubles and the real .NET Framework JSON parser. No native SIM/LIVE orders, sockets to NinjaTrader,
settings resets or native installation changes are made by these tests.

New regressions cover long and short reducing partial fills through flat, no opposite-side brackets,
exact remaining protection, stale reduction commands, CLOSE HALF protection before/after fill,
pending reduction shrinking after a TP fills, delayed submission after flat/reopen, deferred changes
and cancellations, partial batch retries with unique OCO fragments, partial TP/SL sibling resizing,
account selection changes while protection is queued, and absence of the extra order-button warning.

BridgeCheck compiles the production AddOn against installed NinjaTrader assemblies. This does not
prove native event timing, order acceptance, exchange OCO, or race-free execution.

## Required native SIM checks — NOT completed by automated tests

Replace both DOM and bridge, compile with F5. Save native order/execution logs for each check.
Do not use funded/live accounts for certification.

- [ ] Repeat the previous version's full Ticket checklist, excluding its removed banner requirement.
- [ ] Long 10: ladder SELL 4 fills in partials. Remain long 6; no BuyToCover TP/SL appears.
- [ ] Repeat short 10 / BUY 4; protection covers only remaining short 6.
- [ ] Scale out the remainder to flat: owned TP/SL and pending reductions cancel; no new stop appears.
- [ ] TP and SL partial/full fills: paired quantities reconcile, final flat leaves no owned exits.
- [ ] Place reduction, fill another TP first: pending reduction shrinks/cancels to remaining exposure.
- [ ] CLOSE HALF leaves protection until its fill, then shrinks correct owned OCO pairs.
- [ ] Scale in while exit quantity changes/cancels are pending: new protection eventually appears
      at its own fill-derived prices and no old queued protection attaches after flat/reopen.
- [ ] Drag pending entries/exits; account switches, connection loss, rejected changes/cancellations,
      and fast competing fills do not silently leave wrong-side or oversized orders.
- [ ] Extra yellow BRACKET OFF text is gone; existing toggle and saved setting remain unchanged.

`reduceOnly` is application/bridge intent, not a NinjaTrader/exchange reduce-only property. Separate
native exits and pending reductions can fill before cancel/change acknowledgments; this patch does
not implement the proposed transactional exit allocator. Any reversal or protection mismatch fails
native certification. Keep beta status until actual logs establish the required behavior.
