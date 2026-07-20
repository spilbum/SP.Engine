using System.Threading.Tasks;
using SP.Resource;
using SP.Resource.Web;

namespace SP.ResourceServer;

public interface IRpcHandler<TReq, TRes> where TReq : IRpcRequest<TRes>
{
    Task<TRes> HandleAsync(TReq? request);
}

public sealed class CheckClientHandler(ServerRegistry registry) : IRpcHandler<CheckClientReq, CheckClientRes>
{
    // Mock Configs
    private const int MinAllowedMajor = 1;
    private const int MockLatestPatchVersion = 5;
    private const string MockLatestBuildVersion = "1.0.0";
    private const string MockCdnUrl = "https://cdn.example.com";

    public Task<CheckClientRes> HandleAsync(CheckClientReq? req)
    {
        var res = new CheckClientRes
        {
            IsAllow = true,
            LatestBuildVersion = MockLatestBuildVersion,
            LatestPatchVersion = MockLatestPatchVersion
        };

        if (req == null) return Task.FromResult(res);

        if (!BuildVersion.TryParse(req.BuildVersion, out var clientVersion))
        {
            res.IsAllow = false;
            return Task.FromResult(res);
        }

        // 1. Force Update Check (Major mismatch)
        if (clientVersion.Major < MinAllowedMajor)
        {
            res.IsAllow = false;
            res.IsForceUpdate = true;
            res.StoreUrl = req.StoreType == StoreType.AppStore 
                ? "https://apps.apple.com/app/id12345" 
                : "https://play.google.com/store/apps/details?id=com.example";
            return Task.FromResult(res);
        }

        // 2. Soft Update Check (Resource Patch)
        if (req.ResourceVersion is null or < MockLatestPatchVersion)
        {
            res.IsSoftUpdate = true;
            res.DownloadSchsFileUrl = PatchUtil.BuildLocalizationDownloadUrl(MockCdnUrl, MockLatestPatchVersion, PatchConst.SchsFile);
            res.DownloadRefsFileUrl = PatchUtil.BuildRefsDownloadUrl(MockCdnUrl, "all", MockLatestPatchVersion, PatchConst.RefsFile);
            res.DownloadLocsFileUrl = PatchUtil.BuildLocalizationDownloadUrl(MockCdnUrl, MockLatestPatchVersion, PatchConst.LocsFile);
        }

        // 3. Find Best Server (Major version match)
        var server = registry.GetBestServer(clientVersion, req.ForceServerGroupType ?? ServerGroupType.Live);
        if (server == null)
        {
            // 가용 서버가 없으면 점검 중이거나 장애 상태로 간주
            res.IsAllow = false;
            res.IsMaintenance = true;
        }
        else
        {
            res.Server = server;
        }

        return Task.FromResult(res);
    }
}

public sealed class SyncServerListHandler(ServerRegistry registry) : IRpcHandler<SyncServerListReq, SyncServerListRes>
{
    public Task<SyncServerListRes> HandleAsync(SyncServerListReq? req)
    {
        if (req != null)
        {
            registry.Upsert(req.List);
        }

        var res = new SyncServerListRes
        {
            AppliedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        return Task.FromResult(res);
    }
}

public sealed class RefreshResourceHandler : IRpcHandler<RefreshResourceServerReq, RefreshResourceServerRes>
{
    public Task<RefreshResourceServerRes> HandleAsync(RefreshResourceServerReq? req)
    {
        // Mock Implementation
        var res = new RefreshResourceServerRes();
        return Task.FromResult(res);
    }
}
