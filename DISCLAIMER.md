# Disclaimer

**Trading futures involves substantial risk of loss and is not suitable for every investor.** You
can lose more than you deposit. Past results, including any win rates quoted in this software or its
documentation, do not guarantee future results.

- **Not financial advice.** BookDepthIQ DOM is a charting and order-entry tool. Nothing it shows
  (SuperTrend reads, scalp reads, GEX levels, opening range, absorption, warnings) is a
  recommendation to buy or sell anything.
- **You place every order.** The DOM sends an order only when you click, except for the management
  you turn on yourself (B/E after TP1, trailing stop, stop guard, close at the daily limit, close
  before the weekend halt). Those only reduce or protect a position you opened.
- **No warranty.** The software is provided "as is" (see [LICENSE](LICENSE)). Software, data feeds,
  networks and brokers fail. Check every order and position in NinjaTrader itself, and test on a
  simulated account (Sim101) before trading real money.
- **Prop-firm rules are yours to check.** The drawdown, daily-loss and room numbers are computed from
  the values you type in. Your firm's own numbers are the ones that count.

## Third parties

- **NinjaTrader** is a registered trademark of NinjaTrader, LLC. This project is not affiliated with,
  endorsed by or supported by NinjaTrader. It uses NinjaTrader 8's public NinjaScript AddOn API.
- **GEX levels** are computed on your own computer from the delayed option quotes Cboe publishes at
  `cdn.cboe.com`. Each copy of the DOM fetches them itself; this project does not store or
  redistribute Cboe data. Cboe's terms of use apply to that data. Dealer positioning is inferred,
  not observed, and the data is delayed.
- **CME** contract specifications (tick size, tick value) are public product facts; CME Group is not
  affiliated with this project.
