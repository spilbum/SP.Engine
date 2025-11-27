namespace OperationTool.Storage;

public interface ISettingsProvider
{
    ToolSettings Current { get; }
    Task SaveAsync();
}

public sealed class SettingsProvider : ISettingsProvider
{
    private readonly ISettingsStorage _storage;
    public ToolSettings Current { get; }

    public SettingsProvider(ISettingsStorage storage)
    {
        _storage = storage;
        
        var s = storage.Load();
        if (s is null)
        {
            s = new ToolSettings();
            storage.SaveAsync(s);
        }
        
        Current = s;
    }
    
    public async Task SaveAsync()
        => await _storage.SaveAsync(Current);
}
