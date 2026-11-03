using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SP.Core.Buffers;
using SP.Core.Serialization;
using SP.Resource.Table;

namespace SP.Resource.Refs;

public static class RefFileWriter
{
    private const int InitialBufferCapacity = 65536;

    public static void Write(RefTableSchema schema, RefTableData data, string path)
    {
        using var resizer = BufferResizer.Rent(InitialBufferCapacity);
        var written = Serialize(resizer, schema, data);

        EnsureDirectory(path);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.Write(resizer.GetWrittenSpan(written));
    }

    public static async Task WriteAsync(
        RefTableSchema schema,
        RefTableData data,
        string path,
        CancellationToken ct = default)
    {
        using var resizer = BufferResizer.Rent(InitialBufferCapacity);
        var written = Serialize(resizer, schema, data);

        EnsureDirectory(path);
        await using var fs = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);

        await fs.WriteAsync(resizer.GetWrittenMemory(written), ct).ConfigureAwait(false);
    }

    private static int Serialize(BufferResizer resizer, RefTableSchema schema, RefTableData data)
    {
        var w = new NetWriter(resizer.Span, resizer);

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
                GetAndWrite(ref w, row, i, column.Type);
            }
        }

        return w.WrittenCount;
    }

    private static void GetAndWrite(ref NetWriter w, RefRow row, int index, ColumnType type)
    {
        switch (type)
        {
            case ColumnType.String:
                w.WriteString(row.GetString(index));
                break;
            case ColumnType.Byte:
                w.WriteByte(row.GetByte(index));
                break;
            case ColumnType.Int32:
                w.WriteInt32(row.GetInt32(index));
                break;
            case ColumnType.Int64:
                w.WriteInt64(row.GetInt64(index));
                break;
            case ColumnType.Float:
                w.WriteSingle(row.GetSingle(index));
                break;
            case ColumnType.Double:
                w.WriteDouble(row.GetDouble(index));
                break;
            case ColumnType.Boolean:
                w.WriteBool(row.GetBoolean(index));
                break;
            case ColumnType.DateTime:
                w.WriteDateTime(row.GetDateTime(index));
                break;
            default:
                throw new NotSupportedException($"Unsupported ColumnType: {type}");
        }
    }

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}
