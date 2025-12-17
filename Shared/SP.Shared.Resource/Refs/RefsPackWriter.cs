using System.Threading;
using System.Threading.Tasks;

namespace SP.Shared.Resource.Refs;

public static class RefsPackWriter
{
    public static void Write(string refDir, string outputRefsPath)
        => ZipHelper.PackDir(refDir, "*.ref", outputRefsPath);
    
    public static async Task WriteAsync(
        string refDir,
        string outputRefsPath,
        CancellationToken ct = default)
        => await ZipHelper.PackDirAsync(refDir, "*.ref", outputRefsPath, ct);
    

}
