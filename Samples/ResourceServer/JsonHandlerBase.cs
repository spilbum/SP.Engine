using SP.Shared.Resource;
using SP.Shared.Resource.Web;

namespace ResourceServer;

public abstract class JsonHandlerBase<TReq, TRes>(IHttpContextAccessor http) : IJsonHandler
{
    protected HttpContext? HttpContext => http.HttpContext;

    protected string? ClientIp
    {
        get
        {
            var ctx = http.HttpContext;
            if (ctx == null) return null;

            if (ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var xff))
            {
                var first = xff.ToString().Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(first))
                    return first;
            }
            
            return ctx.Connection.RemoteIpAddress?.ToString();
        }
    }
    
    public abstract int ReqId { get; }
    public abstract int ResId { get; }
    public Type ReqType => typeof(JsonCmd<TReq>);
    protected abstract ValueTask<TRes> HandlePayloadAsync(TReq req, CancellationToken ct);

    async ValueTask<JsonResBase> IJsonHandler.HandleAsync(object req, CancellationToken ct)
    {
        if (req is not JsonCmd<TReq> cmd || cmd.Payload is null)
            return JsonResult.Error<object>(ResId, ErrorCode.InvalidFormat, "payload is null");

        try
        {
            var payload = await HandlePayloadAsync(cmd.Payload, ct).ConfigureAwait(false);
            return JsonResult.Ok(ResId, payload);
        }
        catch (OperationCanceledException)
        {
            return JsonResult.Error<object>(ResId, ErrorCode.InternalError, "Canceled");
        }
        catch (ErrorCodeException ex)
        {
            return JsonResult.Error<object>(ResId, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return JsonResult.Error<object>(ResId, ErrorCode.InternalError, ex.Message);
        }
    }
}
