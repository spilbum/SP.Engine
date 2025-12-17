using System.Collections.Immutable;
using ResourceServer.DatabaseHandler;

namespace ResourceServer.Services;


public interface IResourceConfigStore
{
    string? Get(string key);
    int GetInt(string key, int defaultValue = 0);
    bool GetBool(string key, bool defaultValue = false);
    Task ReloadAsync(CancellationToken ct = default);
}

public sealed class ResourceConfigStore(IDbConnector dbConnector) : IResourceConfigStore
{
    private ImmutableDictionary<string, string> _configs =
        ImmutableDictionary<string, string>.Empty;
    
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

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

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _reloadLock.WaitAsync(ct);
        try
        {
            using var conn = await dbConnector.OpenAsync(ct).ConfigureAwait(false);
            var entities = await ResourceDb.GetResourceConfigs(conn, ct).ConfigureAwait(false);
            
            var dict = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

            foreach (var e in entities
                         .Where(e => !string.IsNullOrWhiteSpace(e.Key)))
            {
                dict[e.Key.Trim()] = e.Value;
            }
            
            _configs = dict.ToImmutable();
        }
        finally
        {
            _reloadLock.Release();
        }
    }
}
