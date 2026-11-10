using BtcApi.Domain.Models;

namespace BtcApi.Application.Abstractions;

/// <summary>アプリケーション層へBTCの過去ローソク足を提供します。</summary>
public interface IBtcMarketDataService
{
    /// <summary>指定時間足のローソク足を時系列順で取得します。</summary>
    /// <param name="range">時間足を表すキー（1m、1h、4h、1d、1w）。</param>
    /// <param name="before">指定時刻より古い足を取得するためのUnix秒カーソル（省略可）。</param>
    /// <param name="limit">取得する最大本数（省略可）。</param>
    /// <param name="cancellationToken">取得処理をキャンセルするトークン。</param>
    /// <returns>時刻の昇順に並んだローソク足配列。</returns>
    Task<CandleData[]> GetHistoryAsync(string? range, long? before, int? limit, CancellationToken cancellationToken);
}