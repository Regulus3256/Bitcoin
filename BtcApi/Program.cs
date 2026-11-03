using BtcApi.Endpoints;
using BtcApi.Services;
using Microsoft.Extensions.Caching.Memory;

// アプリケーションの依存関係、ミドルウェア、機能エンドポイントを登録します。
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<KrakenPriceStream>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<KrakenPriceStream>());
builder.Services.AddSingleton<IBtcMarketDataService, KrakenMarketDataService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();
app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapBtcEndpoints();
app.Run();
