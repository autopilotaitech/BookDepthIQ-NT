# Chart order overlays — beta4 verification

Owner approved a bounded, display-only overlay: entry/TP/SL price and remaining quantity, position
average on both charts, selected account/instrument only, per-chart hide setting. No chart order
placement, order dragging, cancellation, bracket management or new native protocol.

Branch: `codex/chart-order-overlays`; package version `1.13.0-beta.4`.

## Isolated evidence

- Projection tests were written before the model existed and failed, then passed with implementation.
- Native snapshot readiness, matching account/contract and connected-account checks gate all lines.
  Account switches and disconnects produce an empty projection, even if old lists remain allocated.
- Partial fills use quantity minus filled; order changes replace price/quantity; cancel/flat clear
  corresponding display. Different contracts, terminal states and non-finite prices are omitted.
- Stop-limit trigger/limit are separate. Review found MIT initially used the wrong field; a regression
  failed before the fix, then passed using the native Stop trigger. Existing ladder/order code untouched.
- Headless bitmap comparison proves overlays render, hide setting restores the original candle image,
  and adding them does not change viewport anchoring. Both chart controls clear when source clears.
- The real Ticket harness command sink stays empty while the projection reads a working partial target.
- Full suite:297 passed (264 core/DOM +33 bridge-source). Release build passes.

## Independent review

Read-only reviewer found one important MIT display-price issue, fixed with a failing-before-fix test.
No chart order-submission path was found. Native event sequencing, installed-package behavior and
actual broker execution were not certified. No native SIM/LIVE orders were submitted by these tests.

## Manual acceptance remaining

With the owner's own SIM trades, confirm both panes follow the selected account/contract; resting
entries, targets and stops show actual prices/remaining quantities; ladder moves update the chart;
partial exits resize labels; filled/cancelled orders and flat average disappear. Confirm per-pane hide,
offscreen arrow tags and crowded-level grouping. Do not interpret displayed protection as a guarantee
that an exchange/broker has accepted it: the source is the same native order snapshot as the DOM.

App-only upgrade from beta2/beta3; included NT AddOn sources are unchanged. No installation/settings
reset or native recompile is performed by this build workflow.
