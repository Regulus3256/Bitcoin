using System.Collections.Concurrent;
using BtcApi.Application.Abstractions;
using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;

namespace BtcApi.Application.Services;

/// <summary>ライブ価格を1分単位に集約し、過去足ストアへ定期保存します。</summary>
public sealed class LiveMinuteCandleRecorder : ILiveCandleRecorder
{
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(5);
    private readonly ICandleRepository _repository;
    private readonly ILogger<LiveMinuteCandleRecorder> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, CandleData> _currentCandles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastPersistedAtUtc = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ライブ足記録サービスを生成します。</summary>
    /// <param name="repository">1分足の永続化先。</param>
    public LiveMinuteCandleRecorder(ICandleRepository repository, ILogger<LiveMinuteCandleRecorder> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordAsync(PriceSnapshot snapshot, CancellationToken cancellationToken)
    {
        var instrument = MarketInstrument.FromCode(snapshot.Symbol);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var updatedAt = new DateTimeOffset(DateTime.SpecifyKind(snapshot.UpdatedAt, DateTimeKind.Utc));
            var timestamp = updatedAt.ToUnixTimeSeconds();
            var minuteStart = timestamp / 60 * 60;
            if (!_currentCandles.TryGetValue(instrument.Code, out var currentCandle))
            {
                var latest = await _repository.GetLatestMinuteCandleAsync(instrument, cancellationToken);
                currentCandle = latest?.Time == minuteStart
                    ? latest
                    : new CandleData(minuteStart, snapshot.PriceUsd, snapshot.PriceUsd, snapshot.PriceUsd, snapshot.PriceUsd);
            }
            else if (currentCandle.Time != minuteStart)
            {
                currentCandle = new CandleData(minuteStart, snapshot.PriceUsd, snapshot.PriceUsd, snapshot.PriceUsd, snapshot.PriceUsd);
            }
            else
            {
                currentCandle = currentCandle with
                {
                    High = Math.Max(currentCandle.High, snapshot.PriceUsd),
                    Low = Math.Min(currentCandle.Low, snapshot.PriceUsd),
                    Close = snapshot.PriceUsd
                };
            }
            _currentCandles[instrument.Code] = currentCandle;

            var lastPersisted = _lastPersistedAtUtc.GetValueOrDefault(instrument.Code, DateTime.MinValue);
            if (updatedAt.UtcDateTime - lastPersisted >= PersistInterval)
            {
                try
                {
                    await _repository.UpsertMinuteCandleAsync(instrument, currentCandle, "kraken-live", cancellationToken);
                    _lastPersistedAtUtc[instrument.Code] = updatedAt.UtcDateTime;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "ライブ1分足のDB保存に失敗しました");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}