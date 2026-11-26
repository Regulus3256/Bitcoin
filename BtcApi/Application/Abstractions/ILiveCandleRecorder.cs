using BtcApi.Domain.Models;

namespace BtcApi.Application.Abstractions;

/// <summary>ライブ価格から1分足を組み立て、履歴ストアへ記録する契約です。</summary>
public interface ILiveCandleRecorder
{
    /// <summary>価格スナップショットを1分足へ反映し、必要なら保存します。</summary>
    /// <param name="snapshot">反映するライブ価格。</param>
    /// <param name="cancellationToken">保存処理を中断するトークン。</param>
    /// <returns>処理完了後に完了するタスク。</returns>
    Task RecordAsync(PriceSnapshot snapshot, CancellationToken cancellationToken);
}