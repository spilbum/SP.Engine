using System.Text.Json;

namespace OperationTool.Storage;

public sealed class ToolSettingsStorage : ISettingsStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    
    private readonly string _filePath = Path.Combine(FileSystem.AppDataDirectory, "Configs", "settings.json");

    public ToolSettings? Load()
    {
        if (!File.Exists(_filePath))
            return null;

        using var fs = File.OpenRead(_filePath);
        return JsonSerializer.Deserialize<ToolSettings>(fs, JsonOptions);
    }

    public async Task SaveAsync(ToolSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        await using var fs = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(fs, settings, JsonOptions);
    }
}
