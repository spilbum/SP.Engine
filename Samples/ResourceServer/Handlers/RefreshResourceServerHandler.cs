using ResourceServer.Services;
using SP.Shared.Resource.Web;

namespace ResourceServer.Handlers;

public sealed class RefreshResourceServerHandler(
    IHttpContextAccessor http,
    IBuildPolicyStore build,
    IResourceConfigStore config,
    IResourcePatchStore resource,
    IMaintenanceStore maintenance,
    ILocalizationStore localization
    ) : JsonHandlerBase<RefreshResourceServerReq, RefreshResourceServerRes>(http)
{
    public override int ReqId => ResourceMsgId.RefreshResourceServerReq;
    public override int ResId => ResourceMsgId.RefreshResourceServerRes;

    protected override async ValueTask<RefreshResourceServerRes> HandlePayloadAsync(RefreshResourceServerReq req, CancellationToken ct)
    {
        await build.ReloadAsync(ct);
        await config.ReloadAsync(ct);
        await resource.ReloadAsync(ct);
        await maintenance.ReloadAsync(ct);
        await localization.ReloadAsync(ct);
        
        var res = new RefreshResourceServerRes();
        return res;
    }
}
