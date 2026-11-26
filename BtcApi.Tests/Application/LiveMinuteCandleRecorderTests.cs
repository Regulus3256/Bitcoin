using BtcApi.Application.Services;
using BtcApi.Domain.Models;
using BtcApi.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BtcApi.Tests.Application;

public sealed class LiveMinuteCandleRecorderTests
{
    [Fact]
    public async Task RecordAsync_UpdatesOhlcAndPersistsAtFiveSecondIntervals()
    {
        var repository = new InMemoryCandleRepository();
        var recorder = new LiveMinuteCandleRecorder(repository, NullLogger<LiveMinuteCandleRecorder>.Instance);
        var start = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        await recorder.RecordAsync(new PriceSnapshot("BTC", 100m, start), CancellationToken.None);
        await recorder.RecordAsync(new PriceSnapshot("BTC", 110m, start.AddSeconds(2)), CancellationToken.None);
        await recorder.RecordAsync(new PriceSnapshot("BTC", 90m, start.AddSeconds(6)), CancellationToken.None);

        Assert.Equal(2, repository.Upserts.Count);
        var candle = repository.Upserts[^1].Candle;
        Assert.Equal(100m, candle.Open);
        Assert.Equal(110m, candle.High);
        Assert.Equal(90m, candle.Low);
        Assert.Equal(90m, candle.Close);
        Assert.Equal("kraken-live", repository.Upserts[^1].Source);
    }

    [Fact]
    public async Task RecordAsync_StartsFreshCandleAtMinuteBoundary()
    {
        var repository = new InMemoryCandleRepository();
        var recorder = new LiveMinuteCandleRecorder(repository, NullLogger<LiveMinuteCandleRecorder>.Instance);
        var start = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        await recorder.RecordAsync(new PriceSnapshot("BTC", 100m, start), CancellationToken.None);
        await recorder.RecordAsync(new PriceSnapshot("BTC", 90m, start.AddSeconds(61)), CancellationToken.None);

        var nextMinute = repository.Upserts[^1].Candle;
        Assert.Equal(new DateTimeOffset(start.AddMinutes(1)).ToUnixTimeSeconds(), nextMinute.Time);
        Assert.Equal(90m, nextMinute.Open);
        Assert.Equal(90m, nextMinute.High);
        Assert.Equal(90m, nextMinute.Low);
        Assert.Equal(90m, nextMinute.Close);
    }
}