
using System.Diagnostics;
using SP.Shared.Resource;

namespace ResourceServer.Endpoints;

public static class JsonGatewayEndpoints
{
    public static void MapJsonGateway(this WebApplication app)
    {
        app.MapPost("/rpc", async (
            HttpRequest req, 
            JsonDispatcher dispatcher, 
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("JsonGateway");
            if (!req.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? true)
                return Results.BadRequest(new { error = "content-type must be application/json" });
            
            var sw = Stopwatch.StartNew();

            ReadOnlyMemory<byte> mem;
            var capacity = (int)(req.ContentLength ?? 4096);
            using (var ms = new MemoryStream(capacity))
            {
                await req.Body.CopyToAsync(ms, ct);

                mem = ms.TryGetBuffer(out var seg) 
                    ? seg.AsMemory(0, (int)ms.Length) 
                    : ms.ToArray().AsMemory();
            }
            
            var res = await dispatcher.DispatchAsync(mem, ct);
            sw.Stop();

            if (res is JsonResBase json)
            {
                logger.LogInformation("rpc handled: msgId={MsgId} elapsedMs={ElapsedMs} ok={Ok}",
                    json.MsgId, sw.ElapsedMilliseconds, json.Ok);
            }

            return Results.Json(res);
        });
    }
}
