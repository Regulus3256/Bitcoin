using System.Data;
using BtcApi.Application.Abstractions;
using BtcApi.Domain.MarketData;
using BtcApi.Domain.Models;
using Npgsql;
using NpgsqlTypes;

namespace BtcApi.Infrastructure.Persistence;

/// <summary>PostgreSQLへローソク足とアーカイブ取込状態を保存します。</summary>
public sealed class PostgresCandleRepository : ICandleRepository
{
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>PostgreSQL接続先からローソク足リポジトリを生成します。</summary>
    /// <param name="dataSource">アプリケーションで共有するNpgsql接続元。</param>
    public PostgresCandleRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS btc_candles (
                symbol text NOT NULL,
                interval_minutes integer NOT NULL,
                open_time timestamptz NOT NULL,
                open numeric(20, 8) NOT NULL,
                high numeric(20, 8) NOT NULL,
                low numeric(20, 8) NOT NULL,
                close numeric(20, 8) NOT NULL,
                volume numeric(30, 12) NOT NULL DEFAULT 0,
                source text NOT NULL,
                PRIMARY KEY (symbol, interval_minutes, open_time)
            );

            CREATE INDEX IF NOT EXISTS ix_btc_candles_time
                ON btc_candles (symbol, interval_minutes, open_time DESC);

            CREATE TABLE IF NOT EXISTS candle_import_months (
                symbol text NOT NULL,
                interval_minutes integer NOT NULL,
                month_start date NOT NULL,
                rows_imported integer NOT NULL,
                imported_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (symbol, interval_minutes, month_start)
            );

            CREATE TABLE IF NOT EXISTS candle_import_days (
                symbol text NOT NULL,
                interval_minutes integer NOT NULL,
                day_start date NOT NULL,
                rows_imported integer NOT NULL,
                imported_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (symbol, interval_minutes, day_start)
            );
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CandleData[]> GetCandlesAsync(
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
            WITH grouped AS (
                SELECT
                    floor(extract(epoch FROM open_time) / ($2::bigint * 60))::bigint * ($2::bigint * 60) AS candle_time,
                    (array_agg(open ORDER BY open_time ASC))[1] AS candle_open,
                    max(high) AS candle_high,
                    min(low) AS candle_low,
                    (array_agg(close ORDER BY open_time DESC))[1] AS candle_close,
                    sum(volume) AS candle_volume
                FROM btc_candles
                WHERE symbol = $1
                  AND interval_minutes = 1
                  AND ($3 IS NULL OR open_time >= to_timestamp($3))
                  AND ($4 IS NULL OR open_time < to_timestamp($4))
                  AND ($5 IS NULL OR open_time < to_timestamp($5))
                GROUP BY candle_time
            )
            SELECT candle_time, candle_open, candle_high, candle_low, candle_close, candle_volume
            FROM grouped
            ORDER BY candle_time {sortDirection}
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

    /// <inheritdoc />
    public async Task<CandleData?> GetLatestMinuteCandleAsync(MarketInstrument instrument, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT open_time, open, high, low, close, volume
            FROM btc_candles
            WHERE symbol = $1 AND interval_minutes = 1
            ORDER BY open_time DESC
            LIMIT 1;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(instrument.ArchiveSymbol);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CandleData(
            reader.GetFieldValue<DateTimeOffset>(0).ToUnixTimeSeconds(),
            reader.GetDecimal(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5));
    }

    /// <inheritdoc />
    public async Task<CandleCoverage> GetCoverageAsync(MarketInstrument instrument, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT count(*),
                   (extract(epoch FROM min(open_time)))::bigint,
                   (extract(epoch FROM max(open_time)))::bigint
            FROM btc_candles
            WHERE symbol = $1 AND interval_minutes = 1;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(instrument.ArchiveSymbol);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new CandleCoverage(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2));
    }

