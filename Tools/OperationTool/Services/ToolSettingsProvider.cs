namespace OperationTool.Services;

public sealed class ToolSettingsProvider(ISettingsStorage storage) : ISettingsProvider
{
    public ToolSettings Settings { get; private set; } = new();

    public async Task<ToolSettings> LoadAsync(CancellationToken ct = default)
    {
        Settings = await storage.LoadAsync(ct) ?? new ToolSettings();
        return Settings;
    }
    
    public Task SaveAsync(CancellationToken ct = default)
        => storage.SaveAsync(Settings, ct); 
}
