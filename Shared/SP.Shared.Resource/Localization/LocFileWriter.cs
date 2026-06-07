using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SP.Core;
using SP.Core.Buffers;
using SP.Core.Serialization;

namespace SP.Shared.Resource.Localization;

public static class LocFileWriter
{
    public static async Task WriteAsync(
        string language,
        IReadOnlyDictionary<string, string> map,
        string path,
        CancellationToken ct = default)
    {
        var buf = BufferOwnerPool.Rent(65536);

        try
        {
            var written = SerializeToBuffer(buf.Memory.Span, language, map);
            var memory = buf.Memory[..written];
            await File.WriteAllBytesAsync(path, memory.ToArray(), ct);
        }
        finally
        {
            buf.Dispose();
        }
    }

    private static int SerializeToBuffer(
        Span<byte> destination,
        string language,
        IReadOnlyDictionary<string, string> map)
    {
        var w = new NetWriter(destination);
        
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
}
