using BtcApi.Domain.Models;
using BtcApi.Infrastructure.Streaming;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BtcApi.Tests.Infrastructure.Streaming;

public sealed class BrowserPriceHubTests
{
    [Fact]
    public async Task PublishAsync_StoresLatestSnapshotByCanonicalInstrument()
    {
        var hub = new BrowserPriceHub(NullLogger<BrowserPriceHub>.Instance);
        var first = new PriceSnapshot("BTC", 100m, DateTime.UtcNow);
        var latest = new PriceSnapshot("BTC", 101m, DateTime.UtcNow.AddSeconds(1));

        Assert.Null(hub.GetCurrentPrice(null));
        await hub.PublishAsync(first, CancellationToken.None);
        await hub.PublishAsync(latest, CancellationToken.None);

        Assert.Same(latest, hub.GetCurrentPrice("btcusd"));
    }

    [Fact]
    public void GetCurrentPrice_RejectsUnsupportedSymbol()
    {
        var hub = new BrowserPriceHub(NullLogger<BrowserPriceHub>.Instance);

        Assert.Throws<KeyNotFoundException>(() => hub.GetCurrentPrice("DOGE"));
    }
}