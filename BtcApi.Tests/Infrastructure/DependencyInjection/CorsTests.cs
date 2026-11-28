using System.Net;
using BtcApi.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BtcApi.Tests.Infrastructure.DependencyInjection;

public sealed class CorsTests
{
    [Theory]
    [InlineData(null, "https://example.com", false)]
    [InlineData("https://example.com", "https://example.com", true)]
    [InlineData("https://example.com", "https://other.example.com", false)]
    public async Task Cors_AllowsOnlyConfiguredOrigins(string? configured, string origin, bool allowed)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        if (configured is not null)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = configured
            });
        }
        builder.Services.AddBtcCors(builder.Configuration);
        await using var app = builder.Build();
        app.UseCors();
        app.MapGet("/test", () => "ok");
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        using var client = new HttpClient { BaseAddress = new Uri(addresses.Addresses.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
        {
            Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }
    }

    [Fact]
    public async Task Cors_PreflightRejectsUnconfiguredMethod()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://example.com"
        });
        builder.Services.AddBtcCors(builder.Configuration);
        await using var app = builder.Build();
        app.UseCors();
        app.MapGet("/test", () => "ok");
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        using var client = new HttpClient { BaseAddress = new Uri(addresses.Addresses.Single()) };
        using var request = new HttpRequestMessage(HttpMethod.Options, "/test");
        request.Headers.Add("Origin", "https://example.com");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        using var response = await client.SendAsync(request);
        Assert.False(response.Headers.TryGetValues("Access-Control-Allow-Methods", out var methods)
            && methods.SelectMany(value => value.Split(',')).Any(method => method.Trim() == "POST"));
    }
}
