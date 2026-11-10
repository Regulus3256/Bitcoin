using System.Text.Json;
using BtcApi.Application.Abstractions;
using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;

namespace BtcApi.Infrastructure.Kraken;

/// <summary>Krakenの公開REST APIを使って過去足取得契約を実装します。</summary>
public sealed class KrakenOhlcClient : IKrakenOhlcClient
{
    private const string Endpoint = "https://api.kraken.com/0/public/OHLC";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KrakenOhlcClient> _logger;

    /// <summary>KrakenのOHLC接続クラスを生成します。</summary>
    /// <param name="httpClientFactory">外部HTTPクライアントの生成元。</param>
    /// <param name="logger">プロバイダー接続失敗を記録するロガー。</param>
    public KrakenOhlcClient(IHttpClientFactory httpClientFactory, ILogger<KrakenOhlcClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CandleData[]> GetCandlesAsync(CandleTimeframe timeframe, long? before, int limit, CancellationToken cancellationToken)
    {
        var requestUrl = $"{Endpoint}?pair=XBTUSD&interval={timeframe.KrakenIntervalMinutes}";
        if (before.HasValue)
        {
            var since = Math.Max(0, before.Value - (long)limit * timeframe.KrakenIntervalMinutes * 60);
            requestUrl += $"&since={since}";
        }

        try
        {
            using var response = await _httpClientFactory.CreateClient("Kraken").GetAsync(requestUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return KrakenOhlcParser.Parse(json, before, limit);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or FormatException)
        {
            _logger.LogError(ex, "Kraken OHLC request failed for {Timeframe}", timeframe.Key);
            throw new HttpRequestException("Kraken historical price data is unavailable.", ex);
        }
    }
}