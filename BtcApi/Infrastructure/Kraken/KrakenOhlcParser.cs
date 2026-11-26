using System.Globalization;
using System.Text.Json;
using BtcApi.Domain.Models;

namespace BtcApi.Infrastructure.Kraken;

/// <summary>Krakenの公開OHLC JSONをドメインローソク足へ変換します。</summary>
internal static class KrakenOhlcParser
{
    /// <summary>KrakenのOHLC応答を解析し、カーソルと本数で結果を絞ります。</summary>
    /// <param name="json">Krakenから受信した完全なOHLC応答JSON。</param>
    /// <param name="before">指定時刻より古い足に限定するカーソル（省略可）。</param>
    /// <param name="limit">返却する最大ローソク足数。</param>
    /// <returns>時刻の昇順に並んだローソク足配列。</returns>
    public static CandleData[] Parse(string json, long? before, int limit)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var errors) && errors.GetArrayLength() > 0)
        {
            throw new HttpRequestException(string.Join(", ", errors.EnumerateArray().Select(error => error.GetString())));
        }

        var pairData = root.GetProperty("result").EnumerateObject()
            .First(property => property.Name != "last")
            .Value;
        return pairData.EnumerateArray()
            .Select(ParseCandle)
            .Where(candle => !before.HasValue || candle.Time < before.Value)
            .OrderBy(candle => candle.Time)
            .TakeLast(limit)
            .ToArray();
    }

    /// <summary>KrakenのOHLC配列1行を解析します。</summary>
    /// <param name="entry">時刻とOHLC値を含むKrakenの配列。</param>
    /// <returns>Unix秒の時刻を持つドメインローソク足。</returns>
    private static CandleData ParseCandle(JsonElement entry) => new(
        entry[0].GetInt64(),
        decimal.Parse(entry[1].GetString()!, CultureInfo.InvariantCulture),
        decimal.Parse(entry[2].GetString()!, CultureInfo.InvariantCulture),
        decimal.Parse(entry[3].GetString()!, CultureInfo.InvariantCulture),
        decimal.Parse(entry[4].GetString()!, CultureInfo.InvariantCulture),
        entry.GetArrayLength() > 6
            ? decimal.Parse(entry[6].GetString()!, CultureInfo.InvariantCulture)
            : 0);
}