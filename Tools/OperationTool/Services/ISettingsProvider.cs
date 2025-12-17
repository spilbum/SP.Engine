namespace OperationTool.Services;

public interface ISettingsProvider
{
    ToolSettings Settings { get; }
    Task<ToolSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}

public sealed class ToolSettingsProvider(ISettingsStorage storage) : ISettingsProvider
{
    public ToolSettings Settings { get; private set; } = new();

    public async Task<ToolSettings> LoadAsync(CancellationToken ct = default)
    {
        var s = await storage.LoadAsync(ct);
        Settings = s ?? throw new InvalidOperationException("Failed to load settings");
        return s;
    }
    
    public async Task SaveAsync(CancellationToken ct = default)
    {
        await storage.SaveAsync(Settings, ct);
    }
}
