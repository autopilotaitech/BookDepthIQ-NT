namespace BookDepthIQ.Core.Charts;

public sealed record ChartBar(int Index,DateTimeOffset Time,DateOnly TradingDate,long Open,long High,long Low,long Close,long Volume,long Revision,bool Forming);
