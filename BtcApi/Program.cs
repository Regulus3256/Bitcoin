var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

// 【追加】HTMLやJSなどの静的ファイルを配信する設定
app.UseDefaultFiles();
app.UseStaticFiles();

// ビットコインの現在価格（USD）を返すAPI
app.MapGet("/api/btc", async (IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient();
    client.DefaultRequestHeaders.Add("User-Agent", "BtcApp");

    var response = await client.GetFromJsonAsync<CoinGeckoResponse>(
        "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd"
    );

    return Results.Ok(new {
        symbol = "BTC",
        priceUsd = response?.Bitcoin?.Usd ?? 0,
        updatedAt = DateTime.UtcNow
    });
});

app.Run();

record CoinGeckoResponse(BitcoinInfo Bitcoin);
record BitcoinInfo(decimal Usd);