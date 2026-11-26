using System.Net;
using BtcApi.Infrastructure.Kraken;
using Xunit;

namespace BtcApi.Tests.Infrastructure.Kraken;

public sealed class KrakenOhlcParserTests
{
    private const string Response = """
        {"error":[],"result":{"XXBTZUSD":[
          [1000,"10","12","9","11","5","3.4"],
          [2000,"11","13","10","12","6","4.5"],
          [3000,"12","14","11","13","7","5.6"]
        ],"last":3000}}
        """;

    [Fact]
    public void Parse_ReturnsChronologicalCandlesAndVolume()
    {
        var candles = KrakenOhlcParser.Parse(Response, null, 10);

        Assert.Equal(new long[] { 1000, 2000, 3000 }, candles.Select(candle => candle.Time));
        Assert.Equal(3.4m, candles[0].Volume);
        Assert.Equal(13m, candles[^1].Close);
    }

    [Fact]
    public void Parse_ExcludesCursorAndLimitsMostRecentCandles()
    {
        var candles = KrakenOhlcParser.Parse(Response, 3000, 1);

        Assert.Single(candles);
        Assert.Equal(2000, candles[0].Time);
    }

    [Fact]
    public void Parse_ThrowsWhenKrakenReturnsErrors()
    {
        const string response = "{\"error\":[\"EGeneral:Invalid arguments\"],\"result\":{}}";

        var exception = Assert.Throws<HttpRequestException>(() => KrakenOhlcParser.Parse(response, null, 10));

        Assert.Contains("Invalid arguments", exception.Message);
    }
}