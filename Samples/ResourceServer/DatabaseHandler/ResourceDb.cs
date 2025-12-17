using System.Data;
using SP.Core.Accessor;
using SP.Shared.Database;
using SP.Shared.Resource;

namespace ResourceServer.DatabaseHandler;

public static class ResourceDb
{
    public class ResourceConfigEntity : BaseDbEntity
    {
        [Member("config_key")] public string Key = string.Empty;
        [Member("config_value")] public string Value = string.Empty;
    }
    
    public class ResourcePatchVersionEntity : BaseDbEntity
    {
        [Member("server_group_type")] public string ServerGroupType = string.Empty;
        [Member("resource_version")] public int ResourceVersion;
        [Member("client_major_version")] public int ClientMajorVersion;
        [Member("file_id")] public int FileId;
        [Member("comment")] public string? Comment;
        [Member("created_utc")] public DateTime CreatedUtc;
    }

    public class ClientBuildVersionEntity : BaseDbEntity
    {
        [Member("server_group_type")] public byte ServerGroupType;
        [Member("store_type")] public byte StoreType;
        [Member("begin_build_version")] public string BeginBuildVersion = string.Empty;
        [Member("end_build_version")] public string EndBuildVersion = string.Empty;
    } 

    public static async Task<List<ResourceConfigEntity>> GetResourceConfigs(DbConn conn, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_get_resource_configs");
        return await cmd.ExecuteReaderListAsync<ResourceConfigEntity>(ct);
    }
    
    public static async Task<List<ClientBuildVersionEntity>> GetClientBuildVersions(DbConn conn, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_get_client_build_versions");
        return await cmd.ExecuteReaderListAsync<ClientBuildVersionEntity>(ct);
    }
    
    public static async Task<List<ResourcePatchVersionEntity>> GetResourcePatchVersions(
        DbConn conn, 
        CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_get_resource_patch_versions");
        return await cmd.ExecuteReaderListAsync<ResourcePatchVersionEntity>(ct);
    }
}
