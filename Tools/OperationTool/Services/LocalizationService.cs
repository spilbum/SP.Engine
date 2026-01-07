using OperationTool.Localization;
using SP.Shared.Resource.Localization;

namespace OperationTool.Services;

public interface ILocalizationService
{
    Task<LocalizationParseResult> ParseAsync(string xlsxFilePath, CancellationToken ct);
    Task<string> GenerateAsync(LocalizationParseResult result, int fileId, string outputDir, CancellationToken ct);
}

public class LocalizationService(IFileUploader fileUploader) : ILocalizationService
{
    public async Task<LocalizationParseResult> ParseAsync(string xlsxFilePath, CancellationToken ct)
        => await Task.Run(() => LocalizationParser.ParseFile(xlsxFilePath), ct);
    
    public async Task<string> GenerateAsync(
        LocalizationParseResult result,
        int fileId,
        string outputDir,
        CancellationToken ct)
    {
        var versionDir = Path.Combine(outputDir, $"{fileId}");
        Directory.CreateDirectory(versionDir);
        
        var locsDir = Path.Combine(versionDir, "locs");
        Directory.CreateDirectory(locsDir);
        
        foreach (var lang in result.Languages)
        {
            var map = result.ByLanguage[lang];
            if (map.Count == 0)
                continue;
            
            await LocFileWriter.WriteAsync(
                lang,
                map,
                Path.Combine(locsDir, $"{lang}.loc"),
                ct);
        }

        var locsFilePath = Path.Combine(versionDir, $"{fileId}.locs");
        await LocPackWriter.WriteAsync(locsDir, locsFilePath, ct);

        await UploadIfExistsAsync($"localization/{fileId}.locs", locsFilePath, ct);
        return locsFilePath;
    }
    
    private async Task UploadIfExistsAsync(string key, string filePath, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
            return;
        
        await using var stream = File.OpenRead(filePath);
        await fileUploader.UploadAsync(key, stream, info.Length, ct);
    }
}
