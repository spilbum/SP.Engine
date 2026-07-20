using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SP.Resource;
using SP.Resource.Web;

namespace SP.ResourceServer;

public sealed class RpcDispatcher
{
    private static readonly JsonSerializer _serializer = JsonSerializer.CreateDefault();
    
    // Delegate signature: (payloadToken, serviceProvider) => Task<object?>
    private readonly Dictionary<int, Func<JToken?, IServiceProvider, Task<object?>>> _handlers = new();
    private readonly IServiceProvider _serviceProvider;

    public RpcDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        BuildHandlerDictionary();
    }

    private void BuildHandlerDictionary()
    {
        var assembly = typeof(RpcDispatcher).Assembly;
        var handlerInterfaceType = typeof(IRpcHandler<,>);

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;

            var interfaceImpl = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterfaceType);

            if (interfaceImpl == null) continue;
            
            var reqType = interfaceImpl.GetGenericArguments()[0];
            var resType = interfaceImpl.GetGenericArguments()[1];

            var reqInstance = Activator.CreateInstance(reqType);
            var rpcRequestInterface = typeof(IRpcRequest<>).MakeGenericType(resType);
            var msgIdProp = rpcRequestInterface.GetProperty("MsgId");
                
            if (msgIdProp == null) continue;
                
            var msgId = (int)msgIdProp.GetValue(reqInstance)!;
            var method = type.GetMethod("HandleAsync");
                
            if (method == null) continue;

            _handlers[msgId] = async (payloadToken, sp) =>
            {
                var handler = sp.GetRequiredService(type);
                var req = payloadToken?.ToObject(reqType, _serializer);
                var task = (Task)method.Invoke(handler, new[] { req })!;
                    
                await task.ConfigureAwait(false);
                    
                var resultProp = task.GetType().GetProperty("Result");
                return resultProp!.GetValue(task);
            };
        }
    }

    public async Task HandleRequestAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        using var jsonReader = new JsonTextReader(reader);
        
        var jObj = await JObject.LoadAsync(jsonReader, context.RequestAborted);
        if (!jObj.TryGetValue("MsgId", out var msgIdToken))
        {
            await WriteErrorAsync(context, 0, ErrorCode.Unknown, "Missing MsgId");
            return;
        }

        var msgId = msgIdToken.Value<int>();
        var payloadToken = jObj["Payload"];

        try
        {
            if (_handlers.TryGetValue(msgId, out var handlerDelegate))
            {
                var resPayload = await handlerDelegate(payloadToken, _serviceProvider);
                await WriteSuccessAsync(context, msgId, resPayload);
            }
            else
            {
                throw new Exception($"Unknown MsgId: {msgId}");
            }
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(context, msgId, ErrorCode.Unknown, ex.Message);
        }
    }

    private static async Task WriteSuccessAsync(HttpContext context, int msgId, object? payload)
    {
        var res = new JsonRes<object>
        {
            MsgId = msgId,
            Code = ErrorCode.Success,
            Payload = payload
        };
        
        context.Response.ContentType = "application/json";
        await using var writer = new StreamWriter(context.Response.Body);
        _serializer.Serialize(writer, res);
    }

    private static async Task WriteErrorAsync(HttpContext context, int msgId, ErrorCode code, string message)
    {
        var res = new JsonRes<object>
        {
            MsgId = msgId,
            Code = code,
            Message = message
        };
        
        context.Response.ContentType = "application/json";
        await using var writer = new StreamWriter(context.Response.Body);
        _serializer.Serialize(writer, res);
    }
}
