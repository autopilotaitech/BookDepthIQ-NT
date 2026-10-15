# No-chase guide audit — 1.12.13

## Scope and outcome

Owner requested hiding the yellow guide/label and questioned the logic. This patch hides it by
default, retaining ST1/ST2. Optional Settings › Indicators › Chart lines › Show no-chase level restores
it only when SuperTrend lines are also on. Older settings have no ShowNoChase field and default OFF;
the next normal settings save persists the chosen value. No forced appdata reset or migration write.

## What the rule does

`MainWindow.Orders.cs` / `LadderLines`: take ST1's line in ticks, add 2*ST1 ATR for Up, subtract it
for Down, then round to a tick. Requires a non-neutral ST1, a line and positive ATR. It does not use
liquidity, order flow, ADX, expected profit, account risk or a predicted price path. It is a visual
distance reference, not evidence that a manual entry is good or bad.

`Structure.ScalpNearAtr` is also used by `Structure.Scalp` to classify stretched distance at 2 ATR.
That calculation is consumed by RefreshTop's scalp read; the scalp pill is not attached to the
current header reads. No entry/management command handler consumes that scalp classification.
The older scalp tooltip's historical win-rate wording and 3-ATR wording are not established by this
audit; they must not be taken as validation of the 2-ATR guide.

EXTENDED uses a different existing rule: at least 2 VWAP sigma or 3 ST1 ATR. It remains unchanged.
ADX CHOP/TREND remains independent. Stops/targets and management use their existing user settings;
the ShowNoChase flag is read only when building the chart overlay. No trading logic changed here.

## Evidence and limits

254 isolated automated tests pass: 226 core/DOM and 28 bridge-source. New cases exercise warmed
up/down SuperTrend data, verify no guide by default while ST1/ST2 remain, exact original opt-in
distance, OFF/ST-hidden behavior, old settings JSON and persisted opt-in, and actual Settings toggle
application. These tests use in-memory commands and do not send orders to NinjaTrader.

This is a display change and code-path audit, not a backtest or statistical validation. No native
SIM/LIVE execution certification or profitability claim is made. The larger Ticket beta checklist
remains outstanding; bridge and order protocol are unchanged.
