using BtcApi.Application.Abstractions;
using BtcApi.Application.Services;
using BtcApi.Endpoints;
using BtcApi.Infrastructure.Authentication;
using BtcApi.Infrastructure.HistoricalData;
using BtcApi.Infrastructure.Kraken;
using BtcApi.Infrastructure.Persistence;
using BtcApi.Infrastructure.Streaming;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace BtcApi.Infrastructure.DependencyInjection;

/// <summary>アプリケーションサービスとインフラストラクチャ接続を登録します。</summary>
public static class BtcApiServiceCollectionExtensions
{
    /// <summary>明示的に許可されたオリジンからのAPI読み取りを許可します。</summary>
    public static IServiceCollection AddBtcCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins).WithMethods("GET").AllowAnyHeader();
            }
        }));
        return services;
    }

    /// <summary>マーケットデータ、キャッシュ、価格Hub、Kraken配信をDIへ登録します。</summary>
    /// <param name="services">登録先のサービスコレクション。</param>
    /// <returns>登録を追加した同じサービスコレクション。</returns>
    public static IServiceCollection AddBtcApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<HistoricalImportOptions>(configuration.GetSection("HistoricalData"));
        services.AddSingleton<IDemoUserRepository, PostgresDemoUserRepository>();
        services.AddSingleton<IPasswordHasher<DemoUser>, PasswordHasher<DemoUser>>();
        services.AddAuthentication(DemoBasicAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DemoBasicAuthenticationHandler>(
                DemoBasicAuthenticationHandler.SchemeName,
                _ => { });
        services.AddAuthorization(options =>
            options.AddPolicy(DemoEndpoints.AuthorizationPolicyName, policy =>
            {
                policy.AddAuthenticationSchemes(DemoBasicAuthenticationHandler.SchemeName);
                policy.RequireAuthenticatedUser();
            }));
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
        services.AddSingleton<ICandleReader>(provider => provider.GetRequiredService<ICandleRepository>());
        services.AddSingleton<ILiveCandleStore>(provider => provider.GetRequiredService<ICandleRepository>());
        services.AddSingleton<ICandleMaintenance>(provider => provider.GetRequiredService<ICandleRepository>());
        services.AddSingleton<ICandleArchiveStore>(provider => provider.GetRequiredService<ICandleRepository>());
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