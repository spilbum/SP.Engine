using System.Collections.Concurrent;

namespace SP.Shared.Database;

public abstract class BaseDbConnector
{
    private readonly ConcurrentDictionary<string, Entry> _connections = new(StringComparer.Ordinal);

    protected bool HasConnection(string dbKind)
        => !string.IsNullOrWhiteSpace(dbKind) && _connections.ContainsKey(dbKind);

    protected void Register(string dbKind, string connectionString, IDbProvider provider)
    {
        if (string.IsNullOrEmpty(dbKind)) throw new ArgumentNullException(nameof(dbKind));
        if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));
        ArgumentNullException.ThrowIfNull(provider);

        var entry = new Entry(connectionString, provider);
        if (!_connections.TryAdd(dbKind, entry))
            throw new InvalidOperationException($"{dbKind} is already registered.");
    }

    protected DbConn Open(string dbKind)
    {
        if (!_connections.TryGetValue(dbKind, out var entry))
            throw new Exception($"Unknown DbKind={dbKind}");

        var (cs, provider) = entry;

        try
        {
            var raw = provider.CreateConnection(cs)
                      ?? throw new InvalidOperationException("Provider returned null connection.");
            
            var dbConn = new DbConn(raw, provider);
            dbConn.Open();
            return dbConn;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to open DB. DbKind={dbKind}, CS={cs}", ex);
        }
    }

    protected async Task<DbConn> OpenAsync(string dbKind, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(dbKind, out var entry))
            throw new Exception($"Unknown DbKind={dbKind}");

        var (cs, provider) = entry;

        try
        {
            var raw = provider.CreateConnection(cs)
                      ?? throw new InvalidOperationException("Provider returned null connection.");

            var dbConn = new DbConn(raw, provider);
            await dbConn.OpenAsync();
            return dbConn;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to open DB (async). DbKind={dbKind}, CS={cs}", ex);
        }
    }

    protected bool TryOpen(string dbKind, out DbConn? conn)
    {
        conn = null;
        try
        {
            conn = Open(dbKind);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    protected void Unregister(string dbKind)
        => _connections.TryRemove(dbKind, out _);
    
    protected string GetConnectionString(string dbKind)
        => _connections.TryGetValue(dbKind, out var entry) ? entry.ConnectionString : string.Empty;
    
    private sealed record Entry(string ConnectionString, IDbProvider Provider);
}
