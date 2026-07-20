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
    public static async Task WriteAsync(
        string language,
        IReadOnlyDictionary<string, string> map,
        string path,
        CancellationToken ct = default)
    {
        using var resizer = BufferResizer.Rent(65536);

        var bytes = SerializeToBytes(resizer, language, map);

        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    private static byte[] SerializeToBytes(
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

        return resizer.GetWrittenSpan(w.WrittenCount).ToArray();
    }
}
