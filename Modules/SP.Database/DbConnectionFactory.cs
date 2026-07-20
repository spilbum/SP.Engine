using System.Collections.Concurrent;

namespace SP.Database;

public abstract class DbConnectionFactory
{
    private readonly ConcurrentDictionary<string, Entry> _connections = new(StringComparer.Ordinal);

    protected bool HasConnection(string dbKind)
        => !string.IsNullOrWhiteSpace(dbKind) && _connections.ContainsKey(dbKind);

    protected void AddOrUpdate(string dbKind, string connectionString, IDbProvider provider)
    {
        if (string.IsNullOrEmpty(dbKind)) throw new ArgumentNullException(nameof(dbKind));
        if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));
        ArgumentNullException.ThrowIfNull(provider);

        var entry = new Entry(connectionString, provider);
        _connections.AddOrUpdate(dbKind, entry, (_, _) => entry);
    }

    protected DbSession OpenSession(string dbKind)
    {
        if (!_connections.TryGetValue(dbKind, out var entry))
            throw new Exception($"Unknown DbKind={dbKind}");

        var (cs, provider) = entry;

        try
        {
            var raw = provider.CreateConnection(cs)
                      ?? throw new InvalidOperationException("Provider returned null connection.");

            var dbConn = new DbSession(raw, provider);
            dbConn.Open();
            return dbConn;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to open DB. DbKind={dbKind}, CS={cs}", ex);
        }
    }

    protected async Task<DbSession> OpenSessionAsync(string dbKind, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(dbKind, out var entry))
            throw new Exception($"Unknown DbKind={dbKind}");

        var (cs, provider) = entry;

        try
        {
            var raw = provider.CreateConnection(cs)
                      ?? throw new InvalidOperationException("Provider returned null connection.");

            var dbConn = new DbSession(raw, provider);
            await dbConn.OpenAsync(ct).ConfigureAwait(false);
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

    protected string GetConnectionString(string dbKind)
        => _connections.TryGetValue(dbKind, out var entry) ? entry.ConnectionString : string.Empty;

    private sealed record Entry(string ConnectionString, IDbProvider Provider);
}
