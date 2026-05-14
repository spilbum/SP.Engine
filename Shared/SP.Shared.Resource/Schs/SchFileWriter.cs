using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SP.Core;
using SP.Core.Serialization;
using SP.Shared.Resource.Table;

namespace SP.Shared.Resource.Schs;

public static class SchFileWriter
{
    public static void Write(RefTableSchema schema, string path)
    {
        var buf = new PooledBuffer();
        var w = new NetWriter(buf);

        try
        {
            // Magic
            w.WriteByte((byte)'R');
            w.WriteByte((byte)'S');
            w.WriteByte((byte)'C');
            w.WriteByte((byte)'H');

            // 테이블 이름
            w.WriteString(schema.Name);

            // 컬럼 수
            w.WriteVarUInt((uint)schema.Columns.Count);

            foreach (var column in schema.Columns)
            {
                w.WriteString(column.Name);
                w.WriteByte((byte)column.Type); 
                w.WriteBool(column.IsKey);
                w.WriteInt32(column.Length ?? 0);
            }

            var bytes = w.WrittenSpan.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        finally
        {
            buf.Dispose();
        }
    }

    public static async Task WriteAsync(
        RefTableSchema schema, 
        string path, 
        CancellationToken ct = default)
    {
        var buf = new PooledBuffer();
        var w = new NetWriter(buf);

        try
        {
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
        
            var bytes = w.WrittenSpan.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, ct);
        }
        finally
        {
            buf.Dispose();
        }
    }
}
