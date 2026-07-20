using System;
using System.IO;
using SP.Core.Serialization;
using SP.Resource.Table;

namespace SP.Resource.Refs;

public static class RefFileReader
{
    public static RefTableData Read(RefTableSchema schema, ReadOnlySpan<byte> data)
    {
        var r = new NetReader(data);

        if (r.ReadByte() != (byte)'R' ||
            r.ReadByte() != (byte)'R' ||
            r.ReadByte() != (byte)'E' ||
            r.ReadByte() != (byte)'F')
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
                ReadAndSet(ref r, row, c, type);
            }
            table.Rows.Add(row);
        }

        return table;
    }

    private static void ReadAndSet(ref NetReader r, RefRow row, int index, ColumnType type)
    {
        switch (type)
        {
            case ColumnType.String: row.SetString(index, r.ReadString()); break;
            case ColumnType.Byte: row.SetByte(index, r.ReadByte()); break;
            case ColumnType.Int32: row.SetInt32(index, r.ReadInt32()); break;
            case ColumnType.Int64: row.SetInt64(index, r.ReadInt64()); break;
            case ColumnType.Float: row.SetSingle(index, r.ReadSingle()); break;
            case ColumnType.Double: row.SetDouble(index, r.ReadDouble()); break;
            case ColumnType.Boolean: row.SetBoolean(index, r.ReadBool()); break;
            case ColumnType.DateTime: row.SetDateTime(index, new DateTime(r.ReadInt64(), DateTimeKind.Utc)); break;
            default: throw new NotSupportedException($"Unsupported ColumnType: {type}");
        }
    }
}
