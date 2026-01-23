using System.Collections.Immutable;
using ResourceServer.DatabaseHandler;
using SP.Shared.Resource;

namespace ResourceServer.Services;


public sealed class RefsPatchVersion
{
    public ServerGroupType ServerGroupType { get; }
    public int TargetMajor { get; }
    public int PatchVersion { get; }
    public int FileId { get; }
    public DateTime CreatedUtc { get; }

    public RefsPatchVersion(ResourceDb.RefsPatchVersionEntity e)
    {
        if (!Enum.TryParse(e.ServerGroupType, out ServerGroupType serverGroupType))
            throw new ArgumentException($"Invalid ServerGroupType: {e.ServerGroupType}");
        
        ServerGroupType = serverGroupType;
        TargetMajor = e.TargetMajor;
        PatchVersion = e.PatchVersion;
        FileId = e.FileId;
        CreatedUtc = e.CreatedUtc;
    }
}

public sealed class ResourcePatchSet
{
    public ServerGroupType Group { get; }
    public int TargetMajor { get; }
    
    public ImmutableArray<RefsPatchVersion> Patches { get; }
    
    public RefsPatchVersion? Latest => 
        Patches.IsDefaultOrEmpty ? null : Patches[^1];

    public ResourcePatchSet(
        ServerGroupType group,
        int major,
        IEnumerable<RefsPatchVersion> patches)
    {
        Group = group;
        TargetMajor = major;
        Patches = [..patches.OrderBy(p => p.PatchVersion)];
    }
}

public interface IRefsPatchStore
{
    RefsPatchVersion? GetLatest(ServerGroupType groupType, int clientMajor);
    Task ReloadAsync(CancellationToken ct = default);
}

public sealed class RefsPatchStore(IDbConnector dbConnector) : IRefsPatchStore
{
    private ImmutableDictionary<(ServerGroupType Group, int Major), ResourcePatchSet> _map 
        = ImmutableDictionary<(ServerGroupType, int), ResourcePatchSet>.Empty;

    private readonly SemaphoreSlim _lock = new(1, 1);

    public RefsPatchVersion? GetLatest(ServerGroupType serverGroupType, int clientMajor)
    {
        var snap = Volatile.Read(ref _map);
        return snap.TryGetValue((serverGroupType, clientMajor), out var set)
            ? set.Latest
            : null;
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = await dbConnector.OpenAsync(ct).ConfigureAwait(false);
            var list = await ResourceDb.GetRefsPatchVersions(conn, ct).ConfigureAwait(false);

            var versions = new List<RefsPatchVersion>(list.Count);
            versions.AddRange(list.Select(e => new RefsPatchVersion(e)));

            var builder = ImmutableDictionary.CreateBuilder<(ServerGroupType, int), ResourcePatchSet>();
            foreach (var group in versions.GroupBy(p => (p.ServerGroupType, p.TargetMajor)))
            {
                var set = new ResourcePatchSet(
                    group.Key.ServerGroupType,
                    group.Key.TargetMajor,
                    group);
                builder[group.Key] = set;
            }

            _map = builder.ToImmutable();
        }
        finally
        {
            _lock.Release();
        }
    }
}
