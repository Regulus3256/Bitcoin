using BtcApi.Application.Services;
using BtcApi.Application.Abstractions;
using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;
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

    [Fact]
    public async Task GetHistoryAsync_CoalescesIdenticalQueriesButAllowsDifferentPages()
    {
        var repository = new BlockingReader();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new CachedBtcMarketDataService(repository, cache);
        var first = service.GetHistoryAsync("BTC", "1h", null, null, 100, 24, CancellationToken.None);
        await repository.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = service.GetHistoryAsync("BTC", "1h", null, null, 100, 24, CancellationToken.None);
        var otherPage = service.GetHistoryAsync("BTC", "1h", null, null, 200, 24, CancellationToken.None);

        try
        {
            await repository.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, repository.CallCount);
            Assert.False(duplicate.IsCompleted);
        }
        finally
        {
            repository.Release.TrySetResult();
        }

        await Task.WhenAll(first, duplicate, otherPage);
        Assert.Equal(2, repository.CallCount);
    }

    [Fact]
    public async Task GetHistoryAsync_CancelledWaiterDoesNotPreventLaterRequests()
    {
        var repository = new BlockingReader();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var cancellation = new CancellationTokenSource();
        var service = new CachedBtcMarketDataService(repository, cache);
        var first = service.GetHistoryAsync("BTC", "1h", null, null, 100, 24, CancellationToken.None);
        await repository.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancelled = service.GetHistoryAsync("BTC", "1h", null, null, 100, 24, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        repository.Release.SetResult();
        await first;
        await service.GetHistoryAsync("BTC", "1h", null, null, 100, 24, CancellationToken.None);
        Assert.Equal(1, repository.CallCount);
    }

    private sealed class BlockingReader : ICandleReader
    {
        private int _calls;
        public int CallCount => _calls;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<CandleData[]> GetCandlesAsync(MarketInstrument instrument, CandleTimeframe timeframe,
            long? from, long? through, long? before, int limit, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1) FirstStarted.TrySetResult();
            if (call == 2) SecondStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return [];
        }

        public Task<CandleCoverage> GetCoverageAsync(MarketInstrument instrument, CancellationToken cancellationToken) =>
            Task.FromResult(new CandleCoverage(0, null, null));
    }
}