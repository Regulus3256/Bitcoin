using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BtcApi.Endpoints;
using BtcApi.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BtcApi.Tests.Infrastructure.Authentication;

public sealed class DemoBasicAuthenticationHandlerTests
{
    [Fact]
    public async Task DemoRouteRequiresValidBasicCredentialsAndLeavesPublicRouteOpen()
    {
        var contentRootPath = Path.Combine(Path.GetTempPath(), $"BtcApi.Tests-{Guid.NewGuid():N}");
        var webRootPath = Path.Combine(contentRootPath, "wwwroot");
        var demoPath = Path.Combine(contentRootPath, "Demo");
        Directory.CreateDirectory(webRootPath);
        Directory.CreateDirectory(demoPath);
        await File.WriteAllTextAsync(Path.Combine(webRootPath, "index.html"), "public page");
        await File.WriteAllTextAsync(Path.Combine(demoPath, "demo.html"), "demo page");

        try
        {
            var user = new DemoUser("demo-user", string.Empty);
            var passwordHasher = new PasswordHasher<DemoUser>();
            user = user with { PasswordHash = passwordHasher.HashPassword(user, "test-password-123") };

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = contentRootPath,
                WebRootPath = webRootPath
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton<IDemoUserRepository>(new InMemoryDemoUserRepository(user));
            builder.Services.AddSingleton<IPasswordHasher<DemoUser>>(passwordHasher);
            builder.Services.AddAuthentication(DemoBasicAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, DemoBasicAuthenticationHandler>(
                    DemoBasicAuthenticationHandler.SchemeName,
                    _ => { });
            builder.Services.AddAuthorization(options =>
                options.AddPolicy(DemoEndpoints.AuthorizationPolicyName, policy =>
                {
                    policy.AddAuthenticationSchemes(DemoBasicAuthenticationHandler.SchemeName);
                    policy.RequireAuthenticatedUser();
                }));

            await using var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGet("/public", () => Results.Ok("public"));
            app.MapDemoEndpoints();
            await app.StartAsync();

            var server = app.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()!;
            using var client = new HttpClient { BaseAddress = new Uri(addresses.Addresses.Single()) };

            using var publicResponse = await client.GetAsync("/public");
            Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);

            using var indexResponse = await client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, indexResponse.StatusCode);
            Assert.Equal("public page", await indexResponse.Content.ReadAsStringAsync());

            using var directFileResponse = await client.GetAsync("/Demo/demo.html");
            Assert.Equal(HttpStatusCode.NotFound, directFileResponse.StatusCode);

            using var challengeResponse = await client.GetAsync("/demo");
            Assert.Equal(HttpStatusCode.Unauthorized, challengeResponse.StatusCode);
            Assert.Contains(challengeResponse.Headers.WwwAuthenticate, header => header.Scheme == "Basic");

            using var invalidRequest = CreateDemoRequest("demo-user", "wrong-password");
            using var invalidResponse = await client.SendAsync(invalidRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);

            using var validRequest = CreateDemoRequest("DEMO-USER", "test-password-123");
            using var validResponse = await client.SendAsync(validRequest);
            Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
            Assert.Equal("demo page", await validResponse.Content.ReadAsStringAsync());
        }
        finally
        {
            Directory.Delete(contentRootPath, recursive: true);
        }
    }

    private static HttpRequestMessage CreateDemoRequest(string username, string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/demo");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return request;
    }

    private sealed class InMemoryDemoUserRepository : IDemoUserRepository
    {
        private readonly DemoUser _user;

        public InMemoryDemoUserRepository(DemoUser user)
        {
            _user = user;
        }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DemoUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(username == _user.Username ? _user : null);

        public Task AddAsync(DemoUser user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
