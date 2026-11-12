# デモ取引と過去データ

## 構成

- 1分足の正本データはPostgreSQLの`btc_candles`テーブルへ保存します。
- 2023-01-01以降の過去データは、無料公開されているBinance Visionの月次・日次BTCUSDT ZIPから初回起動時に取り込みます。月・日は取込台帳へ記録し、再起動で同じアーカイブを再取得しません。
- 当月の完了日分は日次アーカイブを先に取り込み、Krakenの直近720分とライブWebSocket価格で最新部分を補います。
- 1時間足以上はDB内の1分足から集約します。
- ブラウザーのデモ口座、取引履歴、再生位置は`localStorage`に保存します。ローソク足本体は保存せず、再開時にDBから再取得します。

過去データはBTCUSDTをUSDの近似として使用します。Krakenの現在価格はBTC/USDのため、USDT/USDの差や取引所間の価格差はデモ結果に残ります。デモ売買は実際の注文を送らず、表示済み1分足の終値で約定します。手数料は現在0.1%です。

## Dockerで起動

ワークスペースのルートで実行します。

```powershell
docker compose up --build -d
docker compose logs -f api
```

DBスキーマ作成後、履歴の取り込みはAPIを起動したままバックグラウンドで進みます。進捗はAPIログに月・日単位で出力されます。DBの保存範囲と本数は次で確認できます。

```powershell
Invoke-RestMethod http://localhost:8080/api/btc/data-status
```

デモ画面は`http://localhost:8080`です。初回取り込み中は、対象期間の1分足が揃うまでセッション開始を再試行してください。

## 保存と容量

ComposeのPostgreSQLデータは名前付きボリューム`pgdata`に保存されるため、APIイメージを再ビルドしても保持されます。DBを消す`docker compose down -v`は実行しないでください。通常の停止・再起動には`docker compose down`と`docker compose up -d`を使います。

開始日を変更する場合はComposeの`HistoricalData__StartDate`を変更します。既定の2023-01-01では数年分を保存します。実測環境では約203万本で約515MBでした。期間を増やすとDB容量も増えます。

## 無料公開環境の注意

ローカルDockerのPostgreSQLは無料で使えますが、PCの稼働とディスク容量に依存します。Renderへ公開する場合は、APIコンテナとは別にPostgreSQLサービスを作成し、`ConnectionStrings__MarketData`へ接続文字列を設定してください。Renderの無料DBは保存期間などの制約が変わる場合があるため、数年分を長期間保持する用途では、期限・容量を確認し、必要なら自前サーバー上のDockerボリュームを使ってください。

Compose内のPostgreSQL認証情報はローカル開発用です。公開環境では必ず独自の強いパスワードと接続文字列を設定してください。
