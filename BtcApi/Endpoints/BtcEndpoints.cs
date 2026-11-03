using BtcApi.Models;
using BtcApi.Services;

namespace BtcApi.Endpoints;

/// <summary>
/// Registers HTTP endpoints for BTC prices, historical candles, and the live price stream.
/// </summary>
public static class BtcEndpoints
{
    /// <summary>
    /// Maps all BTC-related routes onto the application.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder used by the application.</param>
    /// <returns>The same route builder, with BTC routes registered.</returns>
    public static IEndpointRouteBuilder MapBtcEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/btc", GetCurrentPrice);
        endpoints.MapGet("/api/btc/history", GetHistory);
        endpoints.Map("/ws/price", HandlePriceStream);
        return endpoints;
    }

    /// <summary>
    /// Returns the most recently received BTC/USD price.
    /// </summary>
    /// <param name="priceStream">The shared Kraken WebSocket price stream.</param>
    /// <returns>HTTP 200 with a price snapshot, or HTTP 503 until the first price arrives.</returns>
    private static IResult GetCurrentPrice(KrakenPriceStream priceStream)
    {
        var price = priceStream.CurrentPrice;
        return price is not null
            ? Results.Ok(price)
            : Results.Problem("Waiting for the live BTC price feed.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// Returns historical BTC/USD candles for the requested timeframe and page.
    /// </summary>
    /// <param name="marketData">The Kraken-backed historical market-data service.</param>
    /// <param name="range">Candle interval: 1m, 1h, 4h, 1d, or 1w. Defaults to 1h.</param>
    /// <param name="before">Optional exclusive Unix-seconds cursor used to request older candles.</param>
    /// <param name="limit">Optional candle count, clamped by the service to its supported range.</param>
    /// <param name="cancellationToken">Token that cancels data retrieval when the HTTP request ends.</param>
    /// <returns>HTTP 200 with an ordered candle array, or HTTP 502 if market data is unavailable.</returns>
    private static async Task<IResult> GetHistory(
        IBtcMarketDataService marketData,
        string range = "1h",
        long? before = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var candles = await marketData.GetHistoryAsync(range, before, limit, cancellationToken);
            return Results.Ok(candles);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                "Historical BTC data is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Accepts a browser WebSocket connection and streams shared price updates to it.
    /// </summary>
    /// <param name="context">The current HTTP request and WebSocket connection context.</param>
    /// <param name="priceStream">The shared Kraken WebSocket price stream.</param>
    /// <returns>A task that completes when the browser connection closes.</returns>
    private static Task HandlePriceStream(HttpContext context, KrakenPriceStream priceStream)
    {
        return priceStream.AcceptBrowserConnectionAsync(context);
    }
}
