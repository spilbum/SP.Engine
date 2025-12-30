namespace OperationTool.Services;

public interface ISettingsProvider
{
    ToolSettings Settings { get; }
    Task<ToolSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}


