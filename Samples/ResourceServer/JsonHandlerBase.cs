using SP.Shared.Resource;
using SP.Shared.Resource.Web;

namespace ResourceServer;

public abstract class JsonHandlerBase<TReq, TRes> : IJsonHandler
{
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
