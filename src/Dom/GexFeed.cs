using System.Globalization;
using System.Net;
using System.Text.Json;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

/// <summary>
/// Pulls the CBOE delayed option chain behind the DOM's instrument (ES→SPX, NQ→QQQ, YM→DIA, GC→GLD,
/// CL→USO) every 60 s on its own task, never on the UI thread and never near the order path. Conditional
/// GET (If-None-Match → 304, zero bytes) with gzip, as BookDepthIQ measured. 403/404 = the symbol is not
/// served: stop polling. Failures back off to 5 minutes. The frozen vintage and its conversion are cached
/// in %APPDATA%\BookDepthIQ-NT\gex-SYMBOL.json so a restart does not move the lines.
/// </summary>
public sealed class GexFeed : IDisposable
{
    private static readonly HttpClient Http = MakeClient();
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private CancellationTokenSource? cts;
    private string? instrument;
    private double? savedBasis;

    public GexStore? Store { get; private set; }
    /// <summary>What the feed is doing, for the GEX read's tooltip.</summary>
    public string Status { get; private set; } = "off";
    /// <summary>Last answer from CBOE (200 or 304), UTC.</summary>
    public DateTime? LastOk { get; private set; }
    /// <summary>The payload's own timestamp (UTC).</summary>
    public DateTime? PayloadUtc { get; private set; }

    private static HttpClient MakeClient()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { Timeout = TimeSpan.FromSeconds(30) };
        // The CDN does not serve a default agent.
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; BookDepthIQ)");
        return h;
    }

    /// <summary>Point the feed at an instrument (cheap when nothing changed). A new contract month on the
    /// same chain keeps the levels and drops the basis; a new chain restarts the poller.</summary>
    public void Follow(string inst)
    {
        if (inst == instrument) return;
        var src = GexSource.ForRoot(CmeSpecs.RootOf(inst));
        var sameChain = Store != null && src != null && Store.Source.Symbol == src.Symbol;
        instrument = inst;
        if (sameChain)
        {
            Store!.ResetBasis();
            return;
        }
        Stop();
        if (src == null)
        {
            Status = $"no option chain for {CmeSpecs.RootOf(inst)} (GEX covers ES, NQ, YM, GC, CL and micros)";
            return;
        }
        var store = new GexStore(src);
        Store = store;
        Restore(store);
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        Status = $"loading {src.Name} options…";
        _ = Task.Run(() => Loop(store, ct), ct);
    }

    public void Stop()
    {
        cts?.Cancel();
        cts = null;
        Store = null;
        instrument = null;
        LastOk = null;
        PayloadUtc = null;
        Status = "off";
    }

    public void Dispose() => Stop();

    private async Task Loop(GexStore store, CancellationToken ct)
    {
        string? etag = null;
        var backoff = 1;
        while (!ct.IsCancellationRequested)
        {
            var wait = 60;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, store.Source.Url);
                if (etag != null) req.Headers.TryAddWithoutValidation("If-None-Match", etag);
                using var resp = await Http.SendAsync(req, ct);
                var code = (int)resp.StatusCode;
                if (code == 304) { LastOk = DateTime.UtcNow; backoff = 1; }
                else if (code is 403 or 404)
                {
                    Status = $"{store.Source.Name} is not served by CBOE (HTTP {code}) — stopped";
                    return;
                }
                else if (code is < 200 or >= 300) throw new HttpRequestException($"HTTP {code}");
                else
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    if (Gex.ParsePayload(body) is not var (ts, spot, iv30, contracts)) throw new InvalidDataException("payload did not parse");
                    store.NoteSpot(spot);
                    var snap = Gex.Compute(store.Source.Symbol, ts, spot, iv30, contracts);
                    if (store.Accept(snap)) Save(store);
                    etag = resp.Headers.ETag?.ToString();
                    PayloadUtc = DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
                    LastOk = DateTime.UtcNow;
                    backoff = 1;
                    Status = $"{store.Source.Name} · {contracts.Count:N0} contracts · spot {spot:0.##}";
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                // Keep the previous snapshot; try again with a growing wait.
                Status = $"{store.Source.Name}: {e.Message} — keeping the last levels, retry in {Math.Min(backoff, 300)} s";
                wait = Math.Min(backoff, 300);
                backoff = Math.Min(backoff * 2, 300);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(wait), ct); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Feed one live futures price (book mid). Persists the conversion once it first freezes.</summary>
    public void SampleBasis(double futures)
    {
        if (Store is not { } st) return;
        st.SampleBasis(futures);
        if (st.FrozenBasis != savedBasis) Save(st);
    }

    private sealed record Cached(GexSnapshot Snapshot, double? Basis);

    private static string PathFor(GexSource src) => Path.Combine(Settings.Dir, $"gex-{src.Name}.json");

    private void Save(GexStore st)
    {
        if (st.Frozen is not { } snap) return;
        savedBasis = st.FrozenBasis;
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            File.WriteAllText(PathFor(st.Source), JsonSerializer.Serialize(new Cached(snap, savedBasis), Json));
        }
        catch { /* the levels are still right in memory */ }
    }

    private void Restore(GexStore st)
    {
        try
        {
            var p = PathFor(st.Source);
            if (!File.Exists(p)) return;
            if (JsonSerializer.Deserialize<Cached>(File.ReadAllText(p), Json) is { } c && st.Restore(c.Snapshot, c.Basis))
                savedBasis = c.Basis;
        }
        catch { /* a bad cache is ignored; the next poll computes fresh */ }
    }
}
