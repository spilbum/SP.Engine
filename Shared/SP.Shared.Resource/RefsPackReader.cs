
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SP.Shared.Resource;

public static class RefsPackReader
{
    public static List<RefTableSchema> ReadSchsFile(string path)
    {
        var entries = ZipHelper.ReadAll(path);
        var list = new List<RefTableSchema>(entries.Count);
        list.AddRange(entries.Select(entry => RefsSchemaReader.ReadSchFile(entry.Data)));
        return list;
    }

    public static async Task<List<RefTableSchema>> ReadSchsFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var entries = await ZipHelper.ReadAllAsync(path, cancellationToken).ConfigureAwait(false);
        var list = new List<RefTableSchema>(entries.Count);
        list.AddRange(entries.Select(entry => RefsSchemaReader.ReadSchFile(entry.Data)));
        return list;
    }

    public static List<RefTableData> ReadRefsFile(
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
            
            var table = RefsDataReader.ReadRefsFile(schema, entry.Data);
            list.Add(table);
        }
        return list;
    }

    public static async Task<List<RefTableData>> ReadRefsFileAsync(
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
            
            var table = RefsDataReader.ReadRefsFile(schema, entry.Data);
            list.Add(table);
        }
        return list;
    }
}

