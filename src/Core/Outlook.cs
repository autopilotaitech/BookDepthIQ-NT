namespace BookDepthIQ.Core;

public sealed record HoldOutcome(long Level, bool Bid, long Started, long Ended, bool Held);

/// <summary>One non-overlapping 30-second observation: did trades cross the observed level?</summary>
public sealed class OutlookRecorder
{
    private (long Level, bool Bid, long At)? active;
    public bool Active => active.HasValue;
    public void Start(long level, bool bid, long at)
    {
        if (!Active) active = (level, bid, at);
    }
    public HoldOutcome? OnTrade(long price, long at)
    {
        if (active is not { } a) return null;
        var crossed = a.Bid ? price < a.Level : price > a.Level;
        if (!crossed && at - a.At < 30_000) return null;
        active = null;
        return new(a.Level, a.Bid, a.At, at, !crossed);
    }
    public void Reset() => active = null;
}
