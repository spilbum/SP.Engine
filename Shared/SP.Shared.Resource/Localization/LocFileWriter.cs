using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        var w = new NetWriter();
        
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

        await File.WriteAllBytesAsync(path, w.ToArray(), ct);
    }
}
