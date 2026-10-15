# App ↔ NT8 bridge protocol (v2)

TCP `127.0.0.1:36971`, one JSON object per line (UTF-8, `\n`). The bridge AddOn listens; the DOM app
connects. Loopback only: the bridge never listens on another interface.

## Bridge → app

| `t` | Fields | When |
|---|---|---|
| `hello` | `v`, `orderVersion`, `accounts: [{name, conn}]` | on connect; `orderVersion >= 2` is required for new Ticket orders |
| `accounts` | `v`, `orderVersion`, `accounts: [{name, conn}]` | discovery reply and account/connection changes; not a new bridge session |
| `inst` | `name`, `tick` (TickSize), `pv` (PointValue), `desc` | after `sub` |
| `book` | `bids: [[price, size]…]`, `asks: […]`, `ms` | depth changed (coalesced, ≤ 20/s, every level NinjaTrader holds, up to 1000 a side) |
| `quote` | `bid`, `ask`, `last` | best bid/ask/last changed |
| `trade` | `p`, `v`, `side` (`b` at/above ask, `s` at/below bid, `?`), `ms` | each Last print |
| `account` | `name`, `cash`, `realized`, `unrealized`, `netliq`, `trailDd`, `dailyLimit`, `commission` | account items changed |
| `pos` | `acct`, `inst`, `qty` (signed), `avg` | position changed (qty 0 = flat) |
| `orders` | `acct`, `list: [{id, inst, action, type, qty, filled, limit, stop, state, oco, name}]` | any order update |
| `exec` | `acct`, `inst`, `id`, `name` (order name), `side` (`b`/`s`), `qty`, `price`, `commission`, `fee`, `ms` | selected account executions, plus pending bridge entries retained on a previously selected account; app filters `acct` |
| `hist` | `inst`, `trades: [[ms, price, size, side]…]`, `done`, `err?` | answer to `hist`, in chunks of 5,000 |
| `reject` | `name`, `inst`, `action`, `type`, `qty`, `error`, `msg` | NinjaTrader rejected an order (its own reason) |
| `ack` | `cid`, `ok`, `msg` | answer to a command |
| `log` | `msg` | bridge notices (connection, errors) |

## App → bridge

| `t` | Fields |
|---|---|
| `sub` | `inst` — NT instrument name, e.g. `NQ 12-26` |
| `acct` | `name` — account to stream (`pos`, `orders`, `account`) |
| `order` | `cid`, `acct`, `inst`, `side` (`buy`/`sell`), `type` (`market`/`limit`/`stop`), `qty`, `price` (limit/stop), `targets: [[ticks, qty, optionalOriginalTargetNumber]…]`, `sl` (ticks, 0 = none), `reduceOnly` (bridge reduction intent, not a native/exchange flag) |
| `closePart` | `cid`, `acct`, `inst`, `qty` — market exit of part of the position; bracket legs trimmed from the last target |
| `addStop` | `cid`, `acct`, `inst`, `price`, `qty` (0 = whole position) |
| `hist` | `inst`, `hours` (1–48) — past prints from NT's tick history |
| `kill` | `cid`, `acct` — flatten every position and cancel every order on the account |
| `cancel` | `cid`, `acct`, `id` |
| `move` | `cid`, `acct`, `id`, `price` |
| `flatten` | `cid`, `acct`, `inst` |
| `cancelAll` | `cid`, `acct`, `inst` |

Brackets (like NT's ATM): each execution of an entry is allocated to its targets in order; each
slice gets a TP limit (`BDIQ TP1`, `BDIQ TP2` …) and an SL stop (`BDIQ SL1` …) as one OCO pair,
priced in ticks from that execution's fill, so a partial fill is protected at once.

SCALE child slices optionally include their original 1-based target number: `[64,1,2]` remains
`BDIQ TP2` / `BDIQ SL2` even when it is the child's only target. Legacy pairs retain their list index.
The DOM requires `orderVersion >= 2` before new orders, and complete fresh balance, selected-instrument
position and orders snapshots. Session loss or a new `hello` invalidates readiness and pending reverse
intent synchronously. Every instrument subscription also re-sends position/order snapshots.

Opposite-side orders that reduce existing exposure do not receive entry brackets. `reduceOnly:true`
rejects a command when the original reducing direction is no longer valid, and pending reduction
quantities are checked against remaining exposure. Owned protection shrinks/cancels after confirmed
position changes, and pending protection retries after order acknowledgments. Flat invalidates queued
protection for that account/instrument. None of this provides atomic native reduce-only execution;
simultaneous fills can race native cancellations/changes. Emergency flatten/cancel remain available.

The bridge never decides to trade. It executes what the app sends: a click, or a management feature
the user turned on in the DOM (B/E after TP1, trailing stop, stop guard, weekend close, close at the
daily loss limit) — none of which ever opens a position.
