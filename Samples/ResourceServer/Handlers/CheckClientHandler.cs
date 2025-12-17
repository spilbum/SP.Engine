
using ResourceServer.Services;
using SP.Shared.Resource;
using SP.Shared.Resource.Web;

namespace ResourceServer.Handlers;

public sealed class CheckClientHandler(
    IBuildPolicyStore buildStore,
    IResourcePatchStore patchStore,
    IServerStore serverStore,
    IResourceConfigStore configStore) : JsonHandlerBase<CheckClientReq, CheckClientRes>
{
    public override int ReqId => ResourceMsgId.CheckClientReq;
    public override int ResId => ResourceMsgId.CheckClientRes;

    protected override ValueTask<CheckClientRes> HandlePayloadAsync(CheckClientReq req, CancellationToken ct)
    {
        var response = new CheckClientRes();

        if (!BuildVersion.TryParse(req.BuildVersion, out var buildVersion))
            throw new ErrorCodeException(ErrorCode.InvalidFormat, $"Invalid build version: {req.BuildVersion}");
        
        var policy = buildStore.GetPolicy(req.StoreType, buildVersion, req.ForceServerGroupType);
        if (policy == null)
        {
            var storeUrl = req.StoreType switch
            {
                StoreType.GooglePlay => configStore.Get("google_play_store_url"),
                StoreType.AppStore => configStore.Get("app_store_url"),
                _ => null
            };

            response.IsAllow = false;
            response.IsForceUpdate = true;
            response.StoreUrl = storeUrl;
            return ValueTask.FromResult(response);
        }

        response.IsAllow = true;
        response.IsForceUpdate = false;
        response.IsSoftUpdate = policy.IsSoftUpdate(buildVersion);
        response.LatestBuildVersion = policy.LatestBuildVersion.ToString();

        var latestPatch = patchStore.GetLatestPatch(policy.ServerGroupType, buildVersion.Major);
        if (latestPatch != null)
        {
            var latest = latestPatch.ResourceVersion;
            var current = req.ResourceVersion ?? 0;

            response.LatestResourceVersion = latest;

            if (current < latest)
            {
                var endpoint = configStore.Get("minio_endpoint");
                var bucket = configStore.Get("minio_bucket");
                if (!string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(bucket))
                {
                    response.DownloadSchsFileUrl = $"http://{endpoint}/{bucket}/patch/{latestPatch.FileId}.schs";
                    response.DownloadRefsFileUrl =  $"http://{endpoint}/{bucket}/patch/{latestPatch.FileId}.refs";
                }
            }
        }

        var server = FindAvailableServer(policy.ServerGroupType, buildVersion);
        if (server == null)
            throw new ErrorCodeException(ErrorCode.NoServerAvailable, "No server available");

        response.Server = server;
        return ValueTask.FromResult(response);
    }
    
    private ServerConnectInfo? FindAvailableServer(ServerGroupType serverGroupType, BuildVersion buildVersion)
    {
        var snapshot = serverStore.GetSnapshot(serverGroupType);
        if (snapshot == null || snapshot.Servers.IsDefaultOrEmpty)
            return null;

        var candidates = 
            from info in snapshot.Servers
            where info.Status == ServerStatus.Online
            where info.BuildVersion.Major == buildVersion.Major
            where info.MaxUserCount <= 0 || info.UserCount < info.MaxUserCount
            select info;

        var selected = candidates
            .OrderByDescending(s => s.BuildVersion)
            .ThenBy(s =>
            {
                if (s.MaxUserCount <= 0) return 0.0;
                return (double)s.UserCount / s.MaxUserCount;
            })
            .ThenBy(s => s.UserCount)
            .ThenBy(s => s.Id)
            .FirstOrDefault();

        return selected?.ToInfo();
    }
}


