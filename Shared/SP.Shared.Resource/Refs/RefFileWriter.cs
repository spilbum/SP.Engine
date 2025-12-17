using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SP.Core.Serialization;
using SP.Shared.Resource.Table;

namespace SP.Shared.Resource.Refs;

public static class RefFileWriter
{
    public static void Write(RefTableSchema schema, RefTableData data, string path)
    {
        var w = new NetWriter();
        
        // magic
        w.WriteByte((byte)'R');
        w.WriteByte((byte)'R');
        w.WriteByte((byte)'E');
        w.WriteByte((byte)'F');
        
        // 테이블 명
        w.WriteString(schema.Name);
        
        // 행 개수
        w.WriteVarUInt((uint)data.Rows.Count);
        
        foreach (var row in data.Rows)
        {
            for (var i = 0; i < schema.Columns.Count; i++)
            {
                var column = schema.Columns[i];
                var value = row.Get(i);
                WriteValue(ref w, column.Type, value);
            }
        }

        var bytes = w.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    public static async Task WriteAsync(
        RefTableSchema schema, 
        RefTableData data, 
        string path,
        CancellationToken ct = default)
    {
        var w = new NetWriter();
        
        w.WriteByte((byte)'R');
        w.WriteByte((byte)'R');
        w.WriteByte((byte)'E');
        w.WriteByte((byte)'F');
        
        w.WriteString(schema.Name);
        w.WriteVarUInt((uint)data.Rows.Count);

        foreach (var row in data.Rows)
        {
            for (var i = 0; i < schema.Columns.Count; i++)
            {
                var column = schema.Columns[i];
                var value = row.Get(i);
                WriteValue(ref w, column.Type, value);
            }
        }
        
        var bytes = w.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    private static void WriteValue(ref NetWriter w, ColumnType type, object? value)
    {
        switch (type)
        {
            case ColumnType.String:
                w.WriteString((string?)value ?? string.Empty);
                break;
            case ColumnType.Byte:
                w.WriteByte((byte)(value ?? 0));
                break;
            case ColumnType.Int32:
                w.WriteInt32(Convert.ToInt32(value ?? 0));
                break;
            case ColumnType.Int64:
                w.WriteInt64(Convert.ToInt64(value ?? 0L));
                break;
            case ColumnType.Float:
                w.WriteSingle(Convert.ToSingle(value ?? 0f));
                break;
            case ColumnType.Double:
                w.WriteDouble(Convert.ToDouble(value ?? 0d));
                break;
            case ColumnType.Boolean:
                w.WriteBool(value is true);
                break;
            case ColumnType.DateTime:
                var dt = value is DateTime d ? d : DateTime.MinValue;
                w.WriteInt64(dt.ToUniversalTime().Ticks);
                break;
            default:
                throw new NotSupportedException($"Unsupported ColumnType: {type}");
        }
    }
}





