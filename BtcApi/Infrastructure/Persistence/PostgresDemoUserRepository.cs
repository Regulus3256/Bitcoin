using BtcApi.Infrastructure.Authentication;
using Npgsql;

namespace BtcApi.Infrastructure.Persistence;

public sealed class PostgresDemoUserRepository : IDemoUserRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresDemoUserRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS demo_users (
                username text PRIMARY KEY,
                password_hash text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DemoUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        const string sql = "SELECT username, password_hash FROM demo_users WHERE username = $1;";
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(DemoUser.NormalizeUsername(username));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new DemoUser(reader.GetString(0), reader.GetString(1))
            : null;
    }

    public async Task AddAsync(DemoUser user, CancellationToken cancellationToken)
    {
        const string sql = "INSERT INTO demo_users (username, password_hash) VALUES ($1, $2);";
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(DemoUser.NormalizeUsername(user.Username));
        command.Parameters.AddWithValue(user.PasswordHash);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException("A demo user with that username already exists.", exception);
        }
    }
}
