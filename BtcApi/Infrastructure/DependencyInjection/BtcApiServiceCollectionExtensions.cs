using BtcApi.Application.Abstractions;
using BtcApi.Application.Services;
using BtcApi.Infrastructure.Kraken;
using BtcApi.Infrastructure.Streaming;

namespace BtcApi.Infrastructure.DependencyInjection;

/// <summary>アプリケーションサービスとインフラストラクチャ接続を登録します。</summary>
public static class BtcApiServiceCollectionExtensions
{
    /// <summary>マーケットデータ、キャッシュ、価格Hub、Kraken配信をDIへ登録します。</summary>
    /// <param name="services">登録先のサービスコレクション。</param>
    /// <returns>登録を追加した同じサービスコレクション。</returns>
    public static IServiceCollection AddBtcApiServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpClient("Kraken", client => client.DefaultRequestHeaders.UserAgent.ParseAdd("BtcApp/1.0"));
        services.AddSingleton<IKrakenOhlcClient, KrakenOhlcClient>();
        services.AddSingleton<IBtcMarketDataService, CachedBtcMarketDataService>();
        services.AddSingleton<ILivePriceHub, BrowserPriceHub>();
        services.AddHostedService<KrakenPriceFeed>();
        return services;
    }
}