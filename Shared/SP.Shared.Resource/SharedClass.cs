using System;
using System.Collections.Generic;
using System.Linq;

namespace SP.Shared.Resource;


public static class MsgId
{
    public const int CheckClientReq = 101;
    public const int CheckClientRes = 102;
    
    public const int SyncServerListReq = 201;
    public const int SyncServerListRes = 202;

    public const int NotifyPatchReq = 301;
    public const int NotifyPatchRes = 302;
}

public sealed class SyncServerListReq
{
    public long UpdatedUtcMs { get; set; }
    public List<ServerSyncInfo> List { get; set; } = [];
}

public sealed class SyncServerListRes
{
    public long AppliedUtcMs { get; set; }
}

public sealed class CheckClientReq
{
    public PlatformKind Platform { get; set; }
    public string BuildVersion { get; set; } = string.Empty;
    public int? ResourceVersion { get; set; }
}

public sealed class CheckClientRes
{
    public bool IsAllow { get; set; }
    public bool IsForceUpdate { get; set; }
    public bool IsSoftUpdate { get; set; }
    public string LatestBuildVersion { get; set; } = string.Empty;
    public int LatestResourceVersion { get; set; }
    public string? ManifestUrl { get; set; }
    public string? StoreUrl { get; set; }
    public List<ServerConnectionInfo> Servers { get; set; } = [];
}

public sealed class NotifyPatchReq
{
}

public sealed class NotifyPatchRes
{
    public bool IsChanged { get; set; }
}

public sealed class ServerSyncInfo(
    string id,
    string kind,
    string region,
    string host,
    int port,
    ServerStatus status,
    Dictionary<string, string>? meta,
    DateTimeOffset updatedUtc)
{
    public string Id { get; set; } = id;
    public string Kind { get; set; } = kind;
    public string Region { get; set; } = region;
    public string Host { get; set; } = host;
    public int Port { get; set; } = port;
    public ServerStatus Status { get; set; } = status;
    public Dictionary<string,string>? Meta { get; set; } = meta;
    public DateTimeOffset UpdatedUtc { get; set; } = updatedUtc;
}

public sealed class ServerConnectionInfo(
    string id,
    string kind,
    string region,
    string host,
    int port,
    ServerStatus status)
{
    public string Id { get; set; } = id;
    public string Kind { get; set; } = kind;
    public string Region { get; set; } = region;
    public string Host { get; set; } = host;
    public int Port { get; set; } = port;
    public ServerStatus Status { get; set; } = status;
}
