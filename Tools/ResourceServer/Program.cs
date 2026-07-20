using SP.ResourceServer;

var builder = WebApplication.CreateBuilder(args);

// Auto-register handlers
var handlerInterfaceType = typeof(IRpcHandler<,>);
foreach (var type in typeof(RpcDispatcher).Assembly.GetTypes())
{
    if (type is { IsAbstract: false, IsInterface: false })
    {
        if (type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterfaceType))
        {
            builder.Services.AddSingleton(type);
        }
    }
}

builder.Services.AddSingleton<ServerRegistry>();
builder.Services.AddSingleton<RpcDispatcher>();

var app = builder.Build();

app.MapGet("/healthz", () => "ok");

app.MapPost("/rpc", async (HttpContext context, RpcDispatcher dispatcher) =>
{
    await dispatcher.HandleRequestAsync(context);
});

app.Run();
