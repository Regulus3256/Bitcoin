using BtcApi.Domain.MarketData;
using Xunit;

namespace BtcApi.Tests.Domain;

public sealed class MarketInstrumentTests
{
    [Theory]
    [InlineData("BTC", "BTC", "USD", "XBTUSD", "BTC/USD", "BTCUSDT")]
    [InlineData("btcusd", "BTC", "USD", "XBTUSD", "BTC/USD", "BTCUSDT")]
    [InlineData(" BTC/USD ", "BTC", "USD", "XBTUSD", "BTC/USD", "BTCUSDT")]
    public void FromCode_ResolvesInstrumentAndProviderMappings(
        string code,
        string baseAsset,
        string quoteAsset,
        string krakenPair,
        string krakenWebSocketSymbol,
        string archiveSymbol)
    {
        var instrument = MarketInstrument.FromCode(code);

        Assert.Equal("BTC", instrument.Code);
        Assert.Equal(baseAsset, instrument.BaseAsset);
        Assert.Equal(quoteAsset, instrument.QuoteAsset);
        Assert.Equal(krakenPair, instrument.KrakenPair);
        Assert.Equal(krakenWebSocketSymbol, instrument.KrakenWebSocketSymbol);
        Assert.Equal(archiveSymbol, instrument.ArchiveSymbol);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromCode_UsesDefaultInstrumentWhenCodeIsMissing(string? code)
    {
        Assert.Equal("BTC", MarketInstrument.FromCode(code).Code);
    }

    [Fact]
    public void FromCode_RejectsUnknownInstrument()
    {
        Assert.Throws<KeyNotFoundException>(() => MarketInstrument.FromCode("DOGE"));
    }

    [Fact]
    public void SupportedInstruments_HaveUniqueCodesAndArchiveSymbols()
    {
        var instruments = MarketInstrument.Supported;

        Assert.NotEmpty(instruments);
        Assert.Equal(instruments.Count, instruments.Select(item => item.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(instruments.Count, instruments.Select(item => item.ArchiveSymbol).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}