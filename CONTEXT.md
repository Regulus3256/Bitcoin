# Project index

各行は責務・主要な公開シンボル/エンドポイント・依存先/外部I/Oの要約。生成物とロックファイルは対象外。

## Application
- `BtcApi/Application/Abstractions/IBtcMarketDataService.cs` — 過去足取得契約; `GetHistoryAsync`; I/O: なし。
- `BtcApi/Application/Abstractions/ICandleRepository.cs` — 足データと取込状態の永続化契約; `InitializeAsync`, `GetCandlesAsync`, `GetCoverageAsync`, `Upsert*`, `Import*`; DB実装に依存。
- `BtcApi/Application/Abstractions/IHistoricalCandleImporter.cs` — 未取込の過去足取込契約; `ImportMissingMonthsAsync`; I/O: なし。
- `BtcApi/Application/Abstractions/IKrakenOhlcClient.cs` — Kraken過去OHLC取得契約; `GetCandlesAsync`; I/O: なし。
- `BtcApi/Application/Abstractions/IKrakenRecentCandleClient.cs` — Kraken直近1分足取得契約; `GetRecentMinutesAsync`; I/O: なし。
- `BtcApi/Application/Abstractions/ILiveCandleRecorder.cs` — ライブ価格の1分足記録契約; `RecordAsync`; I/O: なし。
- `BtcApi/Application/Abstractions/ILivePriceHub.cs` — 最新価格の取得・配信契約; `GetCurrentPrice`, `PublishAsync`, `AcceptBrowserConnectionAsync`; I/O: なし。
- `BtcApi/Application/Services/CachedBtcMarketDataService.cs` — 時間足履歴のキャッシュと取得; `GetHistoryAsync`; `ICandleRepository`, `IMemoryCache`。
- `BtcApi/Application/Services/LiveMinuteCandleRecorder.cs` — 価格スナップショットから1分足を更新・保存; `RecordAsync`; `ICandleRepository`。

## Domain
- `BtcApi/Domain/MarketData/MarketInstrument.cs` — 銘柄定義と検索; `MarketInstrument`, `Supported`, `FromCode`, `FindByKrakenWebSocketSymbol`; I/O: なし。
- `BtcApi/Domain/MarketData/CandleTimeframe.cs` — 時間足定義とキー検索; `CandleTimeframe`, `FromKey`; I/O: なし。
- `BtcApi/Domain/Models/CandleCoverage.cs` — 足データ収録範囲の値型; `CandleCoverage`; I/O: なし。
- `BtcApi/Domain/Models/CandleData.cs` — OHLCV足の値型; `CandleData`; I/O: なし。
- `BtcApi/Domain/Models/PriceSnapshot.cs` — 銘柄価格・更新時刻の値型; `PriceSnapshot`; I/O: なし。

## API / Infrastructure
- `BtcApi/Endpoints/BtcEndpoints.cs` — HTTP/WebSocketルート登録; `/api/btc`, `/api/btc/history`, `/api/btc/data-status`, `/api/market/{symbol}/price|history|data-status`, `/api/market/instruments`, `/ws/price`; Applicationサービス・Repository。
- `BtcApi/Endpoints/DemoEndpoints.cs` — デモ画面ルート; `/demo`を`DemoRoute`認可ポリシーで保護し`Demo/demo.html`を返す。
- `BtcApi/Infrastructure/DependencyInjection/BtcApiServiceCollectionExtensions.cs` — DI、Basic認証、HTTPクライアント登録; `AddBtcApiServices`; `ConnectionStrings:MarketData`, `HistoricalData`。
- `BtcApi/Infrastructure/Authentication/DemoBasicAuthenticationHandler.cs` — Basic資格情報検証とチャレンジ; `DemoBasic`; DBユーザーと`IPasswordHasher<DemoUser>`を利用。
- `BtcApi/Infrastructure/Authentication/DemoUser.cs` — 認証ユーザーとユーザー名正規化; `DemoUser.NormalizeUsername`; I/O: なし。
- `BtcApi/Infrastructure/Authentication/IDemoUserRepository.cs` — 認証ユーザー永続化契約; `InitializeAsync`, `GetByUsernameAsync`, `AddAsync`; I/O: なし。
- `BtcApi/Infrastructure/Authentication/DemoUserCommand.cs` — CLI専用ユーザー作成; `create-demo-user`; 対話パスワード入力、PostgreSQLへハッシュ保存。
- `BtcApi/Infrastructure/HistoricalData/BinanceVisionCandleImporter.cs` — Binance Vision ZIPの月次・日次足を取込; `ImportMissingMonthsAsync`, `ParseArchive`; `HistoricalArchive` HTTP、Repository、`https://data.binance.vision/data/spot`。
- `BtcApi/Infrastructure/HistoricalData/HistoricalImportOptions.cs` — 過去足取込設定; `HistoricalImportOptions.StartDate`; `HistoricalData`設定。
- `BtcApi/Infrastructure/Kraken/KrakenOhlcClient.cs` — Kraken OHLC取得; `GetCandlesAsync`, `GetRecentMinutesAsync`; Kraken HTTP API、`IHttpClientFactory`。
- `BtcApi/Infrastructure/Kraken/KrakenOhlcParser.cs` — Kraken OHLC応答の解析; `KrakenOhlcParser.Parse`; JSON入力、I/O: なし。
- `BtcApi/Infrastructure/Kraken/KrakenPriceFeed.cs` — Kraken ticker受信と価格配信・足記録; `KrakenPriceFeed`; `wss://ws.kraken.com/v2`, `ILivePriceHub`, `ILiveCandleRecorder`。
- `BtcApi/Infrastructure/Kraken/KrakenTickerParser.cs` — Kraken tickerメッセージ解析; `KrakenTickerParser.Parse`; JSON入力、I/O: なし。
- `BtcApi/Infrastructure/Persistence/CandleDatabaseStartupService.cs` — DB初期化後の認証スキーマ作成・1時間足以上のバックフィル・過去足取込起動; `StartAsync`, `StopAsync`; repositories、Importer、Kraken client。
- `BtcApi/Infrastructure/Persistence/PostgresDemoUserRepository.cs` — `demo_users`の作成・検索・追加; `IDemoUserRepository`; PostgreSQL。
- `BtcApi/Infrastructure/Persistence/PostgresCandleRepository.cs` — 1分足と全上位時間足ロールアップ、coverage、取込台帳をPostgreSQLで管理; `ICandleRepository`実装; `NpgsqlDataSource`。
- `BtcApi/Infrastructure/Streaming/BrowserPriceHub.cs` — 最新価格保持とブラウザー配信; `GetCurrentPrice`, `PublishAsync`, `AcceptBrowserConnectionAsync`; ブラウザーWebSocket。

