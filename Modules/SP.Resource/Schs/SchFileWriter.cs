using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SP.Core.Buffers;
using SP.Core.Serialization;
using SP.Resource.Table;

namespace SP.Resource.Schs;

public static class SchFileWriter
{
    private const int InitialBufferCapacity = 4096;

    public static void Write(RefTableSchema schema, string path)
    {
        using var resizer = BufferResizer.Rent(InitialBufferCapacity);
        var written = Serialize(resizer, schema);

        EnsureDirectory(path);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.Write(resizer.GetWrittenSpan(written));
    }

    public static async Task WriteAsync(
        RefTableSchema schema,
        string path,
        CancellationToken ct = default)
    {
        using var resizer = BufferResizer.Rent(InitialBufferCapacity);
        var written = Serialize(resizer, schema);

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

    private static int Serialize(BufferResizer resizer, RefTableSchema schema)
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

        return w.WrittenCount;
    }

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}
