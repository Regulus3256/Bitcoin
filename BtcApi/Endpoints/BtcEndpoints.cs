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
        endpoints.MapGet("/api/market/{symbol}/price", GetCurrentPrice);
        endpoints.MapGet("/api/market/instruments", GetSupportedInstruments);
        endpoints.MapGet("/api/btc/history", GetHistory);
        endpoints.MapGet("/api/market/{symbol}/history", GetHistory);
        endpoints.MapGet("/api/btc/data-status", GetDataStatus);
        endpoints.MapGet("/api/market/{symbol}/data-status", GetDataStatus);
        endpoints.Map("/ws/price", HandlePriceStream);
        return endpoints;
    }

    /// <summary>最後に受信したBTC/USD価格を返します。</summary>
    /// <param name="priceHub">最新価格を保持するアプリケーションHub。</param>
    /// <param name="symbol">銘柄コード。既定値はBTC。</param>
    /// <returns>価格があればHTTP 200、初回受信前ならHTTP 503。</returns>
    private static IResult GetCurrentPrice(ILivePriceHub priceHub, string? symbol = null)
    {
        try
        {
            var price = priceHub.GetCurrentPrice(symbol);
            return price is not null
                ? Results.Ok(price)
                : Results.Problem("Waiting for the live BTC price feed.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (KeyNotFoundException)
        {
            return UnknownInstrument();
        }
    }

    /// <summary>利用可能な銘柄定義を返します。</summary>
    /// <returns>アプリで設定済みの銘柄コードとデータ提供元対応一覧。</returns>
    private static IResult GetSupportedInstruments() => Results.Ok(BtcApi.Domain.MarketData.MarketInstrument.Supported);

    /// <summary>指定された時間足・ページのBTC/USD過去ローソク足を返します。</summary>
    /// <param name="marketData">過去マーケットデータを取得するアプリケーションサービス。</param>
    /// <param name="symbol">銘柄コード。既定値はBTC。</param>
    /// <param name="range">時間足（1m、1h、4h、1d、1w）。既定値は1h。</param>
    /// <param name="before">これより古い足を取得するためのUnix秒カーソル（省略可）。</param>
    /// <param name="limit">取得本数（省略可）。対応範囲に補正されます。</param>
    /// <param name="cancellationToken">HTTPリクエスト終了時に取得を中断するトークン。</param>
    /// <returns>成功時はHTTP 200と時系列順の配列、データ取得不可時はHTTP 502。</returns>
    private static async Task<IResult> GetHistory(
        IBtcMarketDataService marketData,
        string? symbol = null,
        string range = "1h",
        long? from = null,
        long? through = null,
        long? before = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var candles = await marketData.GetHistoryAsync(symbol, range, from, through, before, limit, cancellationToken);
            return Results.Ok(candles);
        }
        catch (KeyNotFoundException)
        {
            return UnknownInstrument();
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                "Historical BTC data is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>DBに保存されている1分足の収録期間と件数を返します。</summary>
    /// <param name="repository">ローソク足の永続化先。</param>
    /// <param name="cancellationToken">DB問い合わせを中断するトークン。</param>
    /// <returns>最古・最新の保存時刻と1分足件数。</returns>
    private static async Task<IResult> GetDataStatus(
        ICandleRepository repository,
        string? symbol = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Results.Ok(await repository.GetCoverageAsync(
                BtcApi.Domain.MarketData.MarketInstrument.FromCode(symbol), cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return UnknownInstrument();
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

    private static IResult UnknownInstrument() =>
        Results.Problem("The requested market symbol is not supported.", statusCode: StatusCodes.Status404NotFound);
}
