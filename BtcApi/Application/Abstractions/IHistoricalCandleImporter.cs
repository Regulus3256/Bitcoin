namespace BtcApi.Application.Abstractions;

/// <summary>無料の月次アーカイブから未保存期間のローソク足を取り込みます。</summary>
public interface IHistoricalCandleImporter
{
    /// <summary>設定された開始月から直近完了月までの未取込データを保存します。</summary>
    /// <param name="cancellationToken">取込処理を中断するトークン。</param>
    /// <returns>取込処理が終了したときに完了するタスク。</returns>
    Task ImportMissingMonthsAsync(CancellationToken cancellationToken);
}