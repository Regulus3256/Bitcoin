using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BtcApi.Models;

namespace BtcApi.Services;

/// <summary>
/// Maintains one upstream Kraken ticker connection and relays price updates to connected browsers.
/// </summary>
public sealed class KrakenPriceStream : BackgroundService
{
    private const string KrakenSocketUrl = "wss://ws.kraken.com/v2";
    private static readonly JsonSerializerOptions BrowserJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, BrowserClient> _browserClients = new();
    private readonly ILogger<KrakenPriceStream> _logger;
    private PriceSnapshot? _currentPrice;

    /// <summary>
    /// Creates the shared price-stream service.
    /// </summary>
    /// <param name="logger">Logger used to report upstream and browser connection events.</param>
    public KrakenPriceStream(ILogger<KrakenPriceStream> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the most recently received BTC/USD price, or null before the first ticker update.
    /// </summary>
    /// <returns>The latest price snapshot currently held by the service.</returns>
    public PriceSnapshot? CurrentPrice => Volatile.Read(ref _currentPrice);

    /// <summary>
    /// Runs the upstream Kraken WebSocket connection and reconnects with bounded backoff.
    /// </summary>
    /// <param name="stoppingToken">Token signaled when the host is shutting down.</param>
    /// <returns>A task that completes when the hosted service is stopped.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reconnectDelay = TimeSpan.FromSeconds(2);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                await socket.ConnectAsync(new Uri(KrakenSocketUrl), stoppingToken);

                var subscribeMessage = Encoding.UTF8.GetBytes(
                    "{\"method\":\"subscribe\",\"params\":{\"channel\":\"ticker\",\"symbol\":[\"BTC/USD\"],\"snapshot\":true}}"
                );
                await socket.SendAsync(
                    new ArraySegment<byte>(subscribeMessage),
                    WebSocketMessageType.Text,
                    true,
                    stoppingToken);

                await ReceivePricesAsync(socket, stoppingToken);
                reconnectDelay = TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kraken WebSocket disconnected; reconnecting in {Delay}", reconnectDelay);
            }

            try
            {
                await Task.Delay(reconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            reconnectDelay = TimeSpan.FromSeconds(Math.Min(reconnectDelay.TotalSeconds * 2, 30));
        }
    }

    /// <summary>
    /// Receives Kraken messages, stores valid ticker updates, and broadcasts them to browsers.
    /// </summary>
    /// <param name="socket">An open WebSocket connected to Kraken.</param>
    /// <param name="cancellationToken">Token used to stop receiving during host shutdown.</param>
    /// <returns>A task that completes when the upstream socket closes or shutdown begins.</returns>
    private async Task ReceivePricesAsync(ClientWebSocket socket, CancellationToken cancellationToken)
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

            var snapshot = ParseTicker(message.ToArray());
            message.SetLength(0);
            if (snapshot is null)
            {
                continue;
            }

            Volatile.Write(ref _currentPrice, snapshot);
            await BroadcastAsync(JsonSerializer.SerializeToUtf8Bytes(snapshot, BrowserJsonOptions), cancellationToken);
        }
    }

    /// <summary>
    /// Parses a Kraken ticker message and returns a price snapshot when it contains a ticker update.
    /// </summary>
    /// <param name="message">A complete Kraken WebSocket message encoded as UTF-8 JSON.</param>
    /// <returns>A BTC price snapshot, or null for non-ticker or incomplete messages.</returns>
    private static PriceSnapshot? ParseTicker(byte[] message)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        if (!root.TryGetProperty("channel", out var channel)
            || channel.GetString() != "ticker"
            || !root.TryGetProperty("data", out var data)
            || data.GetArrayLength() == 0)
        {
            return null;
        }

        var ticker = data[0];
        if (!ticker.TryGetProperty("last", out var last))
        {
            return null;
        }

        return new PriceSnapshot("BTC", last.GetDecimal(), DateTime.UtcNow);
    }

    /// <summary>
    /// Sends one serialized price update to every connected browser client.
    /// </summary>
    /// <param name="payload">The JSON price update encoded as UTF-8.</param>
    /// <param name="cancellationToken">Token used to stop sends during host shutdown.</param>
    /// <returns>A task that completes after all active browser sends are attempted.</returns>
    private async Task BroadcastAsync(byte[] payload, CancellationToken cancellationToken)
    {
        foreach (var client in _browserClients.Values)
        {
            try
            {
                await client.SendLock.WaitAsync(cancellationToken);
                try
                {
                    if (client.Socket.State == WebSocketState.Open)
                    {
                        await client.Socket.SendAsync(
                            new ArraySegment<byte>(payload),
                            WebSocketMessageType.Text,
                            true,
                            cancellationToken);
                    }
                }
                finally
                {
                    client.SendLock.Release();
                }
            }
            catch (WebSocketException ex)
            {
                _logger.LogDebug(ex, "Removing disconnected browser WebSocket client");
                _browserClients.TryRemove(client.Id, out _);
            }
        }
    }

    /// <summary>
    /// Accepts and manages one browser WebSocket connection until it disconnects.
    /// </summary>
    /// <param name="context">The HTTP request context that initiated the WebSocket upgrade.</param>
    /// <returns>A task that completes when the browser connection is closed.</returns>
    public async Task AcceptBrowserConnectionAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var client = new BrowserClient(Guid.NewGuid(), socket);
        _browserClients[client.Id] = client;

        try
        {
            var currentPrice = CurrentPrice;
            if (currentPrice is not null)
            {
                await SendToClientAsync(client, JsonSerializer.SerializeToUtf8Bytes(currentPrice, BrowserJsonOptions), context.RequestAborted);
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
            _logger.LogDebug(ex, "Browser WebSocket client disconnected");
        }
        finally
        {
            _browserClients.TryRemove(client.Id, out _);
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

    /// <summary>
    /// Sends a single text message to a browser socket while preventing concurrent sends on that socket.
    /// </summary>
    /// <param name="client">The connected browser client and its send lock.</param>
    /// <param name="payload">The JSON message encoded as UTF-8.</param>
    /// <param name="cancellationToken">Token used to cancel the send.</param>
    /// <returns>A task that completes when the send finishes or the socket is no longer open.</returns>
    private static async Task SendToClientAsync(BrowserClient client, byte[] payload, CancellationToken cancellationToken)
    {
        await client.SendLock.WaitAsync(cancellationToken);
        try
        {
            if (client.Socket.State == WebSocketState.Open)
            {
                await client.Socket.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);
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