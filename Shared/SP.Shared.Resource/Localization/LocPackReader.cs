using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SP.Shared.Resource.Localization;

public class LocPackReader
{
    public static async Task<List<LocFile>> ReadAsync(
        string locsFilePath,
        CancellationToken ct = default)
    {
        var entries = await ZipHelper.ReadAllAsync(locsFilePath, ct);
        var list = new List<LocFile>(entries.Count);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            
            if (!entry.Name.EndsWith(".loc", StringComparison.OrdinalIgnoreCase))
                continue;

            var locFile = LocFileReader.Read(entry.Data);
            list.Add(locFile);
        }
        
        return list;
    }
}
