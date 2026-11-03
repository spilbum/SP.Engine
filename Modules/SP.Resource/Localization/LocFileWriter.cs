using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SP.Core.Buffers;
using SP.Core.Serialization;

namespace SP.Resource.Localization;

public static class LocFileWriter
{
    private const int InitialBufferCapacity = 65536;

    public static void Write(
        string dir,
        string language,
        IReadOnlyDictionary<string, string> map)
    {
        using var resizer = BufferResizer.Rent(InitialBufferCapacity);
        var written = Serialize(resizer, language, map);

        EnsureDirectory(dir);

        var filePath = Path.Combine(dir, $"{language}.{PatchConst.LocFile}");
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.Write(resizer.GetWrittenSpan(written));
    }

    public static async Task WriteAsync(
        string dir,
        string language,
        IReadOnlyDictionary<string, string> map,
        CancellationToken ct = default)
    {
        using var resizer = BufferResizer.Rent(InitialBufferCapacity);
        var written = Serialize(resizer, language, map);

        EnsureDirectory(dir);
        
        var filePath = Path.Combine(dir, $"{language}.{PatchConst.LocFile}");
        await using var fs = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);

        await fs.WriteAsync(resizer.GetWrittenMemory(written), ct).ConfigureAwait(false);
    }

    private static int Serialize(
        BufferResizer resizer,
        string language,
        IReadOnlyDictionary<string, string> map)
    {
        var w = new NetWriter(resizer.Span, resizer);

        w.WriteByte((byte)'L');
        w.WriteByte((byte)'L');
        w.WriteByte((byte)'O');
        w.WriteByte((byte)'C');
        w.WriteString(language);

        var ordered = map.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
        w.WriteVarUInt((uint)ordered.Count);
        foreach (var (key, value) in ordered)
        {
            w.WriteString(key);
            w.WriteString(value ?? string.Empty);
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
