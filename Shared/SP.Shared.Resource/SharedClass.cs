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

public static class PatchConst
{
    public const string SchsFile = "schs";
    public const string SchFile = "sch";
    public const string RefsFile = "refs";
    public const string RefFile = "ref";
    public const string LocsFile = "locs";
    public const string LocFile = "loc";
}

public static class PatchUtil
{
    private const string BaseRefsFolder = "patch/refs";
    private const string BaseLocalizationFolder = "patch/localization";

    public static string BuildRefsUploadKey(int fileId, string target, string extension)
        => $"{BaseRefsFolder}/{fileId:D6}/{target}/{fileId}.{extension}";
    
    public static string BuildRefsDownloadUrl(string baseUrl, string target, int fileId, string extension)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("baseUrl is empty.", nameof(baseUrl));
        
        baseUrl = baseUrl.TrimEnd('/');
        return $"{baseUrl}/{BaseRefsFolder}/{fileId:D6}/{target}/{fileId}.{extension}";
    }
    
    public static string BuildLocalizationUploadKey(int fileId, string extension)
        => $"{BaseLocalizationFolder}/{fileId:D6}/{fileId}.{extension}";

    public static string BuildLocalizationDownloadUrl(string baseUrl, int fileId, string extension)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("baseUrl is empty.", nameof(baseUrl));
        
        baseUrl = baseUrl.TrimEnd('/');
        return $"{baseUrl}/{BaseLocalizationFolder}/{fileId:D6}/{fileId}.{extension}";
    }
}
