using System.Globalization;
using BtcApi.Application.Abstractions;
using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BtcApi.Application.Services;

/// <summary>履歴リクエストを正規化し、プロバイダー接続の前後にメモリキャッシュを適用します。</summary>
public sealed class CachedBtcMarketDataService : IBtcMarketDataService
{
    private readonly ICandleReader _repository;
    private readonly IMemoryCache _cache;
    private readonly Dictionary<string, QueryLock> _queryLocks = new();
    private readonly object _queryLocksSync = new();

    private sealed class QueryLock
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Users;
    }

    /// <summary>過去データ取得サービスを生成します。</summary>
    /// <param name="repository">ローソク足の読み取り先。</param>
    /// <param name="cache">外部APIへの重複アクセスを減らすキャッシュ。</param>
    public CachedBtcMarketDataService(ICandleReader repository, IMemoryCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<CandleData[]> GetHistoryAsync(string? symbol, string? range, long? from, long? through, long? before, int? limit, CancellationToken cancellationToken)
    {
        var instrument = MarketInstrument.FromCode(symbol);
        var timeframe = CandleTimeframe.FromKey(range);
        var pageSize = limit.HasValue ? Math.Clamp(limit.Value, 20, 10000) : timeframe.DefaultPageSize;
        var fromKey = from?.ToString(CultureInfo.InvariantCulture) ?? "start";
        var throughKey = through?.ToString(CultureInfo.InvariantCulture) ?? "open";
        var beforeKey = before?.ToString(CultureInfo.InvariantCulture) ?? "latest";
        var cacheKey = $"market-history:{instrument.Code}:{timeframe.Key}:{fromKey}:{throughKey}:{beforeKey}:{pageSize}";
        if (_cache.TryGetValue(cacheKey, out CandleData[]? cached) && cached is not null)
        {
            return cached;
        }

        QueryLock queryLock;
        lock (_queryLocksSync)
        {
            if (!_queryLocks.TryGetValue(cacheKey, out queryLock!))
            {
                queryLock = new QueryLock();
                _queryLocks.Add(cacheKey, queryLock);
            }
            queryLock.Users++;
        }

        var acquired = false;
        try
        {
            await queryLock.Semaphore.WaitAsync(cancellationToken);
            acquired = true;
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var candles = await _repository.GetCandlesAsync(instrument, timeframe, from, through, before, pageSize, cancellationToken);
            _cache.Set(cacheKey, candles, timeframe.CacheDuration);
            return candles;
        }
        finally
        {
            if (acquired)
            {
                queryLock.Semaphore.Release();
            }
            lock (_queryLocksSync)
            {
                if (--queryLock.Users == 0)
                {
                    _queryLocks.Remove(cacheKey);
                    queryLock.Semaphore.Dispose();
                }
            }
        }
    }
}