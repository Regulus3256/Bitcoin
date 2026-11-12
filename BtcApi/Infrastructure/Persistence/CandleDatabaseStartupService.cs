using BtcApi.Application.Abstractions;
using BtcApi.Domain.MarketData;

namespace BtcApi.Infrastructure.Persistence;

/// <summary>DBスキーマ作成、直近足補完、過去アーカイブ取込を起動時に行います。</summary>
public sealed class CandleDatabaseStartupService : IHostedService
{
    private readonly ICandleRepository _repository;
    private readonly IHistoricalCandleImporter _importer;
    private readonly IKrakenRecentCandleClient _recentCandleClient;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<CandleDatabaseStartupService> _logger;
    private Task? _importTask;

    /// <summary>DB初期化サービスを生成します。</summary>
    /// <param name="repository">DBスキーマとローソク足を管理するリポジトリ。</param>
    /// <param name="importer">無料アーカイブから過去足を取り込むサービス。</param>
    /// <param name="recentCandleClient">銘柄ごとの直近足取得クライアント。</param>
    /// <param name="lifetime">ホストの起動・終了通知。</param>
    /// <param name="logger">初期化と取込状態を記録するロガー。</param>
    public CandleDatabaseStartupService(
        ICandleRepository repository,
        IHistoricalCandleImporter importer,
        IKrakenRecentCandleClient recentCandleClient,
        IHostApplicationLifetime lifetime,
        ILogger<CandleDatabaseStartupService> logger)
    {
        _repository = repository;
        _importer = importer;
        _recentCandleClient = recentCandleClient;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>PostgreSQL接続を再試行し、スキーマ作成後に取込を開始します。</summary>
    /// <param name="cancellationToken">起動処理を中断するトークン。</param>
    /// <returns>DB初期化が完了したときに完了するタスク。</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _repository.InitializeAsync(cancellationToken);
                break;
            }
            catch (Exception ex) when (attempt < 20 && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "PostgreSQL接続を再試行します。試行回数={Attempt}/20", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }

        foreach (var instrument in MarketInstrument.Supported)
        {
            try
            {
                var recentCandles = await _recentCandleClient.GetRecentMinutesAsync(instrument, 720, cancellationToken);
                await _repository.UpsertMinuteCandlesAsync(instrument, recentCandles, "kraken-catchup", cancellationToken);
                _logger.LogInformation("Kraken直近1分足を補完しました: {Instrument} count={Count}", instrument.Code, recentCandles.Length);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Kraken直近足の補完に失敗しました。月次アーカイブ取込を続行します: {Instrument}", instrument.Code);
            }
        }

        _importTask = _importer.ImportMissingMonthsAsync(_lifetime.ApplicationStopping);
    }

    /// <summary>ホスト終了時にバックグラウンド取り込みの終了を待ちます。</summary>
    /// <param name="cancellationToken">終了待ちを中断するトークン。</param>
    /// <returns>取込処理終了後に完了するタスク。</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_importTask is not null)
        {
            await Task.WhenAny(_importTask, Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }
}
