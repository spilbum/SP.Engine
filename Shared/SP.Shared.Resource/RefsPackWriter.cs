using System.Threading;
using System.Threading.Tasks;

namespace SP.Shared.Resource;

public static class RefsPackWriter
{
    public static void CreateRefsFile(string refDir, string outputRefsPath)
        => ZipHelper.PackDir(refDir, "*.ref", outputRefsPath);
    
    public static void CreateSchsFile(string schDir, string outputSchsPath)
        => ZipHelper.PackDir(schDir, "*.sch", outputSchsPath); 
    
    public static async Task CreateRefsFileAsync(
        string refDir,
        string outputRefsPath,
        CancellationToken ct = default)
        => await ZipHelper.PackDirAsync(refDir, "*.ref", outputRefsPath, ct);
    
    public static async Task CreateSchsFileAsync(
        string schDir,
        string outputSchsPath,
        CancellationToken ct = default)
        => await ZipHelper.PackDirAsync(schDir, "*.sch", outputSchsPath, ct);
}
