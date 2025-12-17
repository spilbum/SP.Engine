using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SP.Shared.Resource.Table;

namespace SP.Shared.Resource.Refs;

public static class RefsPackReader
{
    public static List<RefTableData> Read(
        List<RefTableSchema> schemas,
        string path)
    {
        var entries = ZipHelper.ReadAll(path);
        var list = new List<RefTableData>(entries.Count);
        
        var map = schemas.ToDictionary(schema => schema.Name, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var name = Path.GetFileNameWithoutExtension(entry.Name);
            if (!map.TryGetValue(name, out var schema))
                throw new InvalidDataException($"Schema not found: {name}");
            
            var table = RefFileReader.Read(schema, entry.Data);
            list.Add(table);
        }
        return list;
    }

    public static async Task<List<RefTableData>> ReadAsync(
        List<RefTableSchema> schemas,
        string path,
        CancellationToken ct = default)
    {
        var entries = await ZipHelper.ReadAllAsync(path, ct).ConfigureAwait(false);
        var list = new List<RefTableData>(entries.Count);
        
        var map = schemas.ToDictionary(schema => schema.Name, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            
            var tableName = Path.GetFileNameWithoutExtension(entry.Name);
            if (!map.TryGetValue(tableName, out var schema))
                throw new InvalidDataException($"Schema not found: {tableName}");
            
            var table = RefFileReader.Read(schema, entry.Data);
            list.Add(table);
        }
        return list;
    }
}

