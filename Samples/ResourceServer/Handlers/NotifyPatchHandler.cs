using ResourceServer.Services;
using SP.Shared.Resource;

namespace ResourceServer.Handlers;

public sealed class NotifyPatchHandler(IPatchPolicyLoader loader) : IJsonHandler
{
    public int ReqId => MsgId.NotifyPatchReq;
    public int ResId => MsgId.NotifyPatchRes;
    public Type ReqType => typeof(JsonCmd<NotifyPatchReq>);

    public async ValueTask<object> HandleAsync(object req, CancellationToken ct)
    {
        var _ = (JsonCmd<NotifyPatchReq>)req;                                                             
        var changed = await loader.ReloadAsync(ct);
        return JsonResult.Ok(ResId, new NotifyPatchRes { IsChanged = changed });
    }
}