    /// <inheritdoc />
    public async Task UpsertMinuteCandleAsync(MarketInstrument instrument, CandleData candle, string source, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO btc_candles (symbol, interval_minutes, open_time, open, high, low, close, volume, source)
            VALUES ($1, 1, to_timestamp($2), $3, $4, $5, $6, $7, $8)
            ON CONFLICT (symbol, interval_minutes, open_time) DO UPDATE SET
                high = GREATEST(btc_candles.high, EXCLUDED.high),
                low = LEAST(btc_candles.low, EXCLUDED.low),
                close = EXCLUDED.close,
                volume = EXCLUDED.volume,
                source = EXCLUDED.source;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(instrument.ArchiveSymbol);
        command.Parameters.AddWithValue(candle.Time);
        command.Parameters.AddWithValue(candle.Open);
        command.Parameters.AddWithValue(candle.High);
        command.Parameters.AddWithValue(candle.Low);
        command.Parameters.AddWithValue(candle.Close);
        command.Parameters.AddWithValue(candle.Volume);
        command.Parameters.AddWithValue(source);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertMinuteCandlesAsync(
        MarketInstrument instrument,
        IReadOnlyCollection<CandleData> candles,
        string source,
        CancellationToken cancellationToken)
    {
        if (candles.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO btc_candles (symbol, interval_minutes, open_time, open, high, low, close, volume, source)
            VALUES ($1, 1, to_timestamp($2), $3, $4, $5, $6, $7, $8)
            ON CONFLICT (symbol, interval_minutes, open_time) DO UPDATE SET
                high = GREATEST(btc_candles.high, EXCLUDED.high),
                low = LEAST(btc_candles.low, EXCLUDED.low),
                close = EXCLUDED.close,
                volume = EXCLUDED.volume,
                source = EXCLUDED.source;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var batch = new NpgsqlBatch(connection, transaction);
        foreach (var candle in candles)
        {
            var command = new NpgsqlBatchCommand(sql);
            command.Parameters.AddWithValue(instrument.ArchiveSymbol);
            command.Parameters.AddWithValue(candle.Time);
            command.Parameters.AddWithValue(candle.Open);
            command.Parameters.AddWithValue(candle.High);
            command.Parameters.AddWithValue(candle.Low);
            command.Parameters.AddWithValue(candle.Close);
            command.Parameters.AddWithValue(candle.Volume);
            command.Parameters.AddWithValue(source);
            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsMonthImportedAsync(MarketInstrument instrument, DateOnly month, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM candle_import_months
                WHERE symbol = $1 AND interval_minutes = 1 AND month_start = $2
            );
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(instrument.ArchiveSymbol);
        command.Parameters.AddWithValue(month.ToDateTime(TimeOnly.MinValue));
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    /// <inheritdoc />
    public async Task<bool> IsDayImportedAsync(MarketInstrument instrument, DateOnly day, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM candle_import_days
                WHERE symbol = $1 AND interval_minutes = 1 AND day_start = $2
            );
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(instrument.ArchiveSymbol);
        command.Parameters.AddWithValue(day.ToDateTime(TimeOnly.MinValue));
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    /// <inheritdoc />
    public async Task<int> ImportMonthAsync(MarketInstrument instrument, DateOnly month, IReadOnlyCollection<CandleData> candles, CancellationToken cancellationToken)
    {
        return await ImportPeriodAsync(instrument, month, candles, "candle_import_months", "month_start", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> ImportDayAsync(MarketInstrument instrument, DateOnly day, IReadOnlyCollection<CandleData> candles, CancellationToken cancellationToken)
    {
        return await ImportPeriodAsync(instrument, day, candles, "candle_import_days", "day_start", cancellationToken);
    }

    /// <summary>アーカイブデータと取込済み期間の印を同一トランザクションで保存します。</summary>
    /// <param name="period">対象月または日。</param>
    /// <param name="candles">一括保存するローソク足。</param>
    /// <param name="ledgerTable">記録先台帳テーブル名。</param>
    /// <param name="dateColumn">台帳の日付列名。</param>
    /// <param name="cancellationToken">処理を中断するトークン。</param>
    /// <returns>新規に追加されたローソク足数。</returns>
    private async Task<int> ImportPeriodAsync(
        MarketInstrument instrument,
        DateOnly period,
        IReadOnlyCollection<CandleData> candles,
        string ledgerTable,
        string dateColumn,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var createStage = new NpgsqlCommand("""
            CREATE TEMP TABLE candle_import_stage (
                open_time timestamptz NOT NULL,
                open numeric(20, 8) NOT NULL,
                high numeric(20, 8) NOT NULL,
                low numeric(20, 8) NOT NULL,
                close numeric(20, 8) NOT NULL,
                volume numeric(30, 12) NOT NULL
            ) ON COMMIT DROP;
            """, connection, transaction))
        {
            await createStage.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var importer = await connection.BeginBinaryImportAsync(
            "COPY candle_import_stage (open_time, open, high, low, close, volume) FROM STDIN (FORMAT BINARY)",
            cancellationToken))
        {
            foreach (var candle in candles)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(DateTimeOffset.FromUnixTimeSeconds(candle.Time), NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(candle.Open, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(candle.High, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(candle.Low, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(candle.Close, NpgsqlDbType.Numeric, cancellationToken);
                await importer.WriteAsync(candle.Volume, NpgsqlDbType.Numeric, cancellationToken);
            }
            await importer.CompleteAsync(cancellationToken);
        }

        const string insertSql = """
            WITH inserted AS (
                INSERT INTO btc_candles (symbol, interval_minutes, open_time, open, high, low, close, volume, source)
                SELECT $1, 1, open_time, open, high, low, close, volume, 'binance-vision'
                FROM candle_import_stage
                ON CONFLICT (symbol, interval_minutes, open_time) DO NOTHING
                RETURNING 1
            )
            SELECT count(*)::integer FROM inserted;
            """;
        int insertedRows;
        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue(instrument.ArchiveSymbol);
            insertedRows = (int)(await insert.ExecuteScalarAsync(cancellationToken) ?? 0);
        }

        var markSql = $"""
            INSERT INTO {ledgerTable} (symbol, interval_minutes, {dateColumn}, rows_imported)
            VALUES ($1, 1, $2, $3)
            ON CONFLICT (symbol, interval_minutes, {dateColumn}) DO UPDATE SET
                rows_imported = EXCLUDED.rows_imported,
                imported_at = now();
            """;
        await using (var mark = new NpgsqlCommand(markSql, connection, transaction))
        {
            mark.Parameters.AddWithValue(instrument.ArchiveSymbol);
            mark.Parameters.AddWithValue(period.ToDateTime(TimeOnly.MinValue));
            mark.Parameters.AddWithValue(candles.Count);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return insertedRows;
    }
}