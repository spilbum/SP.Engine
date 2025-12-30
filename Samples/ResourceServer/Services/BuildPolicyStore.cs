using System.Collections.Immutable;
using ResourceServer.DatabaseHandler;
using SP.Shared.Resource;

namespace ResourceServer.Services;


public interface IBuildPolicyStore
{
    BuildPolicy? Get(StoreType storeType, BuildVersion buildVersion, ServerGroupType? forceServerGroupType = null);
    Task ReloadAsync(CancellationToken ct = default);
}

public class BuildPolicyStore(IDbConnector dbConnector) : IBuildPolicyStore
{
    private ImmutableArray<BuildPolicy> _policies = ImmutableArray<BuildPolicy>.Empty;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    public BuildPolicy? Get(StoreType storeType, BuildVersion buildVersion, ServerGroupType? forceServerGroupType = null)
    {
        var snapshot = _policies;
        
        if (forceServerGroupType is ServerGroupType.Live)
            return null;

        return snapshot
            .Where(p => p.StoreType == storeType)
            .Where(p => !forceServerGroupType.HasValue || p.ServerGroupType == forceServerGroupType.Value)
            .FirstOrDefault(p => p.Supports(buildVersion));
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _reloadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = await dbConnector.OpenAsync(ct).ConfigureAwait(false);
            var entities = await ResourceDb.GetClientBuildVersions(conn, ct).ConfigureAwait(false);
            
            var list = new List<BuildPolicy>(entities.Count);
            foreach (var entity in entities)
            {
                try
                {
                    list.Add(new BuildPolicy(entity));
                }
                catch { /* ignore */}
            }
            
            _policies = [..list];
        }
        finally
        {
            _reloadLock.Release();
        }
    }
}

public sealed class BuildPolicy
{
    public StoreType StoreType { get; }
    public ServerGroupType ServerGroupType { get; }
    public BuildVersion BeginBuildVersion { get; }
    public BuildVersion EndBuildVersion { get; }

    public BuildPolicy(ResourceDb.ClientBuildVersionEntity entity)
    {
        if (!Enum.TryParse(entity.ServerGroupType, out ServerGroupType serverGroupType))
            throw new ArgumentException($"Invalid server group type: {entity.ServerGroupType}");
        
        if (!Enum.TryParse(entity.StoreType, out StoreType storeType))
            throw new ArgumentException($"Invalid store type: {entity.StoreType}");
        
        if (!BuildVersion.TryParse(entity.BeginBuildVersion, out var minVersion) ||
            !BuildVersion.TryParse(entity.EndBuildVersion, out var maxVersion) ||
            minVersion.CompareTo(maxVersion) > 0)
        {
            throw new ArgumentException("Invalid build version");
        }
        
        ServerGroupType = serverGroupType;
        StoreType = storeType;
        BeginBuildVersion = minVersion;
        EndBuildVersion = maxVersion;
    }
    
    public bool Supports(BuildVersion v)
        => v.CompareTo(BeginBuildVersion) >= 0 && v.CompareTo(EndBuildVersion) <= 0;
    
    public bool IsSoftUpdate(BuildVersion v)
        => v.CompareTo(EndBuildVersion) < 0;
    
    public BuildVersion LatestBuildVersion => EndBuildVersion;
}
