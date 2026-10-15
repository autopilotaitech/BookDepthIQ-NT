namespace BookDepthIQ.Core;

public sealed record JournalPerformance(int Count, int Wins, int Losses, int Longs, int Shorts,
    double Net, double Fees, double? WinRate, double? ProfitFactor, double? Expectancy,
    double MaxDrawdown, double LargestWin, double LargestLoss, int WinStreak, int LossStreak,
    double AverageMinutes, double? Sharpe, double? Sortino, double[] Equity);
public sealed record RecordedDayTotals(string Day, double Net, int Trades, long? LastLossAt);

public static class JournalAnalytics
{
    public static DateOnly TradingDate(long ms) => DateOnly.ParseExact(Protect.TradingDay(ms),"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
    public static RecordedDayTotals RecordedDay(IEnumerable<JournalTrade> source,string account,long now)
    {
        var day=Protect.TradingDay(now); double net=0; int count=0; long? lastLoss=null;
        foreach(var t in source)
        {
            if(t.Account!=account || !t.Exit.HasValue || t.Close>now || Protect.TradingDay(t.Close)!=day) continue;
            net+=t.Pnl; count++;
            if(t.Pnl<0 && (lastLoss==null || t.Close>lastLoss.Value)) lastLoss=t.Close;
        }
        return new(day,net,count,lastLoss);
    }
    public static string Environment(string account) => account.StartsWith("Playback", StringComparison.OrdinalIgnoreCase) ? "PLAYBACK"
        : account.StartsWith("Sim", StringComparison.OrdinalIgnoreCase) ? "SIM" : "LIVE";
    public static DateOnly Day(long ms) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
        DateTimeOffset.FromUnixTimeMilliseconds(ms), Session.Zone("America/Chicago")).DateTime);

    public static JournalPerformance Compute(IEnumerable<JournalTrade> source)
    {
        var trades = source.Where(t => t.Exit.HasValue).OrderBy(t => t.Close).ToArray();
        var wins = trades.Where(t => t.Pnl > 0).ToArray();
        var losses = trades.Where(t => t.Pnl < 0).ToArray();
        var profit = wins.Sum(t => t.Pnl);
        var loss = -losses.Sum(t => t.Pnl);
        double equity = 0, peak = 0, dd = 0;
        int ws = 0, ls = 0, maxWs = 0, maxLs = 0;
        var curve = new List<double> { 0 };
        foreach (var t in trades)
        {
            equity += t.Pnl; peak = Math.Max(peak, equity); dd = Math.Max(dd, peak - equity); curve.Add(equity);
            ws = t.Pnl > 0 ? ws + 1 : 0; ls = t.Pnl < 0 ? ls + 1 : 0;
            maxWs = Math.Max(maxWs, ws); maxLs = Math.Max(maxLs, ls);
        }
        var mean = trades.Length > 0 ? equity / trades.Length : 0;
        var sd = trades.Length > 1 ? Math.Sqrt(trades.Sum(t => Math.Pow(t.Pnl - mean, 2)) / (trades.Length - 1)) : 0;
        var downside = losses.Length >= 2 ? Math.Sqrt(losses.Average(t => t.Pnl * t.Pnl)) : 0;
        return new(trades.Length, wins.Length, losses.Length, trades.Count(t => t.Long), trades.Count(t => !t.Long),
            equity, trades.Sum(t => t.FeesUsd), trades.Length > 0 ? (double)wins.Length / trades.Length : null,
            loss > 0 ? profit / loss : null, trades.Length > 0 ? mean : null, dd,
            wins.Select(t => t.Pnl).DefaultIfEmpty(0).Max(), losses.Select(t => t.Pnl).DefaultIfEmpty(0).Min(), maxWs, maxLs,
            trades.Length > 0 ? trades.Average(t => Math.Max(0, t.Close - t.Open) / 60000.0) : 0,
            trades.Length >= 20 && sd > 0 ? mean / sd : null, trades.Length >= 20 && downside > 0 ? mean / downside : null, curve.ToArray());
    }
}
