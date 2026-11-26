using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;

namespace BtcApi.Application.Abstractions;

/// <summary>過去ローソク足とインポート状態を永続化する契約です。</summary>
public interface ICandleRepository
{
    /// <summary>必要なテーブルと索引を作成します。</summary>
    /// <param name="cancellationToken">処理を中断するトークン。</param>
    /// <returns>スキーマ作成後に完了するタスク。</returns>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>時間足とカーソルに一致するローソク足を取得します。</summary>
    /// <param name="timeframe">取得対象の時間足。</param>
    /// <param name="from">開始時刻以上を取得するカーソル（省略可）。</param>
    /// <param name="before">指定時刻より古い足を取得するカーソル（省略可）。</param>
    /// <param name="limit">取得する最大本数。</param>
    /// <param name="cancellationToken">処理を中断するトークン。</param>
    /// <returns>時刻の昇順に並んだローソク足配列。</returns>
    Task<CandleData[]> GetCandlesAsync(MarketInstrument instrument, CandleTimeframe timeframe, long? from, long? through, long? before, int limit, CancellationToken cancellationToken);

    /// <summary>最新の保存済み1分足を取得します。</summary>
    /// <param name="cancellationToken">処理を中断するトークン。</param>
    /// <returns>最新ローソク足。未保存の場合はnull。</returns>
    Task<CandleData?> GetLatestMinuteCandleAsync(MarketInstrument instrument, CancellationToken cancellationToken);

    /// <summary>DBに保存されている1分足の収録範囲を取得します。</summary>
    /// <param name="cancellationToken">処理を中断するトークン。</param>
    /// <returns>保存件数と最古・最新時刻。</returns>
    Task<CandleCoverage> GetCoverageAsync(MarketInstrument instrument, CancellationToken cancellationToken);

    /// <summary>1分足を挿入または更新します。</summary>
    /// <param name="candle">保存するローソク足。</param>
    /// <param name="source">価格データの由来。</param>
    /// <param name="cancellationToken">処理を中断するトークン。</param>
    /// <returns>保存完了後に完了するタスク。</returns>
    Task UpsertMinuteCandleAsync(MarketInstrument instrument, CandleData candle, string source, CancellationToken cancellationToken);

    /// <summary>複数の1分足をまとめて挿入または更新します。</summary>
    /// <param name="candles">保存するローソク足。</param>
    /// <param name="source">価格データの由来。</param>
    /// <param name="cancellationToken">処理を中断するトークン。</param>
    /// <returns>保存完了後に完了するタスク。</returns>
    Task UpsertMinuteCandlesAsync(MarketInstrument instrument, IReadOnlyCollection<CandleData> candles, string source, CancellationToken cancellationToken);

    /// <summary>指定月の履歴アーカイブが保存済みかを確認します。</summary>
    /// <param name="month">UTC月の初日。</param>
    /// <param name="cancellationToken">処理を中断するトークン。</param>
    /// <returns>インポート済みならtrue。</returns>
    Task<bool> IsMonthImportedAsync(MarketInstrument instrument, DateOnly month, CancellationToken cancellationToken);

    /// <summary>指定日の履歴アーカイブが保存済みかを確認します。</summary>
    /// <param name="day">UTC日付。</param>
    /// <param name="cancellationToken">処理を中断するトークン。</param>
    /// <returns>取込済みの場合はtrue。</returns>
    Task<bool> IsDayImportedAsync(MarketInstrument instrument, DateOnly day, CancellationToken cancellationToken);

    /// <summary>月次アーカイブのローソク足を一括保存し、完了印を記録します。</summary>
    /// <param name="month">UTC月の初日。</param>
    /// <param name="candles">保存する1分足。</param>
    /// <param name="cancellationToken">処理を中断するトークン。</param>
    /// <returns>新規登録されたローソク足数。</returns>
    Task<int> ImportMonthAsync(MarketInstrument instrument, DateOnly month, IReadOnlyCollection<CandleData> candles, CancellationToken cancellationToken);

    /// <summary>日次アーカイブのローソク足を一括保存し、完了印を記録します。</summary>
    /// <param name="day">対象UTC日付。</param>
    /// <param name="candles">保存する1分足。</param>
    /// <param name="cancellationToken">処理を中断するトークン。</param>
    /// <returns>新規登録されたローソク足数。</returns>
    Task<int> ImportDayAsync(MarketInstrument instrument, DateOnly day, IReadOnlyCollection<CandleData> candles, CancellationToken cancellationToken);
}