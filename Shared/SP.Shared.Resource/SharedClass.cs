using System;
using System.Collections.Generic;

namespace SP.Shared.Resource;

public sealed class ServerSyncInfo(
    string id,
    string kind,
    string region,
    string host,
    int port,
    string buildVersion,
    ServerStatus status,
    Dictionary<string, string>? meta,
    DateTimeOffset updatedUtc)
{
    public string Id { get; set; } = id;
    public string Kind { get; set; } = kind;
    public string Region { get; set; } = region;
    public string Host { get; set; } = host;
    public int Port { get; set; } = port;
    public string BuildVersion { get; set; } = buildVersion;
    public ServerStatus Status { get; set; } = status;
    public Dictionary<string,string>? Meta { get; set; } = meta;
    public DateTimeOffset UpdatedUtc { get; set; } = updatedUtc;
}

public sealed class ServerConnectInfo(
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


