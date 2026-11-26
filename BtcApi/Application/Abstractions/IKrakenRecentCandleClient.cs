using BtcApi.Domain.Models;
using BtcApi.Domain.MarketData;

namespace BtcApi.Application.Abstractions;

/// <summary>Krakenから直近の1分足を取得する契約です。</summary>
public interface IKrakenRecentCandleClient
{
    /// <summary>指定本数までの直近1分足を取得します。</summary>
    /// <param name="limit">取得する最大本数。</param>
    /// <param name="cancellationToken">取得処理を中断するトークン。</param>
    /// <returns>時刻の昇順に並んだ1分足。</returns>
    Task<CandleData[]> GetRecentMinutesAsync(MarketInstrument instrument, int limit, CancellationToken cancellationToken);
}