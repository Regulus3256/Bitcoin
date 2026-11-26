namespace BtcApi.Endpoints;

public static class DemoEndpoints
{
    public const string AuthorizationPolicyName = "DemoRoute";

    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/demo", (IWebHostEnvironment environment) =>
        {
            var demoPath = Path.Combine(environment.ContentRootPath, "Demo", "demo.html");
            return Results.File(demoPath, "text/html; charset=utf-8");
        }).RequireAuthorization(AuthorizationPolicyName);

        return endpoints;
    }
}
