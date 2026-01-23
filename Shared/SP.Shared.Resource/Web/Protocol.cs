using System;
using System.Collections.Generic;

namespace SP.Shared.Resource.Web;

public sealed class CheckClientReq
{
    public StoreType StoreType { get; set; }
    public string BuildVersion { get; set; } = string.Empty;
    public int? ResourceVersion { get; set; }
    public ServerGroupType? ForceServerGroupType { get; set; }
    public string? DeviceId { get; set; }
}

public sealed class CheckClientRes
{
    public bool IsAllow { get; set; }
    public bool IsForceUpdate { get; set; }
    public bool IsSoftUpdate { get; set; }
    public string LatestBuildVersion { get; set; } = string.Empty;
    public int LatestPatchVersion { get; set; }
    public string? DownloadSchsFileUrl { get; set; }
    public string? DownloadRefsFileUrl { get; set; }
    public string? StoreUrl { get; set; }
    public string? DownloadLocsFileUrl { get; set; }
    public ServerConnectInfo? Server { get; set; }
    
    public bool IsMaintenance { get; set; }
    public string? MaintenanceMessageId { get; set; }
    public DateTime? MaintenanceStartUtc { get; set; }
    public DateTime? MaintenanceEndUtc { get; set; }
}

public sealed class SyncServerListReq
{
    public long UpdatedUtcMs { get; set; }
    public ServerGroupType ServerGroupType { get; set; }
    public List<ServerSyncInfo> List { get; set; } = [];
}

public sealed class SyncServerListRes
{
    public long AppliedUtcMs { get; set; }
}

public sealed class RefreshResourceServerReq
{
}

public sealed class RefreshResourceServerRes
{
}

