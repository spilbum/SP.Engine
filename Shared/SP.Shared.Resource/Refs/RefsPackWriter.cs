using System.Threading;
using System.Threading.Tasks;

namespace SP.Shared.Resource.Refs;

public static class RefsPackWriter
{
    public static void Write(string refDir, string refsFilePath)
        => ZipHelper.PackDir(refDir, "*.ref", refsFilePath);
    
    public static async Task WriteAsync(
        string refDir,
        string refsFilePath,
        CancellationToken ct = default)
        => await ZipHelper.PackDirAsync(refDir, "*.ref", refsFilePath, ct);
    

}
