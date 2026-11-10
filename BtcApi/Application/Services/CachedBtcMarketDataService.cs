using System.Collections.Concurrent;
using System.Globalization;
using BtcApi.Application.Abstractions;
using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BtcApi.Application.Services;

/// <summary>履歴リクエストを正規化し、プロバイダー接続の前後にメモリキャッシュを適用します。</summary>
public sealed class CachedBtcMarketDataService : IBtcMarketDataService
{
    private readonly IKrakenOhlcClient _ohlcClient;
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _rangeLocks = new();

    /// <summary>過去データ取得サービスを生成します。</summary>
    /// <param name="ohlcClient">ローソク足取得を担当するプロバイダー接続。</param>
    /// <param name="cache">外部APIへの重複アクセスを減らすキャッシュ。</param>
    public CachedBtcMarketDataService(IKrakenOhlcClient ohlcClient, IMemoryCache cache)
    {
        _ohlcClient = ohlcClient;
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<CandleData[]> GetHistoryAsync(string? range, long? before, int? limit, CancellationToken cancellationToken)
    {
        var timeframe = CandleTimeframe.FromKey(range);
        var pageSize = limit.HasValue ? Math.Clamp(limit.Value, 20, 500) : timeframe.DefaultPageSize;
        var cursor = before?.ToString(CultureInfo.InvariantCulture) ?? "latest";
        var cacheKey = $"btc-history:{timeframe.Key}:{cursor}:{pageSize}";
        if (_cache.TryGetValue(cacheKey, out CandleData[]? cached) && cached is not null)
        {
            return cached;
        }

        var rangeLock = _rangeLocks.GetOrAdd(timeframe.Key, _ => new SemaphoreSlim(1, 1));
        await rangeLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var candles = await _ohlcClient.GetCandlesAsync(timeframe, before, pageSize, cancellationToken);
            _cache.Set(cacheKey, candles, timeframe.CacheDuration);
            return candles;
        }
        finally
        {
            rangeLock.Release();
        }
    }
}