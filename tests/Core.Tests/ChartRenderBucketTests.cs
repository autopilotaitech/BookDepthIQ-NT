using BookDepthIQ.Core.Charts;
using Xunit;

public class ChartRenderBucketTests
{
    [Fact] public void Dense_indicator_envelopes_keep_spikes_instead_of_sampling_them_away()
    {
        var candles=ChartRenderBuckets.Build(Bars(100),0,100,100,20);
        var values=Enumerable.Repeat<double?>(10,100).ToArray();values[7]=999;
        var spans=ChartRenderBuckets.Study(values,candles);
        Assert.Equal(10,spans.Count);Assert.Equal(999,spans[0].High);Assert.Equal(10,spans[0].Low);Assert.Equal(10,spans[0].Open);Assert.Equal(10,spans[0].Close);
    }
    static ChartBar[] Bars(int count)=>Enumerable.Range(0,count).Select(i=>new ChartBar(i,DateTimeOffset.UnixEpoch.AddMinutes(i),new(2026,10,15),i*10,i*10+8,i*10-3,i*10+5,1,1,false)).ToArray();
    [Fact] public void Dense_pixel_buckets_preserve_first_open_last_close_extrema_and_total_volume()
    {
        var buckets=ChartRenderBuckets.Build(Bars(100),0,100,100,20);
        Assert.Equal(10,buckets.Count);var first=buckets[0];
        Assert.Equal(0,first.First);Assert.Equal(9,first.Last);Assert.Equal(0,first.Open);Assert.Equal(95,first.Close);Assert.Equal(98,first.High);Assert.Equal(-3,first.Low);Assert.Equal(10,first.Volume);
        Assert.Equal(100,buckets.Sum(b=>b.Volume));Assert.Equal(99,buckets[^1].Last);
    }
    [Fact] public void Ten_thousand_native_bars_are_bounded_by_pixel_width_not_history_size()
    {
        var bars=Bars(10000);var buckets=ChartRenderBuckets.Build(bars,0,10000,10000,500);
        Assert.Equal(250,buckets.Count);Assert.Equal(10000,buckets.Sum(b=>b.Volume));Assert.Equal(99998,buckets.Max(b=>b.High));
        Assert.Equal(10000,bars.Length);Assert.Equal(99995,bars[^1].Close);
    }
    [Fact] public void Normal_zoom_keeps_each_native_candle_and_clips_to_visible_source_range()
    {
        var buckets=ChartRenderBuckets.Build(Bars(100),20,40,20,500);
        Assert.Equal(20,buckets.Count);Assert.All(buckets,b=>Assert.Equal(b.First,b.Last));Assert.Equal(20,buckets[0].First);Assert.Equal(39,buckets[^1].Last);
    }
}
