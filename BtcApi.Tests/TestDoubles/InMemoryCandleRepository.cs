using BtcApi.Application.Abstractions;
using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;

namespace BtcApi.Tests.TestDoubles;

internal sealed class InMemoryCandleRepository : ICandleRepository
{
    public int GetCandlesCallCount { get; private set; }
    public MarketInstrument? LastInstrument { get; private set; }
    public CandleTimeframe? LastTimeframe { get; private set; }
    public long? LastFrom { get; private set; }
    public long? LastThrough { get; private set; }
    public long? LastBefore { get; private set; }
    public int LastLimit { get; private set; }
    public CandleData[] CandlesToReturn { get; set; } = [];
    public CandleData? LatestCandle { get; set; }
    public bool DayAlreadyImported { get; set; }
    public DateOnly? ImportedDay { get; private set; }
    public IReadOnlyCollection<CandleData> ImportedDayCandles { get; private set; } = [];
    public List<(CandleData Candle, string Source)> Upserts { get; } = [];
    public List<(MarketInstrument Instrument, long MinuteTime)> RollupRefreshes { get; } = [];

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<CandleData[]> GetCandlesAsync(
        MarketInstrument instrument,
        CandleTimeframe timeframe,
        long? from,
        long? through,
        long? before,
        int limit,
        CancellationToken cancellationToken)
    {
        GetCandlesCallCount += 1;
        LastInstrument = instrument;
        LastTimeframe = timeframe;
        LastFrom = from;
        LastThrough = through;
        LastBefore = before;
        LastLimit = limit;
        return Task.FromResult(CandlesToReturn);
    }

    public Task RefreshRollupsAsync(MarketInstrument instrument, long minuteTime, CancellationToken cancellationToken)
    {
        RollupRefreshes.Add((instrument, minuteTime));
        return Task.CompletedTask;
    }

    public Task BackfillRollupsAsync(MarketInstrument instrument, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<CandleData?> GetLatestMinuteCandleAsync(MarketInstrument instrument, CancellationToken cancellationToken) =>
        Task.FromResult(LatestCandle);

    public Task<CandleCoverage> GetCoverageAsync(MarketInstrument instrument, CancellationToken cancellationToken) =>
        Task.FromResult(new CandleCoverage(0, null, null));

    public Task UpsertMinuteCandleAsync(MarketInstrument instrument, CandleData candle, string source, CancellationToken cancellationToken)
    {
        Upserts.Add((candle, source));
        return Task.CompletedTask;
    }

    public Task UpsertMinuteCandlesAsync(MarketInstrument instrument, IReadOnlyCollection<CandleData> candles, string source, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<bool> IsMonthImportedAsync(MarketInstrument instrument, DateOnly month, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<bool> IsDayImportedAsync(MarketInstrument instrument, DateOnly day, CancellationToken cancellationToken) =>
        Task.FromResult(DayAlreadyImported);

    public Task<int> ImportMonthAsync(MarketInstrument instrument, DateOnly month, IReadOnlyCollection<CandleData> candles, CancellationToken cancellationToken) =>
        Task.FromResult(candles.Count);

    public Task<int> ImportDayAsync(MarketInstrument instrument, DateOnly day, IReadOnlyCollection<CandleData> candles, CancellationToken cancellationToken)
    {
        ImportedDay = day;
        ImportedDayCandles = candles.ToArray();
        return Task.FromResult(candles.Count);
    }
}