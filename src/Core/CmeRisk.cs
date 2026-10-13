namespace BookDepthIQ.Core;

// The risk calculator in CME terms: whole contracts, ticks and tick value, commissions per side.
// Pure: no I/O. Port of BookDepthIQ-Vest src/panel/riskcalc.ts, with perp units/leverage replaced
// by contracts. A prop account FAILS when equity incl. open P&L reaches its drawdown floor; that
// floor, not margin, is the binding limit (see Drawdown.cs).

public enum SizeMode
{
    /// <summary>A fixed number of contracts.</summary>
    Contracts,
    /// <summary>Size from $ risk at the stop (fees in).</summary>
    RiskUsd,
    /// <summary>Size from a % of the room left to the fail floor.</summary>
    RoomPct,
}

public sealed record RiskInput
{
    public double TickValue { get; init; }
    /// <summary>$ the account may still lose before it fails (equity − floor).</summary>
    public double RoomUsd { get; init; }
    public int StopTicks { get; init; }
    public int TargetTicks { get; init; }
    /// <summary>Commission + exchange/NFA fees per contract per side, $.</summary>
    public double FeePerSide { get; init; }
    public SizeMode Mode { get; init; } = SizeMode.RoomPct;
    /// <summary>Contracts (Contracts), $ (RiskUsd) or % (RoomPct).</summary>
    public double Input { get; init; }
    /// <summary>The account's or your own max contracts (0 = no limit).</summary>
    public int MaxContracts { get; init; }
}

public sealed record RiskOut(
    int Contracts,
    /// <summary>Risk sizing wanted more than MaxContracts; cut to it.</summary>
    bool Capped,
    /// <summary>The $ risk asked for buys less than one contract at this stop.</summary>
    bool BelowOne,
    double RiskWanted,
    double Fees,
    /// <summary>$ lost at the stop / won at the target, fees included.</summary>
    double Loss,
    double Win,
    double RR,
    /// <summary>Ticks against the entry before the account fails (∞ when flat).</summary>
    double TicksToFail,
    bool StopPastFail,
    double RoomUsed,
    int LosersLeft,
    /// <summary>$ per tick for this size.</summary>
    double DollarsPerTick);

public static class CmeRisk
{
    public static RiskOut Calc(RiskInput i)
    {
        var perContractStop = i.StopTicks * i.TickValue + 2 * i.FeePerSide;
        var riskWanted = i.Mode switch
        {
            SizeMode.RiskUsd => i.Input,
            SizeMode.RoomPct => Math.Max(0, i.RoomUsd) * i.Input / 100.0,
            _ => 0,
        };
        int c;
        var belowOne = false;
        if (i.Mode == SizeMode.Contracts) c = (int)Math.Floor(Math.Max(0, i.Input) + 1e-9);
        else if (perContractStop <= 0 || i.StopTicks <= 0) c = 0;
        else
        {
            c = (int)Math.Floor(riskWanted / perContractStop + 1e-9);
            belowOne = c == 0 && riskWanted > 0;
        }
        var capped = false;
        if (i.MaxContracts > 0 && c > i.MaxContracts)
        {
            c = i.MaxContracts;
            capped = true;
        }
        var fees = c * 2 * i.FeePerSide;
        var loss = c * i.StopTicks * i.TickValue + fees;
        var win = c * i.TargetTicks * i.TickValue - fees;
        var dpt = c * i.TickValue;
        var ticksToFail = dpt > 0 ? Math.Max(0, i.RoomUsd) / dpt : double.PositiveInfinity;
        return new RiskOut(
            c, capped, belowOne, riskWanted, fees, loss, win,
            loss > 0 ? win / loss : 0,
            ticksToFail,
            c > 0 && i.StopTicks > 0 && i.StopTicks >= ticksToFail,
            i.RoomUsd > 0 ? loss / i.RoomUsd : loss > 0 ? double.PositiveInfinity : 0,
            loss > 0 ? (int)Math.Floor(Math.Max(0, i.RoomUsd) / loss + 1e-9) : int.MaxValue,
            dpt);
    }

    /// <summary>Round-trip cost in ticks for one contract (break-even move before any edge).</summary>
    public static double BreakEvenTicks(double tickValue, double feePerSide) => tickValue > 0 ? 2 * feePerSide / tickValue : 0;
}
