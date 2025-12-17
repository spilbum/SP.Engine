using System.Text.Json;

namespace OperationTool.Services;

public sealed class DbSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "ResourceDB";
    public string User { get; set; } = "root";
    public string Password { get; set; } = "";

    public string GetConnectionString()
        => $"Server={Host};Port={Port};Database={Database};User Id={User};Password={Password}";
}

public sealed class ToolSettings
{
    public DbSettings Database { get; set; } = new();
}

public interface ISettingsStorage
{
    Task<ToolSettings?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(ToolSettings settings, CancellationToken ct = default);
}

public sealed class ToolSettingsStorage : ISettingsStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    
    private readonly string _filePath = Path.Combine(FileSystem.AppDataDirectory, "Configs", "settings.json");

    public async Task<ToolSettings?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return null;

        await using var fs = new FileStream(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        return await JsonSerializer.DeserializeAsync<ToolSettings>(fs, JsonOptions, ct);
    }

    public async Task SaveAsync(ToolSettings settings, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        await using var fs = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(fs, settings, JsonOptions, ct);
    }
}
