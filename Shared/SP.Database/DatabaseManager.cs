
namespace SP.Database;

public class DatabaseManager(IDatabaseConnectionFactory factory, IDatabaseEngineAdapter adapter)
{
    private readonly Dictionary<string, DatabaseConfig> _configs = new();
    private readonly IDatabaseConnectionFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));

    public void AddConfig(DatabaseConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(config.DatabaseType))
            throw new ArgumentException("DatabaseType must be provided.", nameof(config));
        
        if (!_configs.TryAdd(config.DatabaseType, config))
            throw new ArgumentException($"DatabaseType '{config.DatabaseType}' is already registered.");
    }

    private DatabaseConfig RequireConfig(string databaseType)
    {
        if (string.IsNullOrWhiteSpace(databaseType))
            throw new ArgumentException("DatabaseType must be provided.", nameof(databaseType));

        if (!_configs.TryGetValue(databaseType, out var config))
            throw new ArgumentException($"Unknown databaseType '{databaseType}'. {string.Join(", ", _configs.Keys)}");
        return config;
    }

    public DatabaseConnection Open(string databaseType, bool isPublic = false)
    {
        var config = RequireConfig(databaseType);
        var cs = ResolveConnectionString(config, isPublic);
        
        try
        {
            var dbConn = _factory.GetConnection(cs)
                ?? throw new InvalidOperationException("Factory returned null connection.");
            var connection = new DatabaseConnection(dbConn, adapter);
            connection.Open();
            return connection;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Failed to open database connection. Type={databaseType}, IsPublic={isPublic}, ConnectionString={cs}\r\nexception={e.Message}\r\nstackTrace={e.StackTrace}");
        }
    }

    public async Task<DatabaseConnection> OpenAsync(string databaseType, bool isPublic = false, CancellationToken ct = default)
    {
        var config = RequireConfig(databaseType);
        var cs = ResolveConnectionString(config, isPublic);

        try
        {
            var dbConn = _factory.GetConnection(cs)
                         ?? throw new InvalidOperationException("Factory returned null connection.");
            var connection = new DatabaseConnection(dbConn, adapter);
            await connection.OpenAsync().WaitAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Failed to open database connection. Type={databaseType}, IsPublic={isPublic}, ConnectionString={cs}\r\nexception={e.Message}\r\nstackTrace={e.StackTrace}");
        }
    }

    private static string ResolveConnectionString(DatabaseConfig config, bool isPublic)
    {
        var cs = isPublic ? config.PublicConnectionString : config.PrivateConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
            throw new ArgumentException(
                $"ConnectionString is not set. DatabaseType='{config.DatabaseType}', IsPublic={isPublic}");
        return cs;
    }
    
}
