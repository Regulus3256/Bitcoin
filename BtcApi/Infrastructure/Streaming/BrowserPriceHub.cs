using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using BtcApi.Application.Abstractions;
using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;

namespace BtcApi.Infrastructure.Streaming;

/// <summary>最新価格を保持し、ブラウザーのWebSocketクライアントへ配信します。</summary>
public sealed class BrowserPriceHub : ILivePriceHub
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, BrowserClient> _clients = new();
    private readonly ILogger<BrowserPriceHub> _logger;
    private readonly ConcurrentDictionary<string, PriceSnapshot> _currentPrices = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ブラウザー向け価格Hubを生成します。</summary>
    /// <param name="logger">切断したブラウザークライアントを記録するロガー。</param>
    public BrowserPriceHub(ILogger<BrowserPriceHub> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public PriceSnapshot? GetCurrentPrice(string? symbol)
    {
        var instrument = MarketInstrument.FromCode(symbol);
        return _currentPrices.TryGetValue(instrument.Code, out var snapshot) ? snapshot : null;
    }

    /// <inheritdoc />
    public async Task PublishAsync(PriceSnapshot snapshot, CancellationToken cancellationToken)
    {
        _currentPrices[snapshot.Symbol] = snapshot;
        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        foreach (var client in _clients.Values)
        {
            try
            {
                await SendAsync(client, payload, cancellationToken);
            }
            catch (WebSocketException ex)
            {
                _logger.LogDebug(ex, "Removing disconnected browser price client");
                _clients.TryRemove(client.Id, out _);
            }
        }
    }

    /// <inheritdoc />
    public async Task AcceptBrowserConnectionAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var client = new BrowserClient(Guid.NewGuid(), socket);
        _clients[client.Id] = client;
        try
        {
            foreach (var currentPrice in _currentPrices.Values)
            {
                await SendAsync(client, JsonSerializer.SerializeToUtf8Bytes(currentPrice, JsonOptions), context.RequestAborted);
            }

            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (WebSocketException ex)
        {
            _logger.LogDebug(ex, "Browser WebSocket disconnected");
        }
        finally
        {
            _clients.TryRemove(client.Id, out _);
            if (socket.State == WebSocketState.Open)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
                }
                catch (WebSocketException)
                {
                }
            }
        }
    }

    /// <summary>ブラウザーのWebSocketで送信が重ならないようにして、メッセージを1件送ります。</summary>
    /// <param name="client">送信先のブラウザークライアント。</param>
    /// <param name="payload">UTF-8のJSONデータ。</param>
    /// <param name="cancellationToken">送信をキャンセルするトークン。</param>
    /// <returns>送信終了またはソケット切断時に完了するタスク。</returns>
    private static async Task SendAsync(BrowserClient client, byte[] payload, CancellationToken cancellationToken)
    {
        await client.SendLock.WaitAsync(cancellationToken);
        try
        {
            if (client.Socket.State == WebSocketState.Open)
            {
                await client.Socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        finally
        {
            client.SendLock.Release();
        }
    }

    private sealed record BrowserClient(Guid Id, WebSocket Socket)
    {
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }
}