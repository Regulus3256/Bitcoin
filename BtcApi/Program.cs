using BtcApi.Endpoints;
using BtcApi.Infrastructure.Authentication;
using BtcApi.Infrastructure.DependencyInjection;

// アプリケーションの依存関係、ミドルウェア、機能エンドポイントを登録します。
var isCreateDemoUserCommand = args.Length > 0 && args[0] == "create-demo-user";
var builder = WebApplication.CreateBuilder(isCreateDemoUserCommand ? Array.Empty<string>() : args);
if (isCreateDemoUserCommand)
{
    await DemoUserCommand.RunAsync(builder.Configuration, args.Skip(1).ToArray());
    return;
}

builder.Services.AddBtcApiServices(builder.Configuration);
builder.Services.AddBtcCors(builder.Configuration);

var app = builder.Build();
app.UseCors();
app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapBtcEndpoints();
app.MapDemoEndpoints();
app.Run();
