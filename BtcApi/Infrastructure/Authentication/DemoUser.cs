namespace BtcApi.Infrastructure.Authentication;

public sealed record DemoUser(string Username, string PasswordHash)
{
    public static string NormalizeUsername(string username)
    {
        var normalized = username.Trim().ToLowerInvariant();
        if (normalized.Length is < 3 or > 64
            || normalized.Any(character =>
                !(character is >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '.' or '_' or '@' or '+' or '-')))
        {
            throw new ArgumentException("Username must be 3-64 characters using letters, numbers, '.', '_', '@', '+', or '-'.", nameof(username));
        }

        return normalized;
    }
}
