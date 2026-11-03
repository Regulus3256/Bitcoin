using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

// HTMLやJSなどの静的ファイルを配信する設定
app.UseDefaultFiles();
app.UseStaticFiles();

// ビットコインの現在価格（USD）を返すAPI
var tickerRefreshLock = new SemaphoreSlim(1, 1);

app.MapGet("/api/btc", async (IHttpClientFactory clientFactory, IMemoryCache cache) =>
{
    if (cache.TryGetValue("btc-price", out PriceSnapshot? cachedPrice) && cachedPrice is not null)
    {
        return Results.Ok(cachedPrice);
    }

    await tickerRefreshLock.WaitAsync();
    try
    {
        if (cache.TryGetValue("btc-price", out cachedPrice) && cachedPrice is not null)
        {
            return Results.Ok(cachedPrice);
        }

    var client = clientFactory.CreateClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BtcApp/1.0");

    try
    {
        using var response = await client.GetAsync("https://api.kraken.com/0/public/Ticker?pair=XBTUSD");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = document.RootElement.GetProperty("result");
        var ticker = result.EnumerateObject().First().Value;
        var price = decimal.Parse(ticker.GetProperty("c")[0].GetString()!, CultureInfo.InvariantCulture);

        var priceSnapshot = new PriceSnapshot("BTC", price, DateTime.UtcNow);
        cache.Set("btc-price", priceSnapshot, TimeSpan.FromSeconds(20));
        return Results.Ok(priceSnapshot);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Kraken ticker request failed");
        return Results.Problem("BTC price is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    }
    finally
    {
        tickerRefreshLock.Release();
    }
});

app.MapGet("/api/btc/history", async (IHttpClientFactory clientFactory, IMemoryCache cache, string range = "1h", long? before = null, int? limit = null) =>
{
    var normalizedRange = (range ?? "1h").Trim().ToLowerInvariant();
    var interval = normalizedRange switch
    {
        "1m" => 1,
        "1h" => 60,
        "4h" => 240,
        "1d" => 1440,
        "1w" => 10080,
        _ => 60
    };

    var defaultLimit = normalizedRange switch
    {
        "1m" => 60,
        "1h" => 24,
        "4h" => 24,
        "1d" => 30,
        "1w" => 52,
        _ => 24
    };

    var pageLimit = limit.HasValue ? Math.Clamp(limit.Value, 20, 500) : defaultLimit;
    var cacheKey = $"btc-history:{normalizedRange}:{before?.ToString() ?? "latest"}:{pageLimit}";
    if (cache.TryGetValue(cacheKey, out CandleData[]? cachedCandles) && cachedCandles is not null)
    {
        return Results.Ok(cachedCandles);
    }

    var client = clientFactory.CreateClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BtcApp/1.0");
    var requestUrl = $"https://api.kraken.com/0/public/OHLC?pair=XBTUSD&interval={interval}";

    if (before.HasValue)
    {
        var since = Math.Max(0, before.Value - (long)pageLimit * interval * 60);
        requestUrl += $"&since={since}";
    }

    try
    {
        using var response = await client.GetAsync(requestUrl);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var root = document.RootElement;
        if (root.TryGetProperty("error", out var errors) && errors.GetArrayLength() > 0)
        {
            throw new HttpRequestException(string.Join(", ", errors.EnumerateArray().Select(error => error.GetString())));
        }

        var pairData = root.GetProperty("result")
            .EnumerateObject()
            .First(property => property.Name != "last")
            .Value;

        var candles = pairData.EnumerateArray()
            .Select(entry => new CandleData(
                entry[0].GetInt64(),
                decimal.Parse(entry[1].GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(entry[2].GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(entry[3].GetString()!, CultureInfo.InvariantCulture),
                decimal.Parse(entry[4].GetString()!, CultureInfo.InvariantCulture)))
            .Where(candle => !before.HasValue || candle.Time < before.Value)
            .OrderBy(candle => candle.Time)
            .TakeLast(pageLimit)
            .ToArray();

        var cacheDuration = normalizedRange switch
        {
            "1m" => TimeSpan.FromSeconds(10),
            "1h" => TimeSpan.FromSeconds(30),
            "4h" => TimeSpan.FromMinutes(2),
            "1d" => TimeSpan.FromMinutes(5),
            "1w" => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromSeconds(30)
        };
        cache.Set(cacheKey, candles, cacheDuration);
        return Results.Ok(candles);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Kraken OHLC request failed for range {Range}", normalizedRange);
        return Results.Problem("Historical BTC data is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

record CandleData(long Time, decimal Open, decimal High, decimal Low, decimal Close);
record PriceSnapshot(string symbol, decimal priceUsd, DateTime updatedAt);