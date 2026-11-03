using System.Globalization;
using System.Text.Json;
using BtcApi.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BtcApi.Services;

/// <summary>
/// Retrieves and briefly caches OHLC candles from Kraken's public REST API.
/// </summary>
public sealed class KrakenMarketDataService : IBtcMarketDataService
{
    private const string KrakenOhlcUrl = "https://api.kraken.com/0/public/OHLC";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<KrakenMarketDataService> _logger;

    /// <summary>
    /// Creates the Kraken market-data service.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create outbound HTTP clients.</param>
    /// <param name="cache">In-memory cache used to reduce repeated exchange requests.</param>
    /// <param name="logger">Logger for upstream API failures.</param>
    public KrakenMarketDataService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<KrakenMarketDataService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CandleData[]> GetHistoryAsync(
        string range,
        long? before,
        int? limit,
        CancellationToken cancellationToken)
    {
        var normalizedRange = NormalizeRange(range);
        var interval = GetKrakenInterval(normalizedRange);
        var pageLimit = GetPageLimit(normalizedRange, limit);
        var cacheKey = $"btc-history:{normalizedRange}:{before?.ToString(CultureInfo.InvariantCulture) ?? "latest"}:{pageLimit}";

        if (_cache.TryGetValue(cacheKey, out CandleData[]? cachedCandles) && cachedCandles is not null)
        {
            return cachedCandles;
        }

        var candles = await FetchCandlesAsync(normalizedRange, interval, before, pageLimit, cancellationToken);
        _cache.Set(cacheKey, candles, GetCacheDuration(normalizedRange));
        return candles;
    }

    /// <summary>
    /// Normalizes and validates a requested timeframe, defaulting unknown values to 1h.
    /// </summary>
    /// <param name="range">The timeframe supplied by the request.</param>
    /// <returns>A supported lowercase timeframe key.</returns>
    private static string NormalizeRange(string range)
    {
        var normalized = (range ?? "1h").Trim().ToLowerInvariant();
        return normalized is "1m" or "1h" or "4h" or "1d" or "1w" ? normalized : "1h";
    }

    /// <summary>
    /// Converts the application's timeframe key into Kraken's interval value in minutes.
    /// </summary>
    /// <param name="range">A normalized timeframe key.</param>
    /// <returns>The interval, in minutes, required by Kraken's OHLC endpoint.</returns>
    private static int GetKrakenInterval(string range) => range switch
    {
        "1m" => 1,
        "1h" => 60,
        "4h" => 240,
        "1d" => 1440,
        "1w" => 10080,
        _ => 60
    };

    /// <summary>
    /// Selects the candle-page size and clamps caller-supplied values to a safe range.
    /// </summary>
    /// <param name="range">A normalized timeframe key.</param>
    /// <param name="limit">The optional requested page size.</param>
    /// <returns>The number of candles to request and return.</returns>
    private static int GetPageLimit(string range, int? limit)
    {
        if (limit.HasValue)
        {
            return Math.Clamp(limit.Value, 20, 500);
        }

        return range switch
        {
            "1m" => 60,
            "1h" => 24,
            "4h" => 24,
            "1d" => 30,
            "1w" => 52,
            _ => 24
        };
    }

    /// <summary>
    /// Requests candles from Kraken and parses its OHLC response payload.
    /// </summary>
    /// <param name="range">A normalized timeframe key, used for diagnostic logging.</param>
    /// <param name="interval">The Kraken interval in minutes.</param>
    /// <param name="before">Optional exclusive Unix-seconds cursor for historical paging.</param>
    /// <param name="pageLimit">Maximum number of candles to return.</param>
    /// <param name="cancellationToken">Token used to cancel the HTTP request.</param>
    /// <returns>Chronologically ordered candles from the requested page.</returns>
    /// <exception cref="HttpRequestException">Thrown if Kraken responds with an error or invalid data.</exception>
    private async Task<CandleData[]> FetchCandlesAsync(
        string range,
        int interval,
        long? before,
        int pageLimit,
        CancellationToken cancellationToken)
    {
        var requestUrl = $"{KrakenOhlcUrl}?pair=XBTUSD&interval={interval}";
        if (before.HasValue)
        {
            var since = Math.Max(0, before.Value - (long)pageLimit * interval * 60);
            requestUrl += $"&since={since}";
        }

        try
        {
            using var response = await _httpClientFactory.CreateClient().GetAsync(requestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            var root = document.RootElement;
            if (root.TryGetProperty("error", out var errors) && errors.GetArrayLength() > 0)
            {
                throw new HttpRequestException(string.Join(", ", errors.EnumerateArray().Select(error => error.GetString())));
            }

            var pairData = root.GetProperty("result")
                .EnumerateObject()
                .First(property => property.Name != "last")
                .Value;

            return pairData.EnumerateArray()
                .Select(ParseCandle)
                .Where(candle => !before.HasValue || candle.Time < before.Value)
                .OrderBy(candle => candle.Time)
                .TakeLast(pageLimit)
                .ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or FormatException)
        {
            _logger.LogError(ex, "Kraken OHLC request failed for range {Range}", range);
            throw new HttpRequestException("Kraken historical price data is unavailable.", ex);
        }
    }

    /// <summary>
    /// Parses one Kraken OHLC array element into the application's candle model.
    /// </summary>
    /// <param name="entry">A Kraken OHLC JSON array entry.</param>
    /// <returns>The corresponding candle with Unix-seconds time and decimal OHLC values.</returns>
    private static CandleData ParseCandle(JsonElement entry) => new(
        entry[0].GetInt64(),
        decimal.Parse(entry[1].GetString()!, CultureInfo.InvariantCulture),
        decimal.Parse(entry[2].GetString()!, CultureInfo.InvariantCulture),
        decimal.Parse(entry[3].GetString()!, CultureInfo.InvariantCulture),
        decimal.Parse(entry[4].GetString()!, CultureInfo.InvariantCulture));

    /// <summary>
    /// Returns the cache lifetime appropriate for a candle timeframe.
    /// </summary>
    /// <param name="range">A normalized timeframe key.</param>
    /// <returns>The in-memory cache duration.</returns>
    private static TimeSpan GetCacheDuration(string range) => range switch
    {
        "1m" => TimeSpan.FromSeconds(10),
        "1h" => TimeSpan.FromSeconds(30),
        "4h" => TimeSpan.FromMinutes(2),
        "1d" => TimeSpan.FromMinutes(5),
        "1w" => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromSeconds(30)
    };
}
