namespace BtcApi.Domain.MarketData;

/// <summary>チャート・取引で扱う銘柄と各データ提供元のシンボル対応です。</summary>
/// <param name="Code">アプリケーション内の正規銘柄コード。</param>
/// <param name="BaseAsset">基軸通貨コード。</param>
/// <param name="QuoteAsset">決済通貨コード。</param>
/// <param name="KrakenPair">Kraken REST APIのペア名。</param>
/// <param name="KrakenWebSocketSymbol">Kraken WebSocket APIのシンボル名。</param>
/// <param name="ArchiveSymbol">履歴アーカイブのペア名。</param>
public sealed record MarketInstrument(
    string Code,
    string BaseAsset,
    string QuoteAsset,
    string KrakenPair,
    string KrakenWebSocketSymbol,
    string ArchiveSymbol)
{
    private static readonly MarketInstrument[] Instruments =
    [
        new("BTC", "BTC", "USD", "XBTUSD", "BTC/USD", "BTCUSDT")
    ];

    private static readonly IReadOnlyDictionary<string, MarketInstrument> Lookup = BuildLookup();

    /// <summary>現在設定されている銘柄を一覧で取得します。</summary>
    public static IReadOnlyList<MarketInstrument> Supported => Instruments;

    /// <summary>Kraken WebSocketシンボルから銘柄を検索します。</summary>
    /// <param name="symbol">Kraken WebSocketの銘柄名。</param>
    /// <returns>対応する銘柄定義。未登録ならnull。</returns>
    public static MarketInstrument? FindByKrakenWebSocketSymbol(string symbol) =>
        Instruments.FirstOrDefault(instrument => string.Equals(
            instrument.KrakenWebSocketSymbol,
            symbol,
            StringComparison.OrdinalIgnoreCase));

    /// <summary>銘柄コードまたは別名を正規銘柄定義へ変換します。</summary>
    /// <param name="code">BTC、BTCUSD、BTC/USDなどの銘柄表記。</param>
    /// <returns>対応する銘柄定義。</returns>
    /// <exception cref="KeyNotFoundException">未登録の銘柄コードが指定された場合。</exception>
    public static MarketInstrument FromCode(string? code)
    {
        var normalized = string.IsNullOrWhiteSpace(code) ? "BTC" : code.Trim();
        return Lookup.TryGetValue(normalized, out var instrument)
            ? instrument
            : throw new KeyNotFoundException($"未対応の銘柄です: {normalized}");
    }

    /// <summary>公開銘柄一覧から正規コードと別名の索引を作ります。</summary>
    /// <returns>大文字小文字を区別しない銘柄索引。</returns>
    private static IReadOnlyDictionary<string, MarketInstrument> BuildLookup()
    {
        var lookup = new Dictionary<string, MarketInstrument>(StringComparer.OrdinalIgnoreCase);
        foreach (var instrument in Instruments)
        {
            lookup[instrument.Code] = instrument;
            lookup[$"{instrument.BaseAsset}{instrument.QuoteAsset}"] = instrument;
            lookup[$"{instrument.BaseAsset}/{instrument.QuoteAsset}"] = instrument;
            lookup[instrument.BaseAsset] = instrument;
        }
        return lookup;
    }
}
