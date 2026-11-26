using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BtcApi.Infrastructure.Authentication;

public sealed class DemoBasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DemoBasic";

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private readonly IDemoUserRepository _users;
    private readonly IPasswordHasher<DemoUser> _passwordHasher;

    public DemoBasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder,
        IDemoUserRepository users,
        IPasswordHasher<DemoUser> passwordHasher)
        : base(options, logger, encoder)
    {
        _users = users;
        _passwordHasher = passwordHasher;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authorization)
            || !string.Equals(authorization.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(authorization.Parameter))
        {
            return AuthenticateResult.NoResult();
        }

        string decoded;
        try
        {
            decoded = StrictUtf8.GetString(Convert.FromBase64String(authorization.Parameter));
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail("Invalid Basic authentication credentials.");
        }
        catch (DecoderFallbackException)
        {
            return AuthenticateResult.Fail("Invalid Basic authentication credentials.");
        }

        var separator = decoded.IndexOf(':');
        if (separator <= 0)
        {
            return AuthenticateResult.Fail("Invalid Basic authentication credentials.");
        }

        string username;
        try
        {
            username = DemoUser.NormalizeUsername(decoded[..separator]);
        }
        catch (ArgumentException)
        {
            return AuthenticateResult.Fail("Invalid Basic authentication credentials.");
        }

        var user = await _users.GetByUsernameAsync(username, Context.RequestAborted);
        if (user is null
            || _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, decoded[(separator + 1)..])
                == PasswordVerificationResult.Failed)
        {
            return AuthenticateResult.Fail("Invalid Basic authentication credentials.");
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, user.Username)],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers["WWW-Authenticate"] = "Basic realm=\"DemoTrading\", charset=\"UTF-8\"";
        return Task.CompletedTask;
    }
}
