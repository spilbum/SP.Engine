using ResourceServer.Services;
using SP.Shared.Resource.Web;

namespace ResourceServer.Handlers;

public sealed class SyncServerListHandler(
    IServerStore store, 
    ILogger<SyncServerListHandler> logger) : JsonHandlerBase<SyncServerListReq, SyncServerListRes>
{
    public override int ReqId => ResourceMsgId.SyncServerListReq;
    public override int ResId => ResourceMsgId.SyncServerListRes;

    protected override ValueTask<SyncServerListRes> HandlePayloadAsync(SyncServerListReq req, CancellationToken ct)
    {
        var res = new SyncServerListRes();
        var updatedUtc = DateTimeOffset.FromUnixTimeMilliseconds(req.UpdatedUtcMs);

        var snapshot = store.GetSnapshot(req.ServerGroupType);
        if (snapshot != null && updatedUtc <= snapshot.UpdatedUtc)
            return ValueTask.FromResult(res);

        store.ReplaceAll(req.ServerGroupType, req.List, updatedUtc);
        logger.LogInformation("Server list replaced: {List}, ServerGroupType={ServerGroupType}", 
            string.Join(", ", req.List.Select(x => $"{x.Id} - {x.Kind} - {x.Host}:{x.Port}")),
            req.ServerGroupType);
        
        res.AppliedUtcMs = updatedUtc.ToUnixTimeMilliseconds();
        return ValueTask.FromResult(res);
    }
}
