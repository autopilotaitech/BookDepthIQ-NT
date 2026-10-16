# Manual exit coverage correction — beta.12

Bug reproduced with the real bridge code inside the isolated NinjaTrader simulation:
a3-contract stop reserved all3contracts and rejected TP limits; TP limits likewise blocked stops.
The same combined budget also cancelled/shrank complementary exits during reconciliation.

Correction: tracked manual stop reductions and non-stop reductions now have independent position-sized
budgets, in both admission and reconciliation. Multiple targets share the non-stop budget, multiple
stops share the stop budget. Neither group can exceed current exposure. Pending cancellations still
count until confirmed. ClosePart no longer counts working manual stops as profit-exit reservations.
Only confirmed position/fill events resize remaining tracked exits; no new entry brackets are
attached to reducing orders. Flat/direction changes cancel tracked leftover exits. Unrelated native
orders remain untouched. Automatic entry bracket/OCO behavior is unchanged.

## Verification

Four long/short × stop-first/target-first tests failed before the correction. All now pass with
one full-position stop and multiple TP limits, partial target fills, partial stop fills, flat cleanup
and no additional order creation. Added duplicate-family rejection, stop-protected ClosePart and
delayed quantity-change/cancellation confirmation cases. Existing scale/automatic bracket/reverse
safety tests remain green. Full suite:407passed (363 Core/DOM +44 Bridge), no failures/skips.
Release app and net48/C#6 installed-DLL native compile checks pass. No actual SIM/live orders sent.

## Required upgrade and limitations

Update BOTH the beta.12 app and `NinjaTrader/BookDepthIQBridge.cs`, then recompile in NinjaTrader.
Order capability version is now3; the new app refuses new Ticket orders on older bridges.
Cancel/flatten remain available. Chart bridge unchanged from beta.9; beta.11 marker improvements included.
Installed files/running instances are not automatically replaced.

Separately clicked manual exits are **not retroactively broker-native OCO pairs**. Their cleanup is
managed by the running bridge after native position/order updates, not atomically at the exchange.
Disconnects, delayed changes/cancels and two exits filling before reconciliation can still race;
this correction is not a broker-level reduce-only guarantee. It does not cancel/replace an existing
protective stop just to synthesize OCO. Use native OCO/ATM protection where that guarantee is required.
Native SIM acceptance of placement, partial fills, dragging, reconnect and cancellation still required
before funded use. Automated simulation is not claimed as live-provider execution proof.
