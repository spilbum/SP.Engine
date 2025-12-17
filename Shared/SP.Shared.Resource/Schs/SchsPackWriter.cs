using System.Threading;
using System.Threading.Tasks;

namespace SP.Shared.Resource.Schs;

public static class SchsPackWriter
{
    public static void Write(string schDir, string outputSchsPath)
        => ZipHelper.PackDir(schDir, "*.sch", outputSchsPath); 
    
    public static async Task WriteAsync(
        string schDir,
        string outputSchsPath,
        CancellationToken ct = default)
        => await ZipHelper.PackDirAsync(schDir, "*.sch", outputSchsPath, ct);
}
