using System.Text.Json;
using BtcApi.Domain.Models;

namespace BtcApi.Infrastructure.Kraken;

/// <summary>Kraken WebSocket v2のティッカー更新を解析します。</summary>
internal static class KrakenTickerParser
{
    /// <summary>WebSocketメッセージ1件から価格更新を読み取ります。</summary>
    /// <param name="message">UTF-8で符号化されたKraken JSONメッセージ。</param>
    /// <returns>BTC価格情報。ステータスなどティッカー以外のメッセージならnull。</returns>
    public static PriceSnapshot? Parse(ReadOnlyMemory<byte> message)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        if (!root.TryGetProperty("channel", out var channel)
            || channel.GetString() != "ticker"
            || !root.TryGetProperty("data", out var data)
            || data.GetArrayLength() == 0
            || !data[0].TryGetProperty("last", out var last))
        {
            return null;
        }

        return new PriceSnapshot("BTC", last.GetDecimal(), DateTime.UtcNow);
    }
}