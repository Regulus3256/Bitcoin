using System.Globalization;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

// HTMLやJSなどの静的ファイルを配信する設定
app.UseDefaultFiles();
app.UseStaticFiles();

// ビットコインの現在価格（USD）を返すAPI
app.MapGet("/api/btc", async (IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient();
    client.DefaultRequestHeaders.Add("User-Agent", "BtcApp");

    try
    {
        var response = await client.GetFromJsonAsync<CoinGeckoResponse>(
            "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd"
        );

        if (response?.Bitcoin?.Usd > 0)
        {
            return Results.Ok(new
            {
                symbol = "BTC",
                priceUsd = response.Bitcoin.Usd,
                updatedAt = DateTime.UtcNow
            });
        }
    }
    catch
    {
        // CoinGecko rate limit or outage -> fallback to Binance
    }

    try
    {
        var binanceResponse = await client.GetFromJsonAsync<BinanceTickerPrice>(
            "https://api.binance.com/api/v3/ticker/price?symbol=BTCUSDT"
        );

        return Results.Ok(new
        {
            symbol = "BTC",
            priceUsd = binanceResponse?.Price ?? 0m,
            updatedAt = DateTime.UtcNow
        });
    }
    catch
    {
        return Results.Ok(new
        {
            symbol = "BTC",
            priceUsd = 0m,
            updatedAt = DateTime.UtcNow
        });
    }
});

app.MapGet("/api/btc/history", async (IHttpClientFactory clientFactory, string range = "1h", long? before = null, int? limit = null) =>
{
    var client = clientFactory.CreateClient();
    client.DefaultRequestHeaders.Add("User-Agent", "BtcApp");

    var normalizedRange = (range ?? "1h").Trim().ToLowerInvariant();
    var interval = normalizedRange switch
    {
        "1m" => "1m",
        "1h" => "1h",
        "4h" => "4h",
        "1d" => "1d",
        "1w" => "1w",
        _ => "1h"
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
    var requestUrl = $"https://api.binance.com/api/v3/klines?symbol=BTCUSDT&interval={interval}&limit={pageLimit}";

    if (before.HasValue)
    {
        requestUrl += $"&endTime={before.Value * 1000L}";
    }

    try
    {
        var responseJson = await client.GetStringAsync(requestUrl);
        using var document = JsonDocument.Parse(responseJson);

        var candles = document.RootElement
            .EnumerateArray()
            .Select(entry =>
            {
                var values = entry.EnumerateArray().ToArray();
                if (values.Length < 6)
                {
                    return null;
                }

                var timeMs = long.Parse(values[0].GetRawText(), CultureInfo.InvariantCulture);
                var open = decimal.Parse(values[1].GetString() ?? values[1].ToString(), CultureInfo.InvariantCulture);
                var high = decimal.Parse(values[2].GetString() ?? values[2].ToString(), CultureInfo.InvariantCulture);
                var low = decimal.Parse(values[3].GetString() ?? values[3].ToString(), CultureInfo.InvariantCulture);
                var close = decimal.Parse(values[4].GetString() ?? values[4].ToString(), CultureInfo.InvariantCulture);

                return new CandleData(timeMs / 1000, open, high, low, close);
            })
            .Where(c => c is not null)
            .Select(c => c!)
            .OrderBy(c => c.Time)
            .ToArray();

        return Results.Ok(candles);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Binance history request failed for range {Range}; trying CoinGecko fallback", normalizedRange);
    }

    try
    {
        var days = normalizedRange switch
        {
            "1m" or "1h" => 1,
            "4h" => 30,
            "1d" => 90,
            "1w" => 365,
            _ => 1
        };
        var fallbackUrl = $"https://api.coingecko.com/api/v3/coins/bitcoin/market_chart?vs_currency=usd&days={days}";
        var fallbackJson = await client.GetStringAsync(fallbackUrl);
        using var fallbackDocument = JsonDocument.Parse(fallbackJson);
        var bucketSeconds = interval switch
        {
            "1m" => 60,
            "1h" => 3600,
            "4h" => 14400,
            "1d" => 86400,
            "1w" => 604800,
            _ => 3600
        };

        var fallbackCandles = fallbackDocument.RootElement.GetProperty("prices")
            .EnumerateArray()
            .Select(point => new PricePoint(
                Convert.ToInt64(point[0].GetDecimal() / 1000m),
                point[1].GetDecimal()))
            .Where(point => !before.HasValue || point.Time < before.Value)
            .GroupBy(point => point.Time / bucketSeconds * bucketSeconds)
            .Select(group =>
            {
                var points = group.OrderBy(point => point.Time).ToArray();
                return new CandleData(
                    group.Key,
                    points[0].Price,
                    points.Max(point => point.Price),
                    points.Min(point => point.Price),
                    points[^1].Price);
            })
            .OrderBy(candle => candle.Time)
            .TakeLast(pageLimit)
            .ToArray();

        return Results.Ok(fallbackCandles);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Both Binance and CoinGecko history requests failed for range {Range}", normalizedRange);
        return Results.Problem("Historical price data is temporarily unavailable.", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

record CoinGeckoResponse(BitcoinInfo Bitcoin);
record BitcoinInfo(decimal Usd);
record BinanceTickerPrice(decimal Price);
record PricePoint(long Time, decimal Price);
record CandleData(long Time, decimal Open, decimal High, decimal Low, decimal Close);