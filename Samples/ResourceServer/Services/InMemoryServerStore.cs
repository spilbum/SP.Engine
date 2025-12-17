using System.Collections.Immutable;
using SP.Shared.Resource;

namespace ResourceServer.Services;

public sealed class ServerInfo
{
    public string Id { get; }
    public string Kind { get; }
    public string Region { get; }
    public string Host { get; }
    public int Port { get; }
    public BuildVersion BuildVersion { get; }
    public ServerStatus Status { get; }
    public DateTimeOffset UpdatedUtc { get; }
    public Dictionary<string, string>? Meta { get; }
    
    public int UserCount { get; }
    public int MaxUserCount { get; }

    public ServerInfo(ServerSyncInfo info)
    {
        if (!BuildVersion.TryParse(info.BuildVersion, out var buildVersion))
            throw new ArgumentException($"Invalid build version: {info.BuildVersion}");
        
        Id = info.Id;
        Kind = info.Kind;
        Region = info.Region;
        Host = info.Host;
        Port = info.Port;
        Meta = info.Meta;
        BuildVersion = buildVersion;
        Status = info.Status;
        UpdatedUtc = info.UpdatedUtc;

        if (info.Meta != null &&
            info.Meta.TryGetValue("UserCount", out var curStr) &&
            int.TryParse(curStr, out var cur))
        {
            UserCount = cur;
        }
        
        if (info.Meta != null &&
            info.Meta.TryGetValue("MaxUserCount", out var maxStr) &&
            int.TryParse(maxStr, out var max))
        {
            MaxUserCount = max;
        }
    }

    public ServerConnectInfo ToInfo( )
        => new(Id, Kind, Region, Host, Port, Status);
}


public sealed class ServerGroupSnapshot(
    ServerGroupType serverGroupType, 
    ImmutableArray<ServerInfo> servers,
    DateTimeOffset updatedUtc)
{
    public ServerGroupType ServerGroupType { get; } = serverGroupType;
    public ImmutableArray<ServerInfo> Servers { get; } = servers;
    public DateTimeOffset UpdatedUtc { get; } = updatedUtc;
}

public interface IServerStore
{
    ServerGroupSnapshot? GetSnapshot(ServerGroupType serverGroupType);
    void ReplaceAll(ServerGroupType serverGroupType, IReadOnlyList<ServerSyncInfo> infos, DateTimeOffset updatedUtc);
}

public class InMemoryServerStore : IServerStore
{
    private ImmutableDictionary<ServerGroupType, ServerGroupSnapshot> _snapshots =
        ImmutableDictionary<ServerGroupType, ServerGroupSnapshot>.Empty;
    
    private readonly object _lock = new();

    public ServerGroupSnapshot? GetSnapshot(ServerGroupType groupType)
    {
        var snapshot = Volatile.Read(ref _snapshots);
        return CollectionExtensions.GetValueOrDefault(snapshot, groupType);
    }
    
    public void ReplaceAll(ServerGroupType serverGroupType, IReadOnlyList<ServerSyncInfo> infos, DateTimeOffset updatedUtc)
    {
        var newUtc = updatedUtc.ToUniversalTime();

        lock (_lock)
        {
            if (_snapshots.TryGetValue(serverGroupType, out var oldSnap) &&
                newUtc <= oldSnap.UpdatedUtc)
            {
                return;
            }

            var builder = ImmutableArray.CreateBuilder<ServerInfo>(infos.Count);
            foreach (var info in infos)
            {
                builder.Add(new ServerInfo(info));
            }

            var newSnap = new ServerGroupSnapshot(serverGroupType, builder.ToImmutableArray(), newUtc);
            _snapshots = _snapshots.SetItem(serverGroupType, newSnap);
        }
    }
}
