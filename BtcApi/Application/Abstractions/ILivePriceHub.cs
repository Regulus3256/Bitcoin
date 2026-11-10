using BtcApi.Domain.Models;

namespace BtcApi.Application.Abstractions;

/// <summary>最新価格を保持し、特定の取引所に依存せずブラウザーへ配信します。</summary>
public interface ILivePriceHub
{
    /// <summary>最新価格を取得します。最初の価格を受信するまではnullです。</summary>
    PriceSnapshot? CurrentPrice { get; }

    /// <summary>価格情報を接続中のブラウザークライアントへ配信します。</summary>
    /// <param name="snapshot">配信する価格情報。</param>
    /// <param name="cancellationToken">送信処理をキャンセルするトークン。</param>
    /// <returns>接続中クライアントへの送信試行が完了したときに完了するタスク。</returns>
    Task PublishAsync(PriceSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>ブラウザーからのWebSocket接続を受け付け、切断まで管理します。</summary>
    /// <param name="context">WebSocketリクエストのHTTPコンテキスト。</param>
    /// <returns>ブラウザーが切断したときに完了するタスク。</returns>
    Task AcceptBrowserConnectionAsync(HttpContext context);
}