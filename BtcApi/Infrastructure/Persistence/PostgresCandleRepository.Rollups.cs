using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace BtcApi.Infrastructure.Persistence;

public sealed partial class PostgresCandleRepository
{
    /// <inheritdoc />
    public async Task RefreshRollupsAsync(
        MarketInstrument instrument,
        long minuteTime,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await RefreshRollupsForRangeAsync(
            connection, transaction, instrument, minuteTime, minuteTime + 60, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task BackfillRollupsAsync(MarketInstrument instrument, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using (var advisoryLock = new NpgsqlCommand(
            "SELECT pg_advisory_lock(hashtext($1)::bigint);", connection))
        {
            advisoryLock.Parameters.AddWithValue(instrument.ArchiveSymbol);
            await advisoryLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var check = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM candle_rollup_backfills WHERE symbol = $1 AND rollup_version >= $2);",
            connection))
        {
            check.Parameters.AddWithValue(instrument.ArchiveSymbol);
            check.Parameters.AddWithValue(RollupVersion);
            if ((bool)(await check.ExecuteScalarAsync(cancellationToken) ?? false))
            {
                _rollupsReady.TryAdd(instrument.Code, 0);
                return;
            }
        }

        await RefreshRollupIntervalAsync(
            connection, null, instrument, 1, 60, 3_600, null, null, cancellationToken);
        await RefreshRollupIntervalAsync(
            connection, null, instrument, 60, 240, 14_400, null, null, cancellationToken);
        await RefreshRollupIntervalAsync(
            connection, null, instrument, 60, 1440, 86_400, null, null, cancellationToken);
        await RefreshRollupIntervalAsync(
            connection, null, instrument, 1440, 10080, 604_800, null, null, cancellationToken);

        await using var mark = new NpgsqlCommand(
            """
            INSERT INTO candle_rollup_backfills (symbol, rollup_version, completed_at)
            VALUES ($1, $2, now())
            ON CONFLICT (symbol) DO UPDATE SET
                rollup_version = EXCLUDED.rollup_version,
                completed_at = EXCLUDED.completed_at;
            """,
            connection);
        mark.Parameters.AddWithValue(instrument.ArchiveSymbol);
        mark.Parameters.AddWithValue(RollupVersion);
        await mark.ExecuteNonQueryAsync(cancellationToken);
        _rollupsReady.TryAdd(instrument.Code, 0);
    }

    private async Task<CandleData[]> GetStoredRollupsAsync(
        MarketInstrument instrument,
        CandleTimeframe timeframe,
        long? from,
        long? through,
        long? before,
        int limit,
        CancellationToken cancellationToken)
    {
        var sortDirection = from.HasValue ? "ASC" : "DESC";
        var sql = $"""
            SELECT (extract(epoch FROM open_time))::bigint, open, high, low, close, volume
            FROM btc_candles
            WHERE symbol = $1
              AND interval_minutes = $2
              AND ($3 IS NULL OR open_time >= to_timestamp($3))
              AND ($4 IS NULL OR open_time < to_timestamp($4))
              AND ($5 IS NULL OR open_time < to_timestamp($5))
            ORDER BY open_time {sortDirection}
            LIMIT $6;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(instrument.ArchiveSymbol);
        command.Parameters.AddWithValue(timeframe.KrakenIntervalMinutes);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = (object?)from ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = (object?)through ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = (object?)before ?? DBNull.Value });
        command.Parameters.AddWithValue(limit);

        var candles = new List<CandleData>(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candles.Add(new CandleData(
                reader.GetInt64(0),
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5)));
        }

        if (!from.HasValue)
        {
            candles.Reverse();
        }
        return candles.ToArray();
    }

    private async Task RefreshRollupsForRangeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        MarketInstrument instrument,
        long from,
        long through,
        CancellationToken cancellationToken)
    {
        var hourStart = FloorToBucket(from, 3_600);
        var hourEnd = FloorToBucket(through - 1, 3_600) + 3_600;
        var fourHourStart = FloorToBucket(from, 14_400);
        var fourHourEnd = FloorToBucket(through - 1, 14_400) + 14_400;
        var dayStart = FloorToBucket(from, 86_400);
        var dayEnd = FloorToBucket(through - 1, 86_400) + 86_400;
        var weekStart = FloorToBucket(from, 604_800);
        var weekEnd = FloorToBucket(through - 1, 604_800) + 604_800;

        await RefreshRollupIntervalAsync(
            connection, transaction, instrument, 1, 60, 3_600, hourStart, hourEnd, cancellationToken);
        await RefreshRollupIntervalAsync(
            connection, transaction, instrument, 60, 240, 14_400, fourHourStart, fourHourEnd, cancellationToken);
        await RefreshRollupIntervalAsync(
            connection, transaction, instrument, 60, 1440, 86_400, dayStart, dayEnd, cancellationToken);
        await RefreshRollupIntervalAsync(
            connection, transaction, instrument, 1440, 10080, 604_800, weekStart, weekEnd, cancellationToken);
    }

    private static long FloorToBucket(long timestamp, long bucketSeconds) =>
        timestamp / bucketSeconds * bucketSeconds;

    private static async Task RefreshRollupIntervalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        MarketInstrument instrument,
        int sourceIntervalMinutes,
        int targetIntervalMinutes,
        long bucketSeconds,
        long? from,
        long? through,
        CancellationToken cancellationToken)
    {
        var rangeFilter = from.HasValue
            ? "AND open_time >= to_timestamp($5) AND open_time < to_timestamp($6)"
            : string.Empty;
        var sql = $"""
            WITH grouped AS (
                SELECT
                    floor(extract(epoch FROM open_time) / $2::bigint)::bigint * $2::bigint AS bucket_time,
                    min(open_time) AS first_candle_time,
                    max(open_time) AS last_candle_time,
                    max(high) AS candle_high,
                    min(low) AS candle_low,
                    sum(volume) AS candle_volume
                FROM btc_candles
                WHERE symbol = $1
                  AND interval_minutes = $3
                  {rangeFilter}
                GROUP BY bucket_time
            )
            INSERT INTO btc_candles (symbol, interval_minutes, open_time, open, high, low, close, volume, source)
            SELECT
                $1,
                $4,
                to_timestamp(grouped.bucket_time),
                (SELECT open FROM btc_candles WHERE symbol = $1 AND interval_minutes = $3 AND open_time = grouped.first_candle_time),
                grouped.candle_high,
                grouped.candle_low,
                (SELECT close FROM btc_candles WHERE symbol = $1 AND interval_minutes = $3 AND open_time = grouped.last_candle_time),
                grouped.candle_volume,
                'derived'
            FROM grouped
            ON CONFLICT (symbol, interval_minutes, open_time) DO UPDATE SET
                open = EXCLUDED.open,
                high = EXCLUDED.high,
                low = EXCLUDED.low,
                close = EXCLUDED.close,
                volume = EXCLUDED.volume,
                source = EXCLUDED.source;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(instrument.ArchiveSymbol);
        command.Parameters.AddWithValue(bucketSeconds);
        command.Parameters.AddWithValue(sourceIntervalMinutes);
        command.Parameters.AddWithValue(targetIntervalMinutes);
        if (from.HasValue)
        {
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = from.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = through!.Value });
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}
