using ResourceServer.DatabaseHandler;
using SP.Shared.Resource;

namespace ResourceServer.Services;

public sealed class PatchPolicyStore(MySqlDbConnector connector)
{
    private Dictionary<PlatformKind, PatchPolicyInfo> _map = new();
    
    public async Task<bool> ReloadAsync(CancellationToken ct)
    {
        var fresh = await LoadAllAsync(ct);
        var old = Interlocked.Exchange(ref _map, fresh);
        
        if (old.Count != fresh.Count) return true;
        foreach (var (pf, row) in fresh)
        {
            if (!old.TryGetValue(pf, out var prev) || !row.Equals(prev))
                return true;
        }
        return false;
    }

    public bool TryGet(PlatformKind platform, out PatchPolicyInfo info)
    {
        var snap = Volatile.Read(ref _map);
        return snap.TryGetValue(platform, out info!);
    }

    private async Task<Dictionary<PlatformKind, PatchPolicyInfo>> LoadAllAsync(CancellationToken ct)
    {
        using var conn = await connector.OpenAsync(ct);
        var list = await ResourceDb.LoadAllPatchPolicies(conn, ct);
        return list.ToDictionary(entity => (PlatformKind)entity.Platform, entity => new PatchPolicyInfo(entity));
    }
}
