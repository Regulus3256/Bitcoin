using System.Net;
using BtcApi.Infrastructure.HistoricalData;
using BtcApi.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BtcApi.Tests.Infrastructure.HistoricalData;

public sealed class BinanceVisionImporterFlowTests
{
    [Fact]
    public async Task ImportMissingMonthsAsync_ImportsYesterdayDailyArchive()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        var timestamp = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds();
        var archive = BinanceVisionCandleImporterTests.CreateArchiveForFlow(
            FormattableString.Invariant($"{timestamp},10,12,9,11,2"));
        Uri? requestUri = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive)
            });
        });
        var repository = new InMemoryCandleRepository();
        var importer = new BinanceVisionCandleImporter(
            new StubHttpClientFactory(handler),
            repository,
            Options.Create(new HistoricalImportOptions { StartDate = day }),
            NullLogger<BinanceVisionCandleImporter>.Instance);

        await importer.ImportMissingMonthsAsync(CancellationToken.None);

        Assert.Equal(day, repository.ImportedDay);
        Assert.Single(repository.ImportedDayCandles);
        Assert.Equal(11m, repository.ImportedDayCandles.Single().Close);
        Assert.NotNull(requestUri);
        Assert.Contains($"{day:yyyy-MM-dd}", requestUri.AbsolutePath);
    }

    [Fact]
    public async Task ImportMissingMonthsAsync_SkipsAlreadyImportedDay()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        var repository = new InMemoryCandleRepository { DayAlreadyImported = true };
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("HTTP request should not be sent for an imported day."));
        var importer = new BinanceVisionCandleImporter(
            new StubHttpClientFactory(handler),
            repository,
            Options.Create(new HistoricalImportOptions { StartDate = day }),
            NullLogger<BinanceVisionCandleImporter>.Instance);

        await importer.ImportMissingMonthsAsync(CancellationToken.None);

        Assert.Null(repository.ImportedDay);
    }
}