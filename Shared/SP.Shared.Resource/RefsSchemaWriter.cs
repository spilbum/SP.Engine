using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SP.Core.Serialization;

namespace SP.Shared.Resource;

public static class RefsSchemaWriter
{
    public static void WriteSchFile(RefTableSchema schema, string path)
    {
        using var w = new NetWriter();

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
        }

        var bytes = w.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    public static async Task WriteSchFileAsync(
        RefTableSchema schema, 
        string path, 
        CancellationToken ct = default)
    {
        var w = new NetWriter();
        
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
        }
        
        var bytes = w.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }
}
