using System.Text;
using BtcApi.Infrastructure.Kraken;
using Xunit;

namespace BtcApi.Tests.Infrastructure.Kraken;

public sealed class KrakenTickerParserTests
{
    [Fact]
    public void Parse_ReturnsSnapshotForSupportedSymbol()
    {
        var message = Encoding.UTF8.GetBytes("""
            {"channel":"ticker","data":[{"symbol":"BTC/USD","last":123.45}]}
            """);

        var snapshot = KrakenTickerParser.Parse(message);

        Assert.NotNull(snapshot);
        Assert.Equal("BTC", snapshot.Symbol);
        Assert.Equal(123.45m, snapshot.PriceUsd);
        Assert.Equal(DateTimeKind.Utc, snapshot.UpdatedAt.Kind);
    }

    [Theory]
    [InlineData("{\"channel\":\"heartbeat\"}")]
    [InlineData("{\"channel\":\"ticker\",\"data\":[]}")]
    [InlineData("{\"channel\":\"ticker\",\"data\":[{\"symbol\":\"DOGE/USD\",\"last\":1.0}]}")]
    public void Parse_IgnoresNonTickerOrUnsupportedMessages(string json)
    {
        var snapshot = KrakenTickerParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Null(snapshot);
    }
}