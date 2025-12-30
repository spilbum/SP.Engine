using System.Collections.Immutable;
using ResourceServer.DatabaseHandler;
using SP.Shared.Resource;

namespace ResourceServer.Services;


public sealed class ResourcePatchPolicy
{
    public ServerGroupType ServerGroupType { get; }
    public int TargetMajor { get; }
    public int ResourceVersion { get; }
    public int FileId { get; }
    public DateTime CreatedUtc { get; }

    public ResourcePatchPolicy(ResourceDb.ResourcePatchVersionEntity entity)
    {
        if (!Enum.TryParse(entity.ServerGroupType, out ServerGroupType serverGroupType))
            throw new ArgumentException("Invalid server group type");
        
        ServerGroupType = serverGroupType;
        TargetMajor = entity.TargetMajor;
        ResourceVersion = entity.ResourceVersion;
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
    
    public ImmutableArray<ResourcePatchPolicy> Patches { get; }
    
    public ResourcePatchPolicy? Latest => 
        Patches.IsDefaultOrEmpty ? null : Patches[^1];

    public ResourcePatchSet(
        ServerGroupType group,
        int major,
        IEnumerable<ResourcePatchPolicy> patches)
    {
        Group = group;
        TargetMajor = major;
        Patches = [..patches.OrderBy(p => p.ResourceVersion)];
    }

    public ResourcePatchPolicy? GetLatestFor(BuildVersion buildVersion)
        => buildVersion.Major != TargetMajor ? null : Latest;
}

public interface IResourcePatchStore
{
    ResourcePatchPolicy? GetLatest(ServerGroupType groupType, int clientMajor);
    Task ReloadAsync(CancellationToken ct = default);
}

public sealed class ResourcePatchStore(IDbConnector dbConnector) : IResourcePatchStore
{
    private ImmutableDictionary<(ServerGroupType Group, int Major), ResourcePatchSet> _patchSets 
        = ImmutableDictionary<(ServerGroupType, int), ResourcePatchSet>.Empty;

    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    public ResourcePatchPolicy? GetLatest(ServerGroupType serverGroupType, int clientMajor)
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
            var entities = await ResourceDb.GetResourcePatchVersions(conn, ct)
                .ConfigureAwait(false);

            var policies = new List<ResourcePatchPolicy>(entities.Count);
            foreach (var e in entities)
            {
                try
                {
                    policies.Add(new ResourcePatchPolicy(e));
                }
                catch { /* ignore */ }
            }
            
            ImmutableDictionary<(ServerGroupType Group, int Major), ResourcePatchSet>.Builder dictBuilder =
                ImmutableDictionary.CreateBuilder<(ServerGroupType, int), ResourcePatchSet>();
            
            foreach (var group in policies.GroupBy(p => (p.ServerGroupType, p.TargetMajor)))
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
