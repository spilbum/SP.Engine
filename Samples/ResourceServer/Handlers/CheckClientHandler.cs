
using System.Collections.Immutable;
using ResourceServer.Services;
using SP.Shared.Resource;
using SP.Shared.Resource.Web;

namespace ResourceServer.Handlers;

public sealed class CheckClientHandler(
    IHttpContextAccessor http,
    IBuildPolicyStore build,
    IRefsPatchStore refs,
    IServerStore serverStore,
    IResourceConfigStore config,
    IMaintenanceStore maintenance,
    ILocalizationStore localization,
    ILogger<CheckClientHandler> logger) 
    : JsonHandlerBase<CheckClientReq, CheckClientRes>(http)
{
    public override int ReqId => ResourceMsgId.CheckClientReq;
    public override int ResId => ResourceMsgId.CheckClientRes;

    protected override ValueTask<CheckClientRes> HandlePayloadAsync(CheckClientReq req, CancellationToken ct)
    {
        var res = new CheckClientRes();
        
        if (!BuildVersion.TryParse(req.BuildVersion, out var buildVersion))
            throw new ErrorCodeException(ErrorCode.InvalidFormat, $"Invalid build version: {req.BuildVersion}");
        
        var policy = build.Get(req.StoreType, buildVersion, req.ForceServerGroupType);
        if (policy == null)
        {
            var storeUrl = req.StoreType switch
            {
                StoreType.GooglePlay => config.Get("google_play_store_url"),
                StoreType.AppStore => config.Get("app_store_url"),
                _ => null
            };

            res.IsAllow = false;
            res.IsForceUpdate = true;
            res.StoreUrl = storeUrl;
            return ValueTask.FromResult(res);
        }

        // 점검 체크
        var env = maintenance.GetEnv(policy.ServerGroupType);
        if (env is { IsEnabled: true })
        {
            var nowUtc = DateTime.UtcNow;
            if (nowUtc >= env.StartUtc && nowUtc <= env.EndUtc)
            {
                var clientIp = ClientIp;
                var deviceId = req.DeviceId;

                var bypasses = maintenance.GetBypasses(policy.ServerGroupType);
                if (!IsBypassed(bypasses, clientIp, deviceId))
                {
                    logger.LogWarning(
                        "Maintenance blocked client. ServerGroup={ServerGroup} Build={BuildVersion} Ip={ClientIp} DeviceId={DeviceId} Window={StartUtc}~{EndUtc} MessageId={MessageId}",
                        policy.ServerGroupType,
                        buildVersion.ToString(),
                        clientIp,
                        deviceId,
                        env.StartUtc,
                        env.EndUtc,
                        env.MessageId
                    );
                    
                    res.IsAllow = false;
                    res.IsMaintenance = true;
                    res.MaintenanceMessageId = env.MessageId;
                    res.MaintenanceStartUtc = env.StartUtc;
                    res.MaintenanceEndUtc = env.EndUtc;
                    return ValueTask.FromResult(res);
                }
            }
        }
        
        res.IsAllow = true;
        res.IsForceUpdate = false;
        res.IsSoftUpdate = policy.IsSoftUpdate(buildVersion);
        res.LatestBuildVersion = policy.LatestBuildVersion.ToString();
        
        var patchBaseUrl = config.Get("patch_base_url");
        if (string.IsNullOrWhiteSpace(patchBaseUrl))
        {
            throw new ErrorCodeException(ErrorCode.InternalError, "Not found config: patch_base_url");
        }

        var latestRefs = refs.GetLatest(policy.ServerGroupType, buildVersion.Major);
        if (latestRefs != null)
        {
            var latest = latestRefs.PatchVersion;
            var cur = req.ResourceVersion ?? 0;

            res.LatestPatchVersion = latest;

            if (cur < latest)
            {
                res.DownloadSchsFileUrl =
                    PatchUtil.BuildRefsDownloadUrl(patchBaseUrl, "client", latestRefs.FileId, PatchConst.SchsFile);
                
                res.DownloadRefsFileUrl =
                    PatchUtil.BuildRefsDownloadUrl(patchBaseUrl, "server", latestRefs.FileId, PatchConst.RefsFile);
            }
        }

        var loc = localization.GetActive(policy.ServerGroupType, policy.StoreType);
        if (loc != null)
        {
            res.DownloadLocsFileUrl =
                PatchUtil.BuildLocalizationDownloadUrl(patchBaseUrl, loc.FileId, PatchConst.LocsFile);
        }

        var server = FindAvailableServer(policy.ServerGroupType, buildVersion);
        if (server == null)
        {
            throw new ErrorCodeException(ErrorCode.NoServerAvailable, "No server available");
        }

        res.Server = server;
        return ValueTask.FromResult(res);
    }
    
    private static bool IsBypassed(
        ImmutableArray<MaintenanceBypass> bypasses,
        string? ip,
        string? deviceId)
    {
        foreach (var b in bypasses)
        {
            switch (b.Kind)
            {
                case MaintenanceBypassKind.IpCidr:
                    if (ip != null && IpCidrMatcher.IsMatch(ip, b.Value))
                        return true;
                    break;
                
                case MaintenanceBypassKind.DeviceId:
                    if (!string.IsNullOrWhiteSpace(deviceId) &&
                        string.Equals(deviceId.Trim(), b.Value, StringComparison.OrdinalIgnoreCase))
                        return true;
                    break;
            }
        }
        return false;
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


