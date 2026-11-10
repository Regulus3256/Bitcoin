using BtcApi.Application.Abstractions;

namespace BtcApi.Endpoints;

/// <summary>BTC価格、過去ローソク足、ライブ配信のHTTPルートを登録します。</summary>
public static class BtcEndpoints
{
    /// <summary>BTC関連のルートをアプリケーションへ登録します。</summary>
    /// <param name="endpoints">アプリケーションが使用するルートビルダー。</param>
    /// <returns>BTC関連ルートを追加した同じルートビルダー。</returns>
    public static IEndpointRouteBuilder MapBtcEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/btc", GetCurrentPrice);
        endpoints.MapGet("/api/btc/history", GetHistory);
        endpoints.Map("/ws/price", HandlePriceStream);
        return endpoints;
    }

    /// <summary>最後に受信したBTC/USD価格を返します。</summary>
    /// <param name="priceHub">最新価格を保持するアプリケーションHub。</param>
    /// <returns>価格があればHTTP 200、初回受信前ならHTTP 503。</returns>
    private static IResult GetCurrentPrice(ILivePriceHub priceHub)
    {
        var price = priceHub.CurrentPrice;
        return price is not null
            ? Results.Ok(price)
            : Results.Problem("Waiting for the live BTC price feed.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>指定された時間足・ページのBTC/USD過去ローソク足を返します。</summary>
    /// <param name="marketData">過去マーケットデータを取得するアプリケーションサービス。</param>
    /// <param name="range">時間足（1m、1h、4h、1d、1w）。既定値は1h。</param>
    /// <param name="before">これより古い足を取得するためのUnix秒カーソル（省略可）。</param>
    /// <param name="limit">取得本数（省略可）。対応範囲に補正されます。</param>
    /// <param name="cancellationToken">HTTPリクエスト終了時に取得を中断するトークン。</param>
    /// <returns>成功時はHTTP 200と時系列順の配列、データ取得不可時はHTTP 502。</returns>
    private static async Task<IResult> GetHistory(
        IBtcMarketDataService marketData,
        string range = "1h",
        long? before = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var candles = await marketData.GetHistoryAsync(range, before, limit, cancellationToken);
            return Results.Ok(candles);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                "Historical BTC data is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>ブラウザーのWebSocket接続を受け付け、最新価格を配信します。</summary>
    /// <param name="context">現在のHTTPリクエストとWebSocket接続のコンテキスト。</param>
    /// <param name="priceHub">価格配信を担当するアプリケーションHub。</param>
    /// <returns>ブラウザー接続が閉じたときに完了するタスク。</returns>
    private static Task HandlePriceStream(HttpContext context, ILivePriceHub priceHub)
    {
        return priceHub.AcceptBrowserConnectionAsync(context);
    }
}
