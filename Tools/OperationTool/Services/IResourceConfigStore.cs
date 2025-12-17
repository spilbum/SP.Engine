using System.Collections.Immutable;
using OperationTool.DatabaseHandler;

namespace OperationTool.Services;

public interface IResourceConfigStore
{
    string? Get(string key);
    int GetInt(string key, int defaultValue = 0);
    bool GetBool(string key, bool defaultValue = false);
    Task LoadAsync(CancellationToken ct = default);
}

public sealed class ResourceConfigStore(IDbConnector dbConnector) : IResourceConfigStore
{
    private ImmutableDictionary<string, string> _configs =
        ImmutableDictionary<string, string>.Empty;
    
    public string? Get(string key)
    {
        var snapshot = Volatile.Read(ref _configs);
        return CollectionExtensions.GetValueOrDefault(snapshot, key);
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        var v = Get(key);
        return int.TryParse(v, out var result) ? result : defaultValue;
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        var v = Get(key);
        return bool.TryParse(v, out var result) ? result : defaultValue;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        using var conn = await dbConnector.OpenAsync(ct);
        var entities = await ResourceDb.GetResourceConfigsAsync(conn, ct);
        
        var dict = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        foreach (var entity in entities.Where(e => !string.IsNullOrWhiteSpace(e.Value)))
        {
            dict[entity.Key] = entity.Value;
        }
        
        _configs = dict.ToImmutable();
    }
}
