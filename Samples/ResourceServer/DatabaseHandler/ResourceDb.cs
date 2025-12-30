using System.Data;
using SP.Core.Accessor;
using SP.Shared.Database;
using SP.Shared.Resource;

namespace ResourceServer.DatabaseHandler;

public static class ResourceDb
{
    public class ResourceConfigEntity : BaseDbEntity
    {
        [Member("config_key")] public string Key = "";
        [Member("config_value")] public string Value = "";
    }
    
    public class ResourcePatchVersionEntity : BaseDbEntity
    {
        [Member("server_group_type")] public string ServerGroupType = "";
        [Member("resource_version")] public int ResourceVersion;
        [Member("target_major")] public int TargetMajor;
        [Member("file_id")] public int FileId;
        [Member("comment")] public string? Comment;
        [Member("created_utc")] public DateTime CreatedUtc;
    }

    public class ClientBuildVersionEntity : BaseDbEntity
    {
        [Member("server_group_type")] public string ServerGroupType = "";
        [Member("store_type")] public string StoreType = "";
        [Member("begin_build_version")] public string BeginBuildVersion = "";
        [Member("end_build_version")] public string EndBuildVersion = "";
    } 
    
    public class MaintenanceEnvEntity : BaseDbEntity
    {
        [Member("server_group_type")] public string ServerGroupType = "";
        [Member("is_enabled")] public bool IsEnabled;
        [Member("start_utc")] public DateTime StartUtc;
        [Member("end_utc")] public DateTime EndUtc;
        [Member("message_id")] public string MessageId = "";
        [Member("comment")] public string? Comment;
        [Member("updated_by")] public string UpdatedBy = " ";
    }

    public class MaintenanceBypassEntity : BaseDbEntity
    {
        [Member("id", IgnoreSet = true)] public int Id;
        [Member("server_group_type")] public string ServerGroupType = "";
        [Member("kind")] public string Kind = "";
        [Member("value")] public string Value = "";
        [Member("comment")] public string? Comment;
    }

    public static async Task<MaintenanceEnvEntity?> GetMaintenanceEnvAsync(
        DbConn conn, 
        ServerGroupType serverGroupType,
        CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_get_maintenance_env");
        cmd.Add("server_group_type", DbType.String, serverGroupType, 16);
        return await cmd.ExecuteReaderAsync<MaintenanceEnvEntity>(ct);
    }

    public static async Task<List<MaintenanceBypassEntity>> GetMaintenanceBypassAsync(
        DbConn conn, 
        ServerGroupType serverGroupType, 
        CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_get_maintenance_bypasses");
        cmd.Add("server_group_type", DbType.String, serverGroupType, 16);
        return await cmd.ExecuteReaderListAsync<MaintenanceBypassEntity>(ct);
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
