using BtcApi.Application.Abstractions;
using BtcApi.Application.Services;
using BtcApi.Infrastructure.HistoricalData;
using BtcApi.Infrastructure.Kraken;
using BtcApi.Infrastructure.Persistence;
using BtcApi.Infrastructure.Streaming;
using Npgsql;

namespace BtcApi.Infrastructure.DependencyInjection;

/// <summary>アプリケーションサービスとインフラストラクチャ接続を登録します。</summary>
public static class BtcApiServiceCollectionExtensions
{
    /// <summary>マーケットデータ、キャッシュ、価格Hub、Kraken配信をDIへ登録します。</summary>
    /// <param name="services">登録先のサービスコレクション。</param>
    /// <returns>登録を追加した同じサービスコレクション。</returns>
    public static IServiceCollection AddBtcApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<HistoricalImportOptions>(configuration.GetSection("HistoricalData"));
        services.AddSingleton(_ =>
        {
            var connectionString = configuration.GetConnectionString("MarketData")
                ?? throw new InvalidOperationException("ConnectionStrings:MarketData が設定されていません。");
            return NpgsqlDataSource.Create(connectionString);
        });
        services.AddHttpClient("Kraken", client => client.DefaultRequestHeaders.UserAgent.ParseAdd("BtcApp/1.0"));
        services.AddHttpClient("HistoricalArchive", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BtcApp/1.0");
            client.Timeout = TimeSpan.FromMinutes(2);
        });
        services.AddSingleton<ICandleRepository, PostgresCandleRepository>();
        services.AddSingleton<IHistoricalCandleImporter, BinanceVisionCandleImporter>();
        services.AddSingleton<KrakenOhlcClient>();
        services.AddSingleton<IKrakenOhlcClient>(provider => provider.GetRequiredService<KrakenOhlcClient>());
        services.AddSingleton<IKrakenRecentCandleClient>(provider => provider.GetRequiredService<KrakenOhlcClient>());
        services.AddSingleton<IBtcMarketDataService, CachedBtcMarketDataService>();
        services.AddSingleton<ILiveCandleRecorder, LiveMinuteCandleRecorder>();
        services.AddSingleton<ILivePriceHub, BrowserPriceHub>();
        services.AddHostedService<CandleDatabaseStartupService>();
        services.AddHostedService<KrakenPriceFeed>();
        return services;
    }
}