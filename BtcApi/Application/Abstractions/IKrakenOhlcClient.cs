using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;

namespace BtcApi.Application.Abstractions;

/// <summary>過去OHLCデータ取得に使用する外部プロバイダー接続の契約です。</summary>
public interface IKrakenOhlcClient
{
    /// <summary>Krakenからローソク足を取得し、ドメインモデルへ変換します。</summary>
    /// <param name="timeframe">正規化済みの時間足定義。</param>
    /// <param name="before">指定時刻より古い足を取得するカーソル（省略可）。</param>
    /// <param name="limit">返却する最大ローソク足数。</param>
    /// <param name="cancellationToken">HTTPリクエストをキャンセルするトークン。</param>
    /// <returns>時刻の昇順に並んだローソク足配列。</returns>
    Task<CandleData[]> GetCandlesAsync(MarketInstrument instrument, CandleTimeframe timeframe, long? before, int limit, CancellationToken cancellationToken);
}