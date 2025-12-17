using System.Collections.Immutable;
using ResourceServer.DatabaseHandler;
using SP.Shared.Resource;

namespace ResourceServer.Services;


public sealed class ResourcePatchPolicy
{
    public ServerGroupType ServerGroupType { get; }
    public int ClientMajorVersion { get; }
    public int ResourceVersion { get; }
    public int FileId { get; }
    public DateTime CreatedUtc { get; }

    public ResourcePatchPolicy(ResourceDb.ResourcePatchVersionEntity entity)
    {
        if (!Enum.TryParse(entity.ServerGroupType, out ServerGroupType serverGroupType))
            throw new ArgumentException("Invalid server group type");
        
        ServerGroupType = serverGroupType;
        ClientMajorVersion = entity.ClientMajorVersion;
        ResourceVersion = entity.ResourceVersion;
        FileId = entity.FileId;
        CreatedUtc = entity.CreatedUtc;
    }

    public bool IsAllowed(BuildVersion v)
        => v.Major == ClientMajorVersion;
}

public sealed class ResourcePatchSet
{
    public ServerGroupType ServerGroupType { get; }
    public int ClientMajorVersion { get; }
    
    public ImmutableArray<ResourcePatchPolicy> Patches { get; }
    
    public ResourcePatchPolicy? Latest => 
        Patches.IsDefaultOrEmpty ? null : Patches[^1];

    public ResourcePatchSet(
        ServerGroupType serverGroupType,
        int majorVersion,
        IEnumerable<ResourcePatchPolicy> patches)
    {
        ServerGroupType = serverGroupType;
        ClientMajorVersion = majorVersion;
        Patches = [..patches.OrderBy(p => p.ResourceVersion)];
    }

    public ResourcePatchPolicy? GetLatestFor(BuildVersion buildVersion)
        => buildVersion.Major != ClientMajorVersion ? null : Latest;
}

public interface IResourcePatchStore
{
    ResourcePatchPolicy? GetLatestPatch(ServerGroupType groupType, int clientMajorVersion);
    Task ReloadAsync(CancellationToken ct = default);
}

public sealed class ResourcePatchStore(IDbConnector dbConnector) : IResourcePatchStore
{
    private ImmutableDictionary<(ServerGroupType Group, int Major), ResourcePatchSet> _patchSets 
        = ImmutableDictionary<(ServerGroupType, int), ResourcePatchSet>.Empty;

    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    public ResourcePatchPolicy? GetLatestPatch(ServerGroupType serverGroupType, int clientMajorVersion)
    {
        var snapshot = Volatile.Read(ref _patchSets);
        return snapshot.TryGetValue((serverGroupType, clientMajorVersion), out var set)
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
            
            var dictBuilder =
                ImmutableDictionary.CreateBuilder<(ServerGroupType, int), ResourcePatchSet>();
            
            foreach (var group in policies.GroupBy(p => (p.ServerGroupType, p.ClientMajorVersion)))
            {
                var set = new ResourcePatchSet(
                    group.Key.ServerGroupType,
                    group.Key.ClientMajorVersion,
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
