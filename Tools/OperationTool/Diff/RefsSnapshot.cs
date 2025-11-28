using SP.Shared.Resource;

namespace OperationTool.Diff;

public sealed class RefsTableSnapshot(RefTableSchema schema, RefTableData data)
{
    public RefTableSchema Schema { get; } = schema;
    public RefTableData Data { get; } = data;
}

public sealed class RefsSnapshot(IReadOnlyDictionary<string, RefsTableSnapshot> tables)
{
    public IReadOnlyDictionary<string, RefsTableSnapshot> Tables { get; } = tables;
}

public static class RefsSnapshotFactory
{
    public static async Task<RefsSnapshot> FromPackAsync(
        string schsPath,
        string refsPath,
        CancellationToken ct)
    {
        var schemas = await RefsPackReader.ReadSchsFileAsync(schsPath, ct);
        var schemaMap = schemas.ToDictionary(s => s.Name, StringComparer.Ordinal);
        
        var list = await RefsPackReader.ReadRefsFileAsync(schemas, refsPath, ct);
        var tableMap = new Dictionary<string, RefsTableSnapshot>(StringComparer.Ordinal);

        foreach (var data in list)
        {
            if (!schemaMap.TryGetValue(data.Name, out var schema))
                throw new InvalidDataException($"Schema not found for table: {data.Name}");
            
            tableMap[data.Name] = new RefsTableSnapshot(schema, data);
        }
        
        return new RefsSnapshot(tableMap);
    }
}



