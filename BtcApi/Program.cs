using BtcApi.Endpoints;
using BtcApi.Infrastructure.DependencyInjection;

// アプリケーションの依存関係、ミドルウェア、機能エンドポイントを登録します。
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBtcApiServices(builder.Configuration);
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