## Web client
- `BtcApi/wwwroot/index.html` — 通常チャートと`/demo`への入口; Lightweight Charts CDN、`css/style.css`。
- `BtcApi/Demo/demo.html` — デモ取引とチャートの専用画面; `/demo`で提供; `main.js`, Lightweight Charts CDN、`css/style.css`。
- `BtcApi/wwwroot/css/style.css` — チャート、ツール、デモ画面のスタイル; `.chart-context-menu`; I/O: なし。
- `BtcApi/wwwroot/js/aggregator.js` — 価格・足を指定時間足へ集約; `CandleAggregator`; I/O: なし。
- `BtcApi/wwwroot/js/api.js` — APIアクセス; `BtcApiClient.fetchCurrentPrice`, `fetchHistoricalCandles`, `fetchHistoryCoverage`, `fetchInstruments`; `/api/market/*`へHTTP。
- `BtcApi/wwwroot/js/chart.js` — ローソク足チャート、SMA、フィボナッチ、右クリックメニュー; `CandleChartManager`; Lightweight Charts、DOM。
- `BtcApi/wwwroot/js/chart-context.test.js` — チャート右クリックメニューのブラウザーテスト; `runChartContextTests`; DOM。
- `BtcApi/wwwroot/js/demo-trading.js` — デモ残高・成行売買・指値約定; `availableAmount`, `executeBuy`, `executeSell`, `fillLimitOrders`; I/O: なし。
- `BtcApi/wwwroot/js/demo-trading.test.js` — デモ取引ロジックのブラウザーテスト; `runDemoTradingTests`; I/O: なし。
- `BtcApi/wwwroot/js/replay-candle-buffer.js` — デモ用1分足のページ取得・先読みを管理; `ReplayCandleBuffer`; 注入された履歴取得関数。
- `BtcApi/wwwroot/js/replay-candle-buffer.test.js` — デモ履歴バッファのブラウザーテスト; `runReplayCandleBufferTests`; I/O: なし。
- `BtcApi/wwwroot/js/main.js` — 通常チャートとデモ再生UIを制御し日足120本・週足60本から過去足を段階取得; `BtcApiClient`, `CandleAggregator`, `CandleChartManager`, `ReplayCandleBuffer`; `/demo`, `/ws/price`, `/api/market/*`, `localStorage` (`btc-demo-session-v1`, `btc-chart-symbol`)。

