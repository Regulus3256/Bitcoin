namespace BtcApi.Models;

/// <summary>
/// Represents one OHLC candlestick, with time expressed as Unix seconds.
/// </summary>
/// <param name="Time">Opening time of the candle in Unix seconds.</param>
/// <param name="Open">Price at the beginning of the candle.</param>
/// <param name="High">Highest traded price during the candle.</param>
/// <param name="Low">Lowest traded price during the candle.</param>
/// <param name="Close">Price at the end of the candle.</param>
public sealed record CandleData(long Time, decimal Open, decimal High, decimal Low, decimal Close);
