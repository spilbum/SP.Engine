using OperationTool.Localization;
using SP.Shared.Resource;
using SP.Shared.Resource.Localization;

namespace OperationTool.Services;

public interface ILocalizationService
{
    Task<LocalizationParseResult> ParseAsync(string xlsxFilePath, CancellationToken ct);
    Task<string> GenerateLocsFileAsync(LocalizationParseResult result, int fileId, string outputDir, CancellationToken ct);
}

public class LocalizationService(IFileUploader fileUploader) : ILocalizationService
{
    public async Task<LocalizationParseResult> ParseAsync(string xlsxFilePath, CancellationToken ct)
        => await Task.Run(() => LocalizationParser.ParseFile(xlsxFilePath), ct);
    
    public async Task<string> GenerateLocsFileAsync(
        LocalizationParseResult result,
        int fileId,
        string outputDir,
        CancellationToken ct)
    {
        var baseDir = Path.Combine(outputDir, $"{fileId:D6}");
        Directory.CreateDirectory(baseDir);
        
        var fileDir = Path.Combine(baseDir, $"{PatchConst.LocsFile}");
        Directory.CreateDirectory(fileDir);
        
        foreach (var lang in result.Languages)
        {
            var map = result.ByLanguage[lang];
            if (map.Count == 0)
                continue;
            
            await LocFileWriter.WriteAsync(
                lang,
                map,
                Path.Combine(fileDir, $"{lang}.{PatchConst.LocFile}"),
                ct);
        }

        var filePath = Path.Combine(baseDir, $"{fileId}.{PatchConst.LocsFile}");
        await LocPackWriter.WriteAsync(fileDir, filePath, ct);
        
        return filePath;
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
