using ResourceServer.Services;
using SP.Shared.Resource.Web;

namespace ResourceServer.Handlers;

public sealed class RefreshResourceServerHandler(
    IBuildPolicyStore buildStore,
    IResourceConfigStore configStore,
    IResourcePatchStore patchStore
    ) : JsonHandlerBase<RefreshResourceServerReq, RefreshResourceServerRes>
{
    public override int ReqId => ResourceMsgId.RefreshResourceServerReq;
    public override int ResId => ResourceMsgId.RefreshResourceServerRes;

    protected override async ValueTask<RefreshResourceServerRes> HandlePayloadAsync(RefreshResourceServerReq req, CancellationToken ct)
    {
        await buildStore.ReloadAsync(ct);
        await configStore.ReloadAsync(ct);
        await patchStore.ReloadAsync(ct);
        
        var res = new RefreshResourceServerRes();
        return res;
    }
}
