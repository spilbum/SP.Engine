using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SP.Core.Buffers;
using SP.Core.Serialization;
using SP.Resource.Table;

namespace SP.Resource.Schs;

public static class SchFileWriter
{
    public static void Write(RefTableSchema schema, string path)
    {
        using var resizer = BufferResizer.Rent(65536);
        var bytes = SerializeToBytes(resizer, schema);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    public static async Task WriteAsync(
        RefTableSchema schema,
        string path,
        CancellationToken ct = default)
    {
        using var resizer = BufferResizer.Rent(65536);
        var bytes = SerializeToBytes(resizer, schema);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    private static byte[] SerializeToBytes(BufferResizer resizer, RefTableSchema schema)
    {
        var w = new NetWriter(resizer.Span, resizer);
        w.WriteByte((byte)'R');
        w.WriteByte((byte)'S');
        w.WriteByte((byte)'C');
        w.WriteByte((byte)'H');

        w.WriteString(schema.Name);
        w.WriteVarUInt((uint)schema.Columns.Count);

        foreach (var column in schema.Columns)
        {
            w.WriteString(column.Name);
            w.WriteByte((byte)column.Type);
            w.WriteBool(column.IsKey);
            w.WriteInt32(column.Length ?? 0);
        }

        return resizer.GetWrittenSpan(w.WrittenCount).ToArray();
    }
}
