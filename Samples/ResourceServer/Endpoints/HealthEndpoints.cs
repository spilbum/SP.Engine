namespace ResourceServer.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthApi(this WebApplication app)
    {
        var g = app.MapGroup("/healthz").RequireRateLimiting("default");
        g.MapGet("/", () => Results.Content("ok", "text/plain"))
            .CacheOutput(p => p.Expire(TimeSpan.FromSeconds(5)));
    }
}
