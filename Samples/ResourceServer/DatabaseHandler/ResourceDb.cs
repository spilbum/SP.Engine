using System.Data;
using SP.Core.Accessor;
using SP.Shared.Database;

namespace ResourceServer.DatabaseHandler;

public static class ResourceDb
{
    public class PatchPolicyEntity : BaseDbEntity
    {
        [Member("platform")] public byte Platform;
        [Member("min_build_version")] public string? MinBuildVersion;
        [Member("latest_build_version")] public string? LatestBuildVersion;
        [Member("latest_resource_version")] public int LatestResourceVersion;
        [Member("patch_base_url")] public string? PatchBaseUrl;
        [Member("store_url")] public string? StoreUrl;
    }

    public static async Task<List<PatchPolicyEntity>> LoadAllPatchPolicies(DbConn conn, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_PatchPolicy_LoadAll");
        return await cmd.ExecuteReaderListAsync<PatchPolicyEntity>(ct);
    }
}