## Host / configuration
- `BtcApi/Program.cs` — Webアプリ起動、Basic認証とルート登録; `create-demo-user`, `MapBtcEndpoints`, `MapDemoEndpoints`; PostgreSQL設定。
- `BtcApi/BtcApi.csproj` — .NET 9 Webプロジェクト定義とデモHTMLの出力・発行設定; `Microsoft.AspNetCore.OpenApi`, `Npgsql`。
- `BtcApi/BtcApi.http` — ローカルHTTPリクエスト例; `GET /weatherforecast/`; `http://localhost:5081`。
- `BtcApi/Dockerfile` — .NET 9 APIのビルド・実行イメージ; SDK/ASP.NET runtime、`PORT`。
- `BtcApi/.dockerignore` — Dockerビルド対象から生成物とローカル開発設定を除外; `appsettings.Development.json`。
- `BtcApi/appsettings.json` — `HistoricalData:StartDate`, Logging, `AllowedHosts`の設定。
- `BtcApi/Properties/AssemblyInfo.cs` — テスト用内部可視化; `InternalsVisibleTo("BtcApi.Tests")`; I/O: なし。
- `BtcApi/Properties/launchSettings.json` — HTTP/HTTPS開発プロファイル; `http://localhost:5081`, `https://localhost:7206`; `ASPNETCORE_ENVIRONMENT`。
- `docker-compose.yml` — `api`と`db`の起動設定; `.env`の`POSTGRES_PASSWORD`, `ConnectionStrings__MarketData`, `HistoricalData__StartDate`, PostgreSQL volume `pgdata`。
- `docs/DEMO_TRADING.md` — デモ取引、Basic認証、DB接続設定、履歴データ運用を記載; Docker Compose、Render、`/demo`。
- `docs/DB_STRUCTURE.html` — PostgreSQLテーブル構成図; `btc_candles`の全上位時間足ロールアップ、`candle_import_months`, `candle_import_days`, `candle_rollup_backfills`, `demo_users`; 接続・取込設定。

## Tests
- `BtcApi.Tests/BtcApi.Tests.csproj` — xUnitテストプロジェクト; `BtcApi`を参照; `Microsoft.NET.Test.Sdk`, `xunit`。
- `BtcApi.Tests/Application/CachedBtcMarketDataServiceTests.cs` — キャッシュ履歴サービスのテスト; `CachedBtcMarketDataServiceTests`; InMemory Repository。
- `BtcApi.Tests/Application/LiveMinuteCandleRecorderTests.cs` — ライブ1分足記録のテスト; `LiveMinuteCandleRecorderTests`; InMemory Repository。
- `BtcApi.Tests/Domain/CandleTimeframeTests.cs` — 時間足キー定義のテスト; `CandleTimeframeTests`; I/O: なし。
- `BtcApi.Tests/Domain/MarketInstrumentTests.cs` — 銘柄コード・Krakenシンボル定義のテスト; `MarketInstrumentTests`; I/O: なし。
- `BtcApi.Tests/Endpoints/BtcEndpointsTests.cs` — 未対応銘柄の応答テスト; `BtcEndpointsTests`; 実行中のASP.NET Coreアプリ、テストサービス、InMemory Repository。
- `BtcApi.Tests/Infrastructure/DependencyInjection/BtcApiServiceCollectionExtensionsTests.cs` — DI登録のテスト; `BtcApiServiceCollectionExtensionsTests`; `IServiceCollection`。
- `BtcApi.Tests/Infrastructure/Authentication/DemoBasicAuthenticationHandlerTests.cs` — `/demo` Basic認証と公開ルート維持のテスト; `DemoBasicAuthenticationHandlerTests`; ASP.NET Coreアプリ、インメモリユーザー。
- `BtcApi.Tests/Infrastructure/HistoricalData/BinanceVisionCandleImporterTests.cs` — Binance Visionアーカイブ解析・取込のテスト; `BinanceVisionCandleImporterTests`; Stub HTTP、InMemory Repository。
- `BtcApi.Tests/Infrastructure/HistoricalData/BinanceVisionImporterFlowTests.cs` — アーカイブ取込フローのテスト; `BinanceVisionImporterFlowTests`; Stub HTTP、InMemory Repository。
- `BtcApi.Tests/Infrastructure/Kraken/KrakenOhlcClientTests.cs` — Kraken OHLC HTTPクライアントのテスト; `KrakenOhlcClientTests`; Stub HTTP。
- `BtcApi.Tests/Infrastructure/Kraken/KrakenOhlcParserTests.cs` — Kraken OHLC JSON解析のテスト; `KrakenOhlcParserTests`; I/O: なし。
- `BtcApi.Tests/Infrastructure/Kraken/KrakenTickerParserTests.cs` — Kraken ticker JSON解析のテスト; `KrakenTickerParserTests`; I/O: なし。
- `BtcApi.Tests/Infrastructure/Streaming/BrowserPriceHubTests.cs` — 最新価格の保持と未対応銘柄のテスト; `BrowserPriceHubTests`; `BrowserPriceHub`。
- `BtcApi.Tests/TestDoubles/InMemoryCandleRepository.cs` — `ICandleRepository`のテスト用実装; `InMemoryCandleRepository`; メモリー内データ。
- `BtcApi.Tests/TestDoubles/StubHttpClientFactory.cs` — HTTP依存のテスト用スタブ; `StubHttpClientFactory`, `StubHttpMessageHandler`; テスト定義応答。
