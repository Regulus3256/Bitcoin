using BtcApi.Application.Services;
using BtcApi.Tests.TestDoubles;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace BtcApi.Tests.Application;

public sealed class CachedBtcMarketDataServiceTests
{
    [Fact]
    public async Task GetHistoryAsync_NormalizesArgumentsAndClampsLimit()
    {
        var repository = new InMemoryCandleRepository();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new CachedBtcMarketDataService(repository, cache);

        await service.GetHistoryAsync(" btcusd ", "4h", 10, 20, 30, 1, CancellationToken.None);

        Assert.Equal("BTC", repository.LastInstrument?.Code);
        Assert.Equal("4h", repository.LastTimeframe?.Key);
        Assert.Equal(10, repository.LastFrom);
        Assert.Equal(20, repository.LastThrough);
        Assert.Equal(30, repository.LastBefore);
        Assert.Equal(20, repository.LastLimit);
    }

    [Fact]
    public async Task GetHistoryAsync_CachesIdenticalQueries()
    {
        var repository = new InMemoryCandleRepository
        {
            CandlesToReturn = [new(60, 1, 2, 0.5m, 1.5m)]
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new CachedBtcMarketDataService(repository, cache);

        var first = await service.GetHistoryAsync("BTC", "1h", null, null, null, 24, CancellationToken.None);
        var second = await service.GetHistoryAsync("BTC", "1h", null, null, null, 24, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, repository.GetCandlesCallCount);
    }

    [Fact]
    public async Task GetHistoryAsync_DoesNotCacheDifferentCursors()
    {
        var repository = new InMemoryCandleRepository();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new CachedBtcMarketDataService(repository, cache);

        await service.GetHistoryAsync("BTC", "1h", null, null, 100, 24, CancellationToken.None);
        await service.GetHistoryAsync("BTC", "1h", null, null, 200, 24, CancellationToken.None);

        Assert.Equal(2, repository.GetCandlesCallCount);
    }

    [Fact]
    public async Task GetHistoryAsync_RejectsUnknownInstrument()
    {
        var repository = new InMemoryCandleRepository();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new CachedBtcMarketDataService(repository, cache);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetHistoryAsync("DOGE", "1h", null, null, null, null, CancellationToken.None));
        Assert.Equal(0, repository.GetCandlesCallCount);
    }
}