# App ↔ NT8 bridge protocol (v1)

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
| `ack` | `cid`, `ok`, `msg` | answer to a command |
| `log` | `msg` | bridge notices (connection, errors) |

## App → bridge

| `t` | Fields |
|---|---|
| `sub` | `inst` — NT instrument name, e.g. `NQ 12-26` |
| `acct` | `name` — account to stream (`pos`, `orders`, `account`) |
| `order` | `cid`, `acct`, `inst`, `side` (`buy`/`sell`), `type` (`market`/`limit`/`stop`), `qty`, `price` (limit/stop), `tp`, `sl` (ticks, 0 = none) |
| `cancel` | `cid`, `acct`, `id` |
| `move` | `cid`, `acct`, `id`, `price` |
| `flatten` | `cid`, `acct`, `inst` |
| `cancelAll` | `cid`, `acct`, `inst` |

Brackets: when an `order` with `tp`/`sl` fills, the bridge submits a TP limit and an SL stop for
the filled quantity as one OCO pair, priced in ticks from the average fill. Nothing else is ever
sent without a command from a click.
