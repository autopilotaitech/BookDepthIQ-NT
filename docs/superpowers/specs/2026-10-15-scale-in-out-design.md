# Position-aware SCALE IN / SCALE OUT

Status: proposed design; implementation not started. Supersedes the SCALE behavior in 1.12.9.

## Intent and observed failure

The owner enters orders on the ladder and drags orders as market conditions change. The Ticket must
support increasing and reducing an existing position without making management clicks into accidental
new entries. Preserve the existing visual theme and ladder workflow.

Observed on Sim101, 2026-10-15 at 10:38 Chicago: SELL scale fills reduced a long position from 12 to 6,
but the bridge attached BuyToCover brackets as if the fills opened a short. Existing long protection
was not coordinated with the reductions. EVEN also distributed 10 contracts as 4/2/2/2.

The current bridge uses Account.CreateOrder and per-target native OCO pairs. Installed Order metadata
exposes QuantityChanged, StopPriceChanged, LimitPriceChanged and Oco, but no ReduceOnly property.
Neither client-side quantity clamping nor resizing after a fill establishes native reduce-only semantics.

## User-visible behavior approved in conversation

1. Keep flat-position BUY/SELL entries and draggable ladder orders.
2. Ticket exposes SCALE IN and SCALE OUT as separate intents, with explicit exit contracts or a
   percentage of the current position. The entry-size rule does not silently supply scale-out size.
3. SCALE IN follows the selected opening/addition direction and attaches enabled brackets only to
   newly opened exposure. It cannot reduce or reverse an opposite existing position.
4. SCALE OUT is SELL for a long and BUY for a short. Prices extend in the exit direction from the
   clicked price. It reduces the current position only and cannot create opposite exposure.
5. Opposite-side ordinary ladder/market clicks while positioned are reductions, not independently
   bracketed entries. An order that would pass through flat is refused; REVERSE is the explicit path.
6. Existing exit orders remain draggable. Grouped dragging retains the current visible behavior;
   repricing does not allocate more contracts or attach new brackets.
7. EVEN 10 across 4 prices is 3/3/2/2. Whole-contract totals are exact for every skew mode.
8. Preserve manual B/E, CLOSE HALF, FLATTEN, CANCEL ALL and KILL. No autonomous entries are added.

## Architecture decision requiring review

**Recommended: a bridge-owned exit allocation transaction over existing native TP/SL pairs.**
The DOM sends entry/add or reduce/scale-out intent explicitly. For a scale-out plan, the bridge
reallocates existing target capacity instead of placing a second overlapping set of exits. It owns
the transition, waits for actual order acknowledgments, and reconciles executions and positions.

This preserves the existing per-target grouping and avoids automatically multiplying each contract
into separate native orders. It requires a real asynchronous state machine; the current synchronous
ClosePart trimming loop is not a sufficient implementation.

Alternatives considered:

- Merely cap the exit size and attach no new brackets: fixes the observed wrong-side bracket symptom
  but leaves duplicate/oversized old exits and fill races. Rejected as incomplete.
- One-contract TP/SL OCO slots: simpler quantity ownership after native fills, but substantially more
  orders and a separate migration path for existing multi-contract brackets. Not selected by default.

## Required bridge invariants

- Account + instrument + position direction/epoch identify an allocation. A stale command cannot
  act on a later position or another account.
- Every exit allocation has one owner. An OCO TP/SL pair owns the same contracts, not two independent
  allocations. Scale-out orders and retained targets cannot reserve the same contracts separately.
- Validate the entire plan before changing an order. Refuse flat, wrong side, excessive size, invalid
  prices or stale state without submitting a partial ladder.
- Quantities use remaining unfilled size, not original submitted size. Duplicate execution IDs do not
  consume capacity twice. Partial fills during any transition invalidate/recompute the uncommitted plan.
- A reduction execution never enters the bracket-creation path. Existing protection is reconciled to
  actual remaining exposure; fully closed exposure leaves no app-owned exits behind.
- Do not assume a successful Cancel/Change call is an acknowledgment. Initialized, submitted,
  cancel/change pending, rejected, part-filled and terminal states are distinct.
