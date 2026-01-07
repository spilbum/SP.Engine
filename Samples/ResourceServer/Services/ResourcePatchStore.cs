using System.Collections.Immutable;
using ResourceServer.DatabaseHandler;
using SP.Shared.Resource;

namespace ResourceServer.Services;


public sealed class ResourcePatchVersion
{
    public ServerGroupType ServerGroupType { get; }
    public int TargetMajor { get; }
    public int PatchVersion { get; }
    public int FileId { get; }
    public DateTime CreatedUtc { get; }

    public ResourcePatchVersion(ResourceDb.RefsPatchVersionEntity entity)
    {
        if (!Enum.TryParse(entity.ServerGroupType, out ServerGroupType serverGroupType))
            throw new ArgumentException("Invalid server group type");
        
        ServerGroupType = serverGroupType;
        TargetMajor = entity.TargetMajor;
        PatchVersion = entity.PatchVersion;
        FileId = entity.FileId;
        CreatedUtc = entity.CreatedUtc;
    }

    public bool IsAllowed(BuildVersion v)
        => v.Major == TargetMajor;
}

public sealed class ResourcePatchSet
{
    public ServerGroupType Group { get; }
    public int TargetMajor { get; }
    
    public ImmutableArray<ResourcePatchVersion> Patches { get; }
    
    public ResourcePatchVersion? Latest => 
        Patches.IsDefaultOrEmpty ? null : Patches[^1];

    public ResourcePatchSet(
        ServerGroupType group,
        int major,
        IEnumerable<ResourcePatchVersion> patches)
    {
        Group = group;
        TargetMajor = major;
        Patches = [..patches.OrderBy(p => p.PatchVersion)];
    }

    public ResourcePatchVersion? GetLatestFor(BuildVersion buildVersion)
        => buildVersion.Major != TargetMajor ? null : Latest;
}

public interface IResourcePatchStore
{
    ResourcePatchVersion? GetLatest(ServerGroupType groupType, int clientMajor);
    Task ReloadAsync(CancellationToken ct = default);
}

public sealed class ResourcePatchStore(IDbConnector dbConnector) : IResourcePatchStore
{
    private ImmutableDictionary<(ServerGroupType Group, int Major), ResourcePatchSet> _patchSets 
        = ImmutableDictionary<(ServerGroupType, int), ResourcePatchSet>.Empty;

    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    public ResourcePatchVersion? GetLatest(ServerGroupType serverGroupType, int clientMajor)
    {
        var snapshot = Volatile.Read(ref _patchSets);
        return snapshot.TryGetValue((serverGroupType, clientMajor), out var set)
            ? set.Latest
            : null;
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _reloadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = await dbConnector.OpenAsync(ct).ConfigureAwait(false);
            var list = await ResourceDb.GetRefsPatchVersions(conn, ct)
                .ConfigureAwait(false);

            var versions = new List<ResourcePatchVersion>(list.Count);
            versions.AddRange(list.Select(e => new ResourcePatchVersion(e)));

            ImmutableDictionary<(ServerGroupType Group, int Major), ResourcePatchSet>.Builder dictBuilder =
                ImmutableDictionary.CreateBuilder<(ServerGroupType, int), ResourcePatchSet>();
            
            foreach (var group in versions.GroupBy(p => (p.ServerGroupType, p.TargetMajor)))
            {
                var set = new ResourcePatchSet(
                    group.Key.ServerGroupType,
                    group.Key.TargetMajor,
                    group);
                dictBuilder[group.Key] = set;
            }

            _patchSets = dictBuilder.ToImmutable();
        }
        finally
        {
            _reloadLock.Release();
        }
    }
}
