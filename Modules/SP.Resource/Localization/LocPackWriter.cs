using System.Threading;
using System.Threading.Tasks;

namespace SP.Resource.Localization;

public static class LocPackWriter
{
    public static async Task WriteAsync(
        string locDir,
        string locsFilePath,
        CancellationToken ct = default)
        => await ZipHelper.PackDirAsync(locDir, "*.loc", locsFilePath, ct);
}