- Replacement capacity cannot be activated until the replaced allocation has been released by native
  confirmation. If a partial/native OCO race makes safe continuation uncertain, abort the transaction,
  visibly report the conflict and reconcile; do not submit a best-effort excess exit.
- Preserve protective coverage during normal target reallocation. If a requested split requires a
  cancel/replace sequence that cannot maintain the required safety invariants, refuse that plan with
  a useful reason instead of silently leaving the position unprotected or claiming reduce-only safety.
- Cancel or reject the pending transaction on account/instrument/session changes, explicit cancellation,
  flatten/kill or a bounded timeout. Reconnection must reconcile native orders, never replay old intent.
- No automatic cancellation/replacement of unrelated orders, and no automatic account reset/flatten
  as a recovery mechanism. Conflicting manual/external orders are detected and require user direction.
- Bridge order capability advances; older bridges are blocked from these new intents while emergency
  exits remain usable. No protocol field defaults a reduction into an entry.

## State machine

1. **Validate:** authoritative position, working native orders, ownership, prices, desired total and
   new-plan overlap checks. Snapshot IDs/revisions and identify a single transaction.
2. **Plan:** retain unaffected native pairs; calculate exact target/stop changes, any pair splits and
   required confirmation dependencies. No native mutations yet.
3. **Apply:** issue only changes whose capacity/protection prerequisites are already satisfied.
4. **Await confirmation:** order and execution callbacks advance the transaction. Each callback checks
   position epoch, deduplication and conservation before allowing the next mutation.
5. **Reconcile:** compare actual remaining position with app-owned native allocations. A filled slot
   is retired exactly once. Stop/target ownership remains consistent after each accepted transition.
6. **Complete or abort:** publish clear state/acknowledgment to the DOM; retain native protection and
   diagnostics on failure, with no delayed opening order.

Rapid repeated clicks cannot start overlapping reallocations. They are visibly refused while a
transaction is unresolved. The transaction does not depend on the DOM's repaint timer for safety.

## Scope boundaries

This change manages app-owned orders, not a position concurrently controlled by another strategy,
ATM, copier or independent interface. Conflicts must be reported rather than hidden. Native exchange
and broker OCO guarantees are not created by this application and must be validated on SIM. Beta stays
on; no claim of universal reduce-only or race-free native execution follows from local unit tests.

Existing protected positions must be adopted only when native order ownership and allocation can be
proven from current snapshots. Unknown/legacy or externally edited orders are not silently rebuilt.
The user must receive a precise refusal and retain emergency/manual management when adoption is unsafe.

## Verification required before packaging

Write failing regressions first using actual production bridge source and the existing isolated API
simulator, extended to delay, reject, reorder and partially fulfill Cancel/Change/Submit callbacks.
Do not rely only on its current synchronous happy-path behavior.

- Reproduce the exact long-12, SELL-6 case and its short-side mirror: position decreases; no opposite
  bracket is created; exit allocation and remaining stops are consistent.
- SCALE IN from flat and same-direction positions preserves existing exposure and protects only added
  fills. Opposite-direction and through-flat requests are refused.
- SCALE OUT partial/full position, all supported counts/skews, contracts/percent sizing, retained
  original targets, partial fills, multiple prices and exact whole-contract totals.
- Fill-before-cancel, fill-during-change, rejected change/cancel, duplicate execution, late callback,
  repeated clicks, stale position, account switch, disconnect/reconnect and timeout.
- Verify the required safety/protection invariants at every simulator event boundary, not only at the
  end. Explicitly fail if active/releasing/replacement allocations can independently over-exit exposure.
- Drag/cancel existing targets and stops without creating entries. Test flat cleanup, B/E, stop guard,
  close half and manual reductions against pending scale-out intent.
- Run the complete solution suite and BridgeCheck against installed NinjaTrader assemblies; include
  both application and bridge tests in packaging. Review the lifecycle code independently.
- Deliver an updated native SIM checklist covering both sides, delayed/partial fills, existing
  brackets and cancellation races. Do not send test orders without explicit SIM authorization.

## Review checkpoint

Approval requested for the bridge-owned allocation approach and the explicit refusal policy when a
safe split/adoption cannot be established. After written-design approval, prepare the implementation
plan before product code changes. No executable/source behavior has changed at this checkpoint.
