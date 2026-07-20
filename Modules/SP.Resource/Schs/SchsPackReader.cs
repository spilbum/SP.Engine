using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SP.Resource.Table;

namespace SP.Resource.Schs;

public static class SchsPackReader
{
    public static List<RefTableSchema> ReadSync(string path)
    {
        var entries = ZipHelper.ReadAll(path);
        var list = new List<RefTableSchema>(entries.Count);
        list.AddRange(entries.Select(entry => SchFileReader.Read(entry.Data)));
        return list;
    }

    public static async Task<List<RefTableSchema>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var entries = await ZipHelper.ReadAllAsync(path, cancellationToken).ConfigureAwait(false);
        var list = new List<RefTableSchema>(entries.Count);
        list.AddRange(entries.Select(entry => SchFileReader.Read(entry.Data)));
        return list;
    }
}
