using EmbyRadarr.Radarr;

namespace EmbyRadarr.Tests;

public class ResponseCacheTests
{
    private DateTime now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    private readonly ResponseCache cache;
    private int calls;

    public ResponseCacheTests()
    {
        this.cache = new ResponseCache(() => this.now);
    }

    private Task<int> Fetch() => Task.FromResult(++this.calls);

    [Fact]
    public async Task Reuses_value_until_ttl_expires()
    {
        var ttl = TimeSpan.FromMinutes(2);

        Assert.Equal(1, await this.cache.GetOrFetch("k", ttl, this.Fetch));
        this.now = this.now.AddSeconds(119);
        Assert.Equal(1, await this.cache.GetOrFetch("k", ttl, this.Fetch));
        this.now = this.now.AddSeconds(2);
        Assert.Equal(2, await this.cache.GetOrFetch("k", ttl, this.Fetch));
    }

    [Fact]
    public async Task Failures_are_cached_for_30_seconds()
    {
        Task<int> Fail() { this.calls++; throw new RadarrException("offline"); }

        await Assert.ThrowsAsync<RadarrException>(() => this.cache.GetOrFetch("k", TimeSpan.FromMinutes(2), Fail));
        await Assert.ThrowsAsync<RadarrException>(() => this.cache.GetOrFetch("k", TimeSpan.FromMinutes(2), Fail));
        Assert.Equal(1, this.calls);

        this.now = this.now.AddSeconds(31);
        Assert.Equal(2, await this.cache.GetOrFetch("k", TimeSpan.FromMinutes(2), this.Fetch));
    }

    [Fact]
    public async Task Concurrent_callers_share_one_request()
    {
        var gate = new TaskCompletionSource<int>();
        Task<int> Slow() { Interlocked.Increment(ref this.calls); return gate.Task; }

        var t1 = this.cache.GetOrFetch("k", TimeSpan.FromMinutes(1), Slow);
        var t2 = this.cache.GetOrFetch("k", TimeSpan.FromMinutes(1), Slow);
        gate.SetResult(42);

        Assert.Equal(new[] { 42, 42 }, await Task.WhenAll(t1, t2));
        Assert.Equal(1, this.calls);
    }

    [Fact]
    public async Task Clear_forces_refetch()
    {
        await this.cache.GetOrFetch("k", TimeSpan.FromMinutes(5), this.Fetch);
        this.cache.Clear();

        Assert.Equal(2, await this.cache.GetOrFetch("k", TimeSpan.FromMinutes(5), this.Fetch));
    }
}
