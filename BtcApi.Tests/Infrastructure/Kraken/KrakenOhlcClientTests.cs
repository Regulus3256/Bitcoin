using BtcApi.Domain.MarketData;
using BtcApi.Infrastructure.Kraken;
using BtcApi.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BtcApi.Tests.Infrastructure.Kraken;

public sealed class KrakenOhlcClientTests
{
    [Fact]
    public async Task GetCandlesAsync_BuildsCursorRequestAndParsesResponse()
    {
        const string json = """
            {"error":[],"result":{"XXBTZUSD":[
              [2000,"11","13","10","12","6","4.5"],
              [3000,"12","14","11","13","7","5.6"]
            ],"last":3000}}
            """;
        Uri? requestUri = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(StubHttpMessageHandler.JsonResponse(json));
        });
        var client = new KrakenOhlcClient(new StubHttpClientFactory(handler), NullLogger<KrakenOhlcClient>.Instance);

        var candles = await client.GetCandlesAsync(
            MarketInstrument.FromCode("BTC"),
            CandleTimeframe.FromKey("1m"),
            3000,
            1,
            CancellationToken.None);

        Assert.NotNull(requestUri);
        Assert.Contains("pair=XBTUSD", requestUri.Query);
        Assert.Contains("interval=1", requestUri.Query);
        Assert.Contains("since=2940", requestUri.Query);
        Assert.Single(candles);
        Assert.Equal(2000, candles[0].Time);
    }

    [Fact]
    public async Task GetCandlesAsync_MapsHttpFailureToHistoricalDataError()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(StubHttpMessageHandler.JsonResponse("{}", System.Net.HttpStatusCode.BadGateway)));
        var client = new KrakenOhlcClient(new StubHttpClientFactory(handler), NullLogger<KrakenOhlcClient>.Instance);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCandlesAsync(
            MarketInstrument.FromCode("BTC"),
            CandleTimeframe.FromKey("1h"),
            null,
            10,
            CancellationToken.None));

        Assert.Contains("historical price data is unavailable", exception.Message);
    }
}