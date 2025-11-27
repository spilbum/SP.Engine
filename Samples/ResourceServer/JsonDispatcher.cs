using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ResourceServer.Handlers;
using SP.Shared.Resource;
using JsonException = System.Text.Json.JsonException;
using JsonResult = SP.Shared.Resource.JsonResult;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace ResourceServer;

public sealed class JsonDispatcher
{
    private readonly Dictionary<int, IJsonHandler> _map;
    
    private static readonly JsonSerializerSettings Settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore
    };

    public JsonDispatcher(IEnumerable<IJsonHandler> handlers)
        => _map = handlers.ToDictionary(h => h.ReqId, h => h);

    private static bool TryReadMsgId(ReadOnlySpan<byte> utf8, out int msgId)
    {
        msgId = 0;

        try
        {
            var json = Encoding.UTF8.GetString(utf8);

            var jo = JObject.Parse(json);
            var token = jo["MsgId"];
            if (token is not { Type: JTokenType.Integer })
                return false;

            msgId = token.Value<int>();
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    public async Task<object> DispatchAsync(ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        if (!TryReadMsgId(body.Span, out var msgId))
            return JsonResult.Error(0, ErrorCode.MissingField, "Missing msgId");
        
        if (!_map.TryGetValue(msgId, out var handler))
            return JsonResult.Error(0, ErrorCode.UnknownMsgId, $"Unknown msgId: {msgId}");

        object? reqObj;
        try
        {
            var json = Encoding.UTF8.GetString(body.Span);
            reqObj = JsonConvert.DeserializeObject(json, handler.ReqType, Settings);
            
            if (reqObj is null)
                return JsonResult.Error(handler.ResId, ErrorCode.InvalidFormat,
                    $"Failed to deserialize request: {handler.ReqType.Name}");
        }
        catch (Exception e)
        {
            return JsonResult.Error(handler.ResId, ErrorCode.InvalidFormat, e.Message);
        }

        try
        {
            return await handler.HandleAsync(reqObj, ct);
        }
        catch (OperationCanceledException)
        {
            return JsonResult.Error(handler.ResId, ErrorCode.InternalError, "Canceled");
        }
        catch (Exception e)
        {
            return JsonResult.Error(handler.ResId, ErrorCode.InternalError, e.Message);
        }
    }
}
