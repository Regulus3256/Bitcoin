using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;
using BtcApi.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace BtcApi.Tests.Infrastructure.Persistence;

public sealed class PostgresCandleRepositoryTests
{
    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ImportDay_PersistsLedgerAndRollups_AndRollsBackOnFailure()
    {
        var connectionString = Environment.GetEnvironmentVariable("BTC_TEST_POSTGRES")!;
        var schema = $"btc_test_{Guid.NewGuid():N}";
        await using var admin = NpgsqlDataSource.Create(connectionString);
        await using (var command = admin.CreateCommand($"CREATE SCHEMA \"{schema}\""))
        {
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
            var repository = new PostgresCandleRepository(dataSource);
            var instrument = MarketInstrument.FromCode("BTC");
            var day = new DateOnly(2024, 1, 1);
            var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            CandleData[] candles =
            [
                new(start, 10, 12, 9, 11, 2),
                new(start + 60, 11, 15, 10, 14, 3)
            ];
            await repository.InitializeAsync(CancellationToken.None);
            await repository.BackfillRollupsAsync(instrument, CancellationToken.None);
            Assert.Equal(2, await repository.ImportDayAsync(instrument, day, candles, CancellationToken.None));
            Assert.True(await repository.IsDayImportedAsync(instrument, day, CancellationToken.None));
            Assert.Equal(0, await repository.ImportDayAsync(instrument, day, candles, CancellationToken.None));

            var hour = Assert.Single(await repository.GetCandlesAsync(
                instrument, CandleTimeframe.FromKey("1h"), start, null, null, 20, CancellationToken.None));
            Assert.Equal(new CandleData(start, 10, 15, 9, 14, 5), hour);
            Assert.Equal(2, (await repository.GetCoverageAsync(instrument, CancellationToken.None)).CandleCount);

            var nextDay = day.AddDays(1);
            CandleData[] invalid = [new(start + 86400, decimal.MaxValue, 1, 1, 1, 1)];
            await Assert.ThrowsAsync<PostgresException>(() =>
                repository.ImportDayAsync(instrument, nextDay, invalid, CancellationToken.None));
            Assert.False(await repository.IsDayImportedAsync(instrument, nextDay, CancellationToken.None));
            Assert.Equal(2, (await repository.GetCoverageAsync(instrument, CancellationToken.None)).CandleCount);
        }
        finally
        {
            await using var command = admin.CreateCommand($"DROP SCHEMA \"{schema}\" CASCADE");
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BTC_TEST_POSTGRES")))
            {
                Skip = "Set BTC_TEST_POSTGRES to a dedicated test database to run PostgreSQL integration tests.";
            }
        }
    }
}
