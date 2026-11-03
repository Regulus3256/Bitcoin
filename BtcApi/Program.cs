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
    catch
    {
        return Results.Ok(Array.Empty<CandleData>());
    }
});

app.Run();

record CoinGeckoResponse(BitcoinInfo Bitcoin);
record BitcoinInfo(decimal Usd);
record BinanceTickerPrice(decimal Price);
record CandleData(long Time, decimal Open, decimal High, decimal Low, decimal Close);