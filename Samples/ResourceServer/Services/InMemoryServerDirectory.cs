using System.Collections.Immutable;
using SP.Shared.Resource;

namespace ResourceServer.Services;

public sealed class ServerSnapshot(ImmutableArray<ServerConnectionInfo> list, DateTimeOffset updatedUtc)
{
    public ImmutableArray<ServerConnectionInfo> List { get; } = list;
    public DateTimeOffset UpdatedUtc { get; } = updatedUtc;
}

public class InMemoryServerDirectory : IServerDirectory
{
    private ServerSnapshot _snapshot = new([], DateTimeOffset.MinValue);

    public ServerSnapshot GetSnapshot() 
        => Volatile.Read(ref _snapshot);

    public void ReplaceAll(IEnumerable<ServerSyncInfo> infos, DateTimeOffset updatedUtc)
    {
        var newUtc = updatedUtc.ToUniversalTime();
        var current = Volatile.Read(ref _snapshot);
        
        if (newUtc <= current.UpdatedUtc)
            return;

        var builder = ImmutableArray.CreateBuilder<ServerConnectionInfo>();
        foreach (var info in infos)
        {
            builder.Add(new ServerConnectionInfo(info.Id, info.Kind, info.Region, info.Host, info.Port, info.Status));
        }

        var newSnapshot = new ServerSnapshot(builder.ToImmutableArray(), newUtc);
        Interlocked.Exchange(ref _snapshot, newSnapshot);
    }
}
