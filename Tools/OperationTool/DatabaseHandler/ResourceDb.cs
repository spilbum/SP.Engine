using System.Data;
using DocumentFormat.OpenXml.Office2010.ExcelAc;
using SP.Core.Accessor;
using SP.Shared.Database;
using SP.Shared.Resource;

namespace OperationTool.DatabaseHandler;

public static class ResourceDb
{
    public class ResourceRefsFileEntity : BaseDbEntity
    {
        [Member("file_id")] public int FileId;
        [Member("comment")] public string? Comment;
        [Member("is_development")] public bool IsDevelopment;
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
        [Member("server_group_order")] public byte ServerGroupOrder;
    }
    
    public class ResourceConfigEntity : BaseDbEntity
    {
        [Member("config_key")] public string Key = string.Empty;
        [Member("config_value")] public string Value = string.Empty;
    }

    public class ResourceTableTargetEntity : BaseDbEntity
    {
        [Member("table_name")] public string TableName = "";
        [Member("target")] public byte Target;
        [Member("comment")] public string? Comment;
        [Member("updated_utc")] public DateTime UpdatedUtc;
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

    public static async Task UpsertMaintenanceEnvAsync(DbConn conn, MaintenanceEnvEntity entity, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_upsert_maintenance_env");
        cmd.AddWithEntity(entity);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task InsertMaintenanceBypassAsync(DbConn conn, MaintenanceBypassEntity entity, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_insert_maintenance_bypass");
        cmd.AddWithEntity(entity);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task RemoveMaintenanceBypassAsync(DbConn conn, int id, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_remove_maintenance_bypass");
        cmd.Add("id", DbType.Int32, id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<List<ResourceTableTargetEntity>> GetResourceTableTargetsAsync(DbConn conn, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_get_resource_table_targets");
        return await cmd.ExecuteReaderListAsync<ResourceTableTargetEntity>(ct);
    }

    public static async Task UpsertResourceTableTargetAsync(
        DbConn conn, ResourceTableTargetEntity entity, CancellationToken ct)
    {
        var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_upsert_resource_table_target");
        cmd.AddWithEntity(entity);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<List<ResourceConfigEntity>> GetResourceConfigsAsync(DbConn conn, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_get_resource_configs");
        return await cmd.ExecuteReaderListAsync<ResourceConfigEntity>(ct);
    }

    public static async Task<List<ClientBuildVersionEntity>> GetClientBuildVersions(DbConn conn, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_get_client_build_versions");
        return await cmd.ExecuteReaderListAsync<ClientBuildVersionEntity>(ct);
    }

    public static async Task<List<ResourcePatchVersionEntity>> GetLatestResourcePatchVersions(DbConn conn, ServerGroupType serverGroupType, int count, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_get_latest_resource_patch_versions");
        cmd.Add("server_group_type", DbType.String, serverGroupType.ToString());
        cmd.Add("count", DbType.Int32, count);
        return await cmd.ExecuteReaderListAsync<ResourcePatchVersionEntity>(ct);
    }
    public static async Task<List<ResourceRefsFileEntity>> GetResourceRefsFilesAsync(DbConn conn, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_get_resource_refs_files");
        return await cmd.ExecuteReaderListAsync<ResourceRefsFileEntity>(ct);
    }

    public static async Task<int> GetLatestResourceRefsFileIdAsync(DbConn conn, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_get_latest_resource_refs_file_id");
        return await cmd.ExecuteScalarAsync<int>(ct);
    }

    public static async Task<int> GetLatestResourceVersionAsync(DbConn conn, ServerGroupType serverGroupType, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_get_latest_resource_version");
        cmd.Add("server_group_type", DbType.String, serverGroupType.ToString());
        var latest = await cmd.ExecuteScalarAsync<int>(ct);
        return latest;
    }

    public static async Task UpsertClientBuildVersionAsync(
        DbConn conn,
        ClientBuildVersionEntity entity,
        CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_upsert_client_build_version");
        cmd.AddWithEntity(entity);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task InsertResourcePatchVersionAsync(
        DbConn conn,
        ResourcePatchVersionEntity entity,
        CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_insert_resource_patch_version");
        cmd.AddWithEntity(entity);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task InsertResourceRefsFileAsync(
        DbConn conn,
        ResourceRefsFileEntity entity,
        CancellationToken ct)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "proc_insert_resource_refs_file");
        cmd.AddWithEntity(entity);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
