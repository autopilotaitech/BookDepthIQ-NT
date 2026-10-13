# App ↔ NT8 bridge protocol (v2)

TCP `127.0.0.1:36971`, one JSON object per line (UTF-8, `\n`). The bridge AddOn listens; the DOM app
connects. Loopback only: the bridge never listens on another interface.

## Bridge → app

| `t` | Fields | When |
|---|---|---|
| `hello` | `v`, `accounts: [{name, conn}]` | on connect, and when accounts/connections change |
| `inst` | `name`, `tick` (TickSize), `pv` (PointValue), `desc` | after `sub` |
| `book` | `bids: [[price, size]…]`, `asks: […]`, `ms` | depth changed (coalesced, ≤ 20/s, top 40 levels) |
| `quote` | `bid`, `ask`, `last` | best bid/ask/last changed |
| `trade` | `p`, `v`, `side` (`b` at/above ask, `s` at/below bid, `?`), `ms` | each Last print |
| `account` | `name`, `cash`, `realized`, `unrealized`, `netliq`, `trailDd`, `dailyLimit`, `commission` | account items changed |
| `pos` | `acct`, `inst`, `qty` (signed), `avg` | position changed (qty 0 = flat) |
| `orders` | `acct`, `list: [{id, inst, action, type, qty, filled, limit, stop, state, oco, name}]` | any order update |
| `exec` | `acct`, `inst`, `id`, `name` (order name), `side` (`b`/`s`), `qty`, `price`, `commission`, `fee`, `ms` | each execution on the streamed account |
| `hist` | `inst`, `trades: [[ms, price, size, side]…]`, `done`, `err?` | answer to `hist`, in chunks of 5,000 |
| `reject` | `name`, `inst`, `action`, `type`, `qty`, `error`, `msg` | NinjaTrader rejected an order (its own reason) |
| `ack` | `cid`, `ok`, `msg` | answer to a command |
| `log` | `msg` | bridge notices (connection, errors) |

## App → bridge

| `t` | Fields |
|---|---|
| `sub` | `inst` — NT instrument name, e.g. `NQ 12-26` |
| `acct` | `name` — account to stream (`pos`, `orders`, `account`) |
| `order` | `cid`, `acct`, `inst`, `side` (`buy`/`sell`), `type` (`market`/`limit`/`stop`), `qty`, `price` (limit/stop), `targets: [[ticks, qty]…]`, `sl` (ticks, 0 = none) |
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

The bridge never decides to trade. It executes what the app sends: a click, or a management feature
the user turned on in the DOM (B/E after TP1, trailing stop, stop guard, weekend close, close at the
daily loss limit) — none of which ever opens a position.
