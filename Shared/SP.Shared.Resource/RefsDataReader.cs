using System;
using System.IO;
using SP.Core.Serialization;

namespace SP.Shared.Resource;

public static class RefsDataReader
{
    public static RefTableData ReadRefsFile(RefTableSchema schema, ReadOnlySpan<byte> data)
    {
        var r = new NetReader(data);

        if (r.ReadByte() != 'R' || 
            r.ReadByte() != 'R' || 
            r.ReadByte() != 'E' || 
            r.ReadByte() != 'F')
            throw new InvalidDataException("Invalid .ref magic");
        
        var name = r.ReadString();
        if (!string.Equals(name, schema.Name, StringComparison.Ordinal))
            throw new InvalidDataException($"Schema name mismatch: file={name}, schema={schema.Name}");
        
        var rowCount = (int)r.ReadVarUInt();
        var table = new RefTableData(schema.Name);

        for (var i = 0; i < rowCount; i++)
        {
            var row = new RefRow(schema.Columns.Count);
            for (var c = 0; c < schema.Columns.Count; c++)
            {
                var type = schema.Columns[c].Type;
                var value = ReadValue(ref r, type);
                row.Set(c, value);
            }
            table.Rows.Add(row);
        }
        
        return table;
    }

    private static object? ReadValue(ref NetReader r, ColumnType type)
    {
        return type switch
        {
            ColumnType.String => r.ReadString(),
            ColumnType.Int32 => r.ReadInt32(),
            ColumnType.Int64 => r.ReadInt64(),
            ColumnType.Float => r.ReadSingle(),
            ColumnType.Double => r.ReadDouble(),
            ColumnType.Boolean => r.ReadBool(),
            ColumnType.DateTime => new DateTime(r.ReadInt64(), DateTimeKind.Utc),
            _ => throw new NotSupportedException($"Unsupported ColumnType: {type}")
        };
    }
}
