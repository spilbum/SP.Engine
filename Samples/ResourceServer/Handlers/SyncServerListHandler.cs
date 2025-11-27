using ResourceServer.Services;
using SP.Shared.Resource;

namespace ResourceServer.Handlers;

public sealed class SyncServerListHandler(IServerDirectory dir, ILogger<SyncServerListHandler> logger) : IJsonHandler
{
    public int ReqId => MsgId.SyncServerListReq;
    public int ResId => MsgId.SyncServerListRes;
    public Type ReqType => typeof(JsonCmd<SyncServerListReq>);

    public ValueTask<object> HandleAsync(object req, CancellationToken ct)
    {
        var cmd = (JsonCmd<SyncServerListReq>)req;
        var payload = cmd.Payload;
        if (payload == null)
            return ValueTask.FromResult<object>(
                JsonResult.Error(ResId, ErrorCode.InvalidFormat, "Payload is null"));

        var updatedUtc = DateTimeOffset.FromUnixTimeMilliseconds(payload.UpdatedUtcMs);

        var snapshot = dir.GetSnapshot();
        if (updatedUtc <= snapshot.UpdatedUtc)
        {
            return ValueTask.FromResult<object>(
                JsonResult.Error(ResId, ErrorCode.OutdatedSnapshot, "Outdated sync snapshot"));
        }

        dir.ReplaceAll(payload.List, updatedUtc);
        logger.LogInformation("Server list replaced: {List}", 
            string.Join(", ", payload.List.Select(x => $"{x.Id} - {x.Kind} - {x.Host}:{x.Port}")));
        
        var res = new SyncServerListRes { AppliedUtcMs = updatedUtc.ToUnixTimeMilliseconds() };
        return ValueTask.FromResult<object>(JsonResult.Ok(ResId, res));
    }
}
