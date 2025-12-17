using System.Data;
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
    
    public class ResourceConfigEntity : BaseDbEntity
    {
        [Member("config_key")] public string Key = string.Empty;
        [Member("config_value")] public string Value = string.Empty;
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
    public static async Task<List<ResourceRefsFileEntity>> GetResourceRefsFiles(DbConn conn, CancellationToken ct)
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
