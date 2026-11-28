# デモ取引と過去データ

## 構成

- 1分足の正本データはPostgreSQLの`btc_candles`テーブルへ保存します。
- 2023-01-01以降の過去データは、無料公開されているBinance Visionの月次・日次BTCUSDT ZIPから初回起動時に取り込みます。月・日は取込台帳へ記録し、再起動で同じアーカイブを再取得しません。
- 当月の完了日分は日次アーカイブを先に取り込み、Krakenの直近720分とライブWebSocket価格で最新部分を補います。
- 1時間足以上はDB内の1分足から集約します。
- ブラウザーのデモ口座、取引履歴、再生位置は`localStorage`に保存します。ローソク足本体は保存せず、再開時にDBから再取得します。
- デモ再生開始時に1分足を複数ページ先読みし、再生中はバッファが尽きる前に次ページを取得します。

過去データはBTCUSDTをUSDの近似として使用します。Krakenの現在価格はBTC/USDのため、USDT/USDの差や取引所間の価格差はデモ結果に残ります。デモ売買は実際の注文を送らず、表示済み1分足の終値で約定します。手数料は現在0.1%です。

履歴APIの`range`は`1m`、`1h`、`4h`、`1d`、`1w`に対応します。省略時は`1h`、未対応の値はHTTP 400です。

## Dockerで起動

ワークスペースのルートで実行します。

初回はルートにGit管理されない`.env`を作成し、次の形式でローカルDB用パスワードを設定してください。

```text
POSTGRES_PASSWORD=任意の強いパスワード
```

既存のDBボリュームを使う場合は、既存のPostgreSQLパスワードを設定してください。`.env`だけを変更しても既存DBのパスワードは変更されません。Dockerを使わず開発する場合は、`BtcApi/appsettings.Development.json`の`ConnectionStrings:MarketData`に接続文字列を設定します。

```powershell
docker compose up --build -d
docker compose logs -f api
```

DBスキーマ作成後、履歴の取り込みはAPIを起動したままバックグラウンドで進みます。進捗はAPIログに月・日単位で出力されます。DBの保存範囲と本数は次で確認できます。

```powershell
Invoke-RestMethod http://localhost:8080/api/btc/data-status
```

通常画面は`http://localhost:8080`で、「デモ取引」から別ページの`http://localhost:8080/demo`へ移動します。セッション終了時は通常画面に戻ります。初回取り込み中は、対象期間の1分足が揃うまでセッション開始を再試行してください。

## デモ画面のBasic認証

- デモ画面は`/demo`で提供し、HTTP Basic認証を要求します。通常チャートと`/api/*`は公開のままです。
- アカウント登録はWeb画面から行えません。`BtcApi`ディレクトリで`dotnet run -- create-demo-user <username>`を実行し、対話プロンプトでパスワードを2回入力してください。パスワードは表示されず、12文字以上が必要です。
- ユーザーはPostgreSQLの`demo_users`テーブルに保存します。パスワードはASP.NET Core `PasswordHasher`でハッシュ化し、平文は保存しません。
- RenderではWeb Service Shellから`dotnet BtcApi.dll create-demo-user <username>`を実行できます。RenderのHTTPS URLでアクセスしてください。Basic認証の資格情報はHTTPSで保護します。

## 保存と容量

ComposeのPostgreSQLデータは名前付きボリューム`pgdata`に保存されるため、APIイメージを再ビルドしても保持されます。DBを消す`docker compose down -v`は実行しないでください。通常の停止・再起動には`docker compose down`と`docker compose up -d`を使います。

開始日を変更する場合はComposeの`HistoricalData__StartDate`を変更します。既定の2023-01-01では数年分を保存します。実測環境では約203万本で約515MBでした。期間を増やすとDB容量も増えます。

アプリの初期化SQLは`btc_candles`を通常テーブルとして作成し、パーティションは作成・移行しません。既存DBを自動変換することもありません。DB側でパーティションを手動管理している場合は、親テーブル・子テーブル・主キーを別途管理し、スキーマの差異に注意してください。移行する場合は、バックアップ、書き込み停止、データと索引の検証を含む独立した移行計画が必要です。

## 外部オリジンからのアクセス

既定ではCORSによる外部オリジンの許可はありません。同一オリジンの画面からのアクセスには設定不要です。別オリジンのブラウザーから履歴などのGET APIを利用する場合は、例えば環境変数`Cors__AllowedOrigins__0=https://example.com`に許可するオリジンを設定してください。複数ある場合は`__1`、`__2`を追加します。スキーム・ホスト・ポートを正確に指定し、末尾にスラッシュを付けないでください。WebSocketのオリジン制限や認証はこのCORS設定の対象外です。

## テスト

```powershell
dotnet test .\BtcApi.Tests\BtcApi.Tests.csproj
node .\BtcApi.Tests\Browser\run-tests.mjs
```

ブラウザーテストにはNode.jsとChromium系ブラウザー（WindowsではEdgeまたはChrome）が必要です。自動検出できない場合は`BROWSER_PATH`にブラウザー実行ファイルを指定してください。失敗時は終了コードが非ゼロになります。

PostgreSQL統合テストは既定ではスキップされます。専用テストDBの接続文字列を`BTC_TEST_POSTGRES`に指定して`dotnet test .\BtcApi.Tests\BtcApi.Tests.csproj --filter "Category=Integration"`を実行してください。テストは接続先に一意のスキーマを作成して終了時に削除します。実運用DBでは実行しないでください。

## 無料公開環境の注意

ローカルDockerのPostgreSQLは無料で使えますが、PCの稼働とディスク容量に依存します。Renderへ公開する場合は、APIコンテナとは別にPostgreSQLサービスを作成し、Web Serviceの環境変数`ConnectionStrings__MarketData`に`Host=...;Port=5432;Database=...;Username=...;Password=...`形式の接続文字列を設定してください。接続情報はGitへ保存しないでください。Renderの無料DBは保存期間などの制約が変わる場合があるため、数年分を長期間保持する用途では、期限・容量を確認し、必要なら自前サーバー上のDockerボリュームを使ってください。

公開環境では必ずローカルとは別の強いパスワードと接続文字列を設定してください。
