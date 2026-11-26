using System.Text;
using BtcApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace BtcApi.Infrastructure.Authentication;

public static class DemoUserCommand
{
    private const int MinimumPasswordLength = 12;

    public static async Task RunAsync(IConfiguration configuration, string[] arguments)
    {
        if (arguments.Length != 1)
        {
            throw new ArgumentException("Usage: dotnet run --project BtcApi -- create-demo-user <username>");
        }
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException("Run this command in an interactive terminal so the password can be entered securely.");
        }

        var username = DemoUser.NormalizeUsername(arguments[0]);
        var password = ReadPassword("Password: ");
        var confirmation = ReadPassword("Confirm password: ");
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The passwords do not match.");
        }
        if (password.Length < MinimumPasswordLength)
        {
            throw new InvalidOperationException($"Password must be at least {MinimumPasswordLength} characters.");
        }

        var connectionString = configuration.GetConnectionString("MarketData")
            ?? throw new InvalidOperationException("ConnectionStrings:MarketData is not configured.");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var repository = new PostgresDemoUserRepository(dataSource);
        await repository.InitializeAsync(CancellationToken.None);

        var user = new DemoUser(username, string.Empty);
        var passwordHasher = new PasswordHasher<DemoUser>();
        await repository.AddAsync(
            user with { PasswordHash = passwordHasher.HashPassword(user, password) },
            CancellationToken.None);
        Console.WriteLine($"Demo user '{username}' created.");
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0) password.Length -= 1;
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }
    }
}
