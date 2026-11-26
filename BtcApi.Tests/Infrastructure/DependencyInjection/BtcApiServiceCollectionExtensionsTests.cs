using BtcApi.Application.Abstractions;
using BtcApi.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BtcApi.Tests.Infrastructure.DependencyInjection;

public sealed class BtcApiServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBtcApiServices_UsesOneKrakenClientForBothContracts()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MarketData"] = "Host=localhost;Database=btc_test;Username=test;Password=test"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBtcApiServices(configuration);
        using var provider = services.BuildServiceProvider();

        var ohlcClient = provider.GetRequiredService<IKrakenOhlcClient>();
        var recentClient = provider.GetRequiredService<IKrakenRecentCandleClient>();

        Assert.Same(ohlcClient, recentClient);
    }

    [Fact]
    public void AddBtcApiServices_RequiresMarketDataConnectionStringWhenResolved()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBtcApiServices(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<Npgsql.NpgsqlDataSource>());
    }
}