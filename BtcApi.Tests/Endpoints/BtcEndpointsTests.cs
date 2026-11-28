using BtcApi.Application.Abstractions;
using BtcApi.Application.Services;
using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;
using BtcApi.Endpoints;
using BtcApi.Tests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BtcApi.Tests.Endpoints;

public sealed class BtcEndpointsTests
{
    [Theory]
    [InlineData("/api/market/DOGE/price")]
    [InlineData("/api/market/DOGE/history")]
    [InlineData("/api/market/DOGE/data-status")]
    public async Task UnknownInstrumentRoutes_ReturnNotFound(string path)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<ILivePriceHub, UnknownSymbolPriceHub>();
        builder.Services.AddSingleton<IBtcMarketDataService, UnknownSymbolMarketDataService>();
        builder.Services.AddSingleton<ICandleReader, InMemoryCandleRepository>();
        await using var app = builder.Build();
        app.MapBtcEndpoints();
        await app.StartAsync();

        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()!;
        using var client = new HttpClient { BaseAddress = new Uri(addresses.Addresses.Single()) };
        using var response = await client.GetAsync(path);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        await app.StopAsync();
    }

    [Theory]
    [InlineData("/api/btc/history?range=bogus")]
    [InlineData("/api/market/BTC/history?range=bogus")]
    public async Task History_InvalidRangeReturnsBadRequest(string path)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ICandleReader, InMemoryCandleRepository>();
        builder.Services.AddSingleton<IBtcMarketDataService, CachedBtcMarketDataService>();
        await using var app = builder.Build();
        app.MapBtcEndpoints();
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        using var client = new HttpClient { BaseAddress = new Uri(addresses.Addresses.Single()) };
        using var response = await client.GetAsync(path);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("timeframe", await response.Content.ReadAsStringAsync());
    }

    private sealed class UnknownSymbolPriceHub : ILivePriceHub
    {
        public PriceSnapshot? GetCurrentPrice(string? symbol)
        {
            MarketInstrument.FromCode(symbol);
            return null;
        }

        public Task PublishAsync(PriceSnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AcceptBrowserConnectionAsync(HttpContext context) => Task.CompletedTask;
    }

    private sealed class UnknownSymbolMarketDataService : IBtcMarketDataService
    {
        public Task<CandleData[]> GetHistoryAsync(
            string? symbol,
            string? range,
            long? from,
            long? through,
            long? before,
            int? limit,
            CancellationToken cancellationToken)
        {
            MarketInstrument.FromCode(symbol);
            return Task.FromResult(Array.Empty<CandleData>());
        }
    }
}