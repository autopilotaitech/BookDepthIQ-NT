namespace BookDepthIQ.Core;

/// <summary>One side of the book: prices in integer ticks, best first.</summary>
public sealed record BookSide(long[] Ticks, long[] Sizes)
{
    public static readonly BookSide Empty = new(Array.Empty<long>(), Array.Empty<long>());
    public long SizeAt(long tick)
    {
        var i = Array.IndexOf(Ticks, tick);
        return i < 0 ? 0 : Sizes[i];
    }
}

/// <summary>A depth snapshot. Prices are integer ticks (price / tickSize, rounded).</summary>
public sealed record Book(BookSide Bids, BookSide Asks, long TimeMs)
{
    public static readonly Book Empty = new(BookSide.Empty, BookSide.Empty, 0);
    public long? BestBid => Bids.Ticks.Length > 0 ? Bids.Ticks[0] : null;
    public long? BestAsk => Asks.Ticks.Length > 0 ? Asks.Ticks[0] : null;

    public static long ToTicks(double price, double tickSize) => (long)Math.Round(price / tickSize);
    public static double ToPrice(long ticks, double tickSize) => ticks * tickSize;

    /// <summary>Builds a book from [price, size] pairs as the bridge sends them.</summary>
    public static Book FromLevels(IEnumerable<double[]> bids, IEnumerable<double[]> asks, double tickSize, long timeMs)
    {
        static BookSide Side(IEnumerable<double[]> lv, double ts, bool desc)
        {
            var rows = lv.Where(r => r.Length >= 2 && r[1] > 0).Select(r => (t: ToTicks(r[0], ts), s: (long)r[1]));
            rows = desc ? rows.OrderByDescending(r => r.t) : rows.OrderBy(r => r.t);
            var a = rows.ToArray();
            return new BookSide(a.Select(r => r.t).ToArray(), a.Select(r => r.s).ToArray());
        }
        return new Book(Side(bids, tickSize, true), Side(asks, tickSize, false), timeMs);
    }
}

/// <summary>A print on the tape.</summary>
public sealed record Trade(long Tick, long Size, char Side, long TimeMs);
