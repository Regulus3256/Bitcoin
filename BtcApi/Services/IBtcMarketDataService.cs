using BtcApi.Models;

namespace BtcApi.Services;

/// <summary>
/// Defines access to historical BTC market data.
/// </summary>
public interface IBtcMarketDataService
{
    /// <summary>
    /// Retrieves historical candles for a timeframe and optional older-page cursor.
    /// </summary>
    /// <param name="range">Candle interval: 1m, 1h, 4h, 1d, or 1w.</param>
    /// <param name="before">Optional exclusive Unix-seconds cursor for older candles.</param>
    /// <param name="limit">Optional maximum number of candles to return.</param>
    /// <param name="cancellationToken">Token used to cancel the upstream request.</param>
    /// <returns>Chronologically ordered OHLC candles.</returns>
    /// <exception cref="HttpRequestException">Thrown when Kraken cannot provide valid market data.</exception>
    Task<CandleData[]> GetHistoryAsync(
        string range,
        long? before,
        int? limit,
        CancellationToken cancellationToken);
}
