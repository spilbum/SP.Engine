using System.Threading;
using System.Threading.Tasks;

namespace SP.Shared.Resource.Localization;

public class LocPackWriter
{
    public static async Task WriteAsync(
        string locDir, 
        string locsFilePath,
        CancellationToken ct = default)
        => await ZipHelper.PackDirAsync(locDir, "*.loc", locsFilePath, ct);
}
