using BtcApi.Domain.MarketData;
using Xunit;

namespace BtcApi.Tests.Domain;

public sealed class CandleTimeframeTests
{
    [Theory]
    [InlineData("1m", 1, 60)]
    [InlineData("1h", 60, 24)]
    [InlineData("4h", 240, 24)]
    [InlineData("1d", 1440, 30)]
    [InlineData("1w", 10080, 52)]
    public void FromKey_ReturnsConfiguredTimeframe(string key, int intervalMinutes, int pageSize)
    {
        var timeframe = CandleTimeframe.FromKey(key);

        Assert.Equal(key, timeframe.Key);
        Assert.Equal(intervalMinutes, timeframe.KrakenIntervalMinutes);
        Assert.Equal(pageSize, timeframe.DefaultPageSize);
        Assert.True(timeframe.CacheDuration > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void FromKey_UsesOneHourForMissingKey(string? key)
    {
        Assert.Equal("1h", CandleTimeframe.FromKey(key).Key);
    }

    [Fact]
    public void FromKey_RejectsUnknownKey()
    {
        Assert.Throws<ArgumentException>(() => CandleTimeframe.FromKey("not-a-range"));
    }

    [Fact]
    public void FromKey_IgnoresWhitespaceAndCase()
    {
        Assert.Equal("4h", CandleTimeframe.FromKey(" 4H ").Key);
    }
}