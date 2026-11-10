namespace BtcApi.Domain.MarketData;

/// <summary>対応するローソク足時間足と、取得・キャッシュ時の既定値を表します。</summary>
/// <param name="Key">時間足を識別するAPIキー。</param>
/// <param name="KrakenIntervalMinutes">Kraken OHLC APIに指定する分単位の間隔。</param>
/// <param name="DefaultPageSize">既定のローソク足取得本数。</param>
/// <param name="CacheDuration">過去データのキャッシュ期間。</param>
public sealed record CandleTimeframe(string Key, int KrakenIntervalMinutes, int DefaultPageSize, TimeSpan CacheDuration)
{
    private static readonly IReadOnlyDictionary<string, CandleTimeframe> Supported =
        new Dictionary<string, CandleTimeframe>(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = new("1m", 1, 60, TimeSpan.FromSeconds(10)),
            ["1h"] = new("1h", 60, 24, TimeSpan.FromSeconds(30)),
            ["4h"] = new("4h", 240, 24, TimeSpan.FromMinutes(2)),
            ["1d"] = new("1d", 1440, 30, TimeSpan.FromMinutes(5)),
            ["1w"] = new("1w", 10080, 52, TimeSpan.FromMinutes(15))
        };

    /// <summary>APIキーを時間足定義に変換します。未対応の値は1時間足として扱います。</summary>
    /// <param name="key">呼び出し元から渡された時間足キー。</param>
    /// <returns>対応する時間足定義。</returns>
    public static CandleTimeframe FromKey(string? key)
    {
        var normalized = string.IsNullOrWhiteSpace(key) ? "1h" : key.Trim();
        return Supported.TryGetValue(normalized, out var timeframe) ? timeframe : Supported["1h"];
    }
}