using System.Globalization;
using System.IO.Compression;
using BtcApi.Application.Abstractions;
using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;
using Microsoft.Extensions.Options;

namespace BtcApi.Infrastructure.HistoricalData;

/// <summary>Binance Visionの無料月次CSVをPostgreSQLへ一度だけ取り込みます。</summary>
public sealed class BinanceVisionCandleImporter : IHistoricalCandleImporter
{
    private const string ArchiveBaseUrl = "https://data.binance.vision/data/spot";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICandleRepository _repository;
    private readonly HistoricalImportOptions _options;
    private readonly ILogger<BinanceVisionCandleImporter> _logger;

    /// <summary>月次アーカイブ取り込みサービスを生成します。</summary>
    /// <param name="httpClientFactory">ZIP取得用HTTPクライアントの生成元。</param>
    /// <param name="repository">ローソク足と取込済み月の保存先。</param>
    /// <param name="options">取込開始日の設定。</param>
    /// <param name="logger">進捗と失敗を記録するロガー。</param>
    public BinanceVisionCandleImporter(
        IHttpClientFactory httpClientFactory,
        ICandleRepository repository,
        IOptions<HistoricalImportOptions> options,
        ILogger<BinanceVisionCandleImporter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ImportMissingMonthsAsync(CancellationToken cancellationToken)
    {
        var firstMonth = new DateOnly(_options.StartDate.Year, _options.StartDate.Month, 1);
        var lastCompletedMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-1);
        foreach (var instrument in MarketInstrument.Supported)
        {
            var client = _httpClientFactory.CreateClient("HistoricalArchive");
            await ImportCurrentMonthDaysAsync(client, instrument, cancellationToken);
            await ImportCompletedMonthsAsync(client, instrument, firstMonth, lastCompletedMonth, cancellationToken);
        }
    }

    /// <summary>未取込の完了月を新しい月から順に保存します。</summary>
    /// <param name="client">アーカイブ取得用HTTPクライアント。</param>
    /// <param name="instrument">取り込む銘柄。</param>
    /// <param name="firstMonth">最初に取り込む月。</param>
    /// <param name="lastCompletedMonth">最新の完了月。</param>
    /// <param name="cancellationToken">取込処理を中断するトークン。</param>
    /// <returns>各月の取込処理完了後に完了するタスク。</returns>
    private async Task ImportCompletedMonthsAsync(
        HttpClient client,
        MarketInstrument instrument,
        DateOnly firstMonth,
        DateOnly lastCompletedMonth,
        CancellationToken cancellationToken)
    {
        for (var month = lastCompletedMonth; month >= firstMonth; month = month.AddMonths(-1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _repository.IsMonthImportedAsync(instrument, month, cancellationToken)) continue;

            try
            {
                var filename = $"{instrument.ArchiveSymbol}-1m-{month:yyyy-MM}.zip";
                var url = $"{ArchiveBaseUrl}/monthly/klines/{instrument.ArchiveSymbol}/1m/{filename}";
                var bytes = await client.GetByteArrayAsync(url, cancellationToken);
                var candles = ParseArchive(bytes, month, month.AddMonths(1));
                var inserted = await _repository.ImportMonthAsync(instrument, month, candles, cancellationToken);
                _logger.LogInformation("月次履歴を取り込みました: {Instrument} {Month} candles={Count} new={Inserted}", instrument.Code, month, candles.Count, inserted);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("月次履歴アーカイブが見つかりません: {Instrument} {Month}", instrument.Code, month);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "月次履歴の取り込みに失敗しました: {Instrument} {Month}", instrument.Code, month);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    /// <summary>当月の完了日分を先に取り込み、最新の連続データを早く利用可能にします。</summary>
    /// <param name="client">アーカイブ取得用HTTPクライアント。</param>
    /// <param name="cancellationToken">取込処理を中断するトークン。</param>
    /// <returns>日次アーカイブ取り込み後に完了するタスク。</returns>
    private async Task ImportCurrentMonthDaysAsync(HttpClient client, MarketInstrument instrument, CancellationToken cancellationToken)
    {
        var firstCurrentMonthDay = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var lastCompletedDay = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        for (var day = lastCompletedDay; day >= firstCurrentMonthDay && day >= _options.StartDate; day = day.AddDays(-1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _repository.IsDayImportedAsync(instrument, day, cancellationToken))
            {
                continue;
            }

            try
            {
                var filename = $"{instrument.ArchiveSymbol}-1m-{day:yyyy-MM-dd}.zip";
                var url = $"{ArchiveBaseUrl}/daily/klines/{instrument.ArchiveSymbol}/1m/{filename}";
                var bytes = await client.GetByteArrayAsync(url, cancellationToken);
                var candles = ParseArchive(bytes, day, day.AddDays(1));
                var inserted = await _repository.ImportDayAsync(instrument, day, candles, cancellationToken);
                _logger.LogInformation("日次履歴を取り込みました: {Instrument} {Day} candles={Count} new={Inserted}", instrument.Code, day, candles.Count, inserted);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("日次履歴アーカイブが見つかりません: {Instrument} {Day}", instrument.Code, day);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "日次履歴の取り込みに失敗しました: {Instrument} {Day}", instrument.Code, day);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    /// <summary>月次ZIP内のCSVから対象月の1分足を解析します。</summary>
    /// <param name="archiveBytes">ダウンロードしたZIPファイル。</param>
    /// <param name="periodStart">取り込み範囲の開始日（UTC）。</param>
    /// <param name="periodEnd">取り込み範囲の終了日（含まない）。</param>
    /// <returns>有効なローソク足一覧。</returns>
    internal static List<CandleData> ParseArchive(byte[] archiveBytes, DateOnly periodStart, DateOnly periodEnd)
    {
        using var archiveStream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(item => item.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("ZIP内にCSVファイルがありません。");
        using var reader = new StreamReader(entry.Open());

        var startTimestamp = new DateTimeOffset(periodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var endTimestamp = new DateTimeOffset(periodEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var candles = new List<CandleData>(periodEnd.DayNumber - periodStart.DayNumber == 1 ? 1440 : 45_000);
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split(',');
            if (fields.Length < 6 || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawTimestamp))
            {
                continue;
            }

            var timestampMilliseconds = rawTimestamp > 100_000_000_000_000L ? rawTimestamp / 1000 : rawTimestamp;
            var time = timestampMilliseconds / 1000;
            if (time < startTimestamp || time >= endTimestamp)
            {
                continue;
            }

            if (!TryParseDecimal(fields[1], out var open)
                || !TryParseDecimal(fields[2], out var high)
                || !TryParseDecimal(fields[3], out var low)
                || !TryParseDecimal(fields[4], out var close)
                || !TryParseDecimal(fields[5], out var volume))
            {
                continue;
            }

            candles.Add(new CandleData(time, open, high, low, close, volume));
        }

        return candles;
    }

    /// <summary>CSVの数値を小数点記号に依存せず解析します。</summary>
    /// <param name="value">CSV内の数値。</param>
    /// <param name="result">解析結果。</param>
    /// <returns>数値として解釈できた場合はtrue。</returns>
    private static bool TryParseDecimal(string value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
}