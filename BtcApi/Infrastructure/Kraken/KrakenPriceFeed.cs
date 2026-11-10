using System.Net.WebSockets;
using System.Text;
using BtcApi.Application.Abstractions;

namespace BtcApi.Infrastructure.Kraken;

/// <summary>Krakenのティッカー接続を1本維持し、更新値をアプリケーションHubへ渡します。</summary>
public sealed class KrakenPriceFeed : BackgroundService
{
    private static readonly Uri SocketUri = new("wss://ws.kraken.com/v2");
    private const string SubscribeMessage = "{\"method\":\"subscribe\",\"params\":{\"channel\":\"ticker\",\"symbol\":[\"BTC/USD\"],\"snapshot\":true}}";
    private readonly ILivePriceHub _priceHub;
    private readonly ILogger<KrakenPriceFeed> _logger;

    /// <summary>Krakenのティッカー配信接続を生成します。</summary>
    /// <param name="priceHub">価格をアプリケーションへ通知するHub。</param>
    /// <param name="logger">再接続やプロバイダー障害を記録するロガー。</param>
    public KrakenPriceFeed(ILivePriceHub priceHub, ILogger<KrakenPriceFeed> logger)
    {
        _priceHub = priceHub;
        _logger = logger;
    }

    /// <summary>上流接続を維持し、上限付き指数バックオフで再接続します。</summary>
    /// <param name="stoppingToken">ホスト終了を通知するトークン。</param>
    /// <returns>ホスト終了時に完了するタスク。</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                await socket.ConnectAsync(SocketUri, stoppingToken);
                var subscribe = Encoding.UTF8.GetBytes(SubscribeMessage);
                await socket.SendAsync(new ArraySegment<byte>(subscribe), WebSocketMessageType.Text, true, stoppingToken);
                await ReceiveUpdatesAsync(socket, stoppingToken);
                delay = TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kraken ticker disconnected; reconnecting in {Delay}", delay);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }

    /// <summary>テキストメッセージを受信し、有効な価格更新をHubへ通知します。</summary>
    /// <param name="socket">Krakenへ接続済みのWebSocket。</param>
    /// <param name="cancellationToken">ホスト終了を通知するトークン。</param>
    /// <returns>上流ソケットが閉じたときに完了するタスク。</returns>
    private async Task ReceiveUpdatesAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }
            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var snapshot = KrakenTickerParser.Parse(message.ToArray());
            message.SetLength(0);
            if (snapshot is not null)
            {
                await _priceHub.PublishAsync(snapshot, cancellationToken);
            }
        }
    }
}