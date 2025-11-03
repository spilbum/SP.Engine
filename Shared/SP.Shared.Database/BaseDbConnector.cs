namespace SP.Shared.Database;

public abstract class BaseDbConnector
{
    private readonly Dictionary<string, (ConnectionInfo, IDbProvider)> _infos = new();

    protected bool HasConnection(string dbKind)
        => _infos.ContainsKey(dbKind);

    protected void AddConnection(
        string dbKind,
        string connectionString,
        IDbProvider provider)
    {
        if (_infos.ContainsKey(dbKind))
            throw new InvalidOperationException($"{dbKind} is already registered.");

        var info = new ConnectionInfo(dbKind, connectionString);
        _infos[dbKind] = (info, provider);
    }

    protected DbConn Open(string dbKind)
    {
        if (!_infos.TryGetValue(dbKind, out var info))
            throw new Exception($"Invalid DbKind={dbKind}");

        var cs = info.Item1.ConnectionString;

        try
        {
            var conn = info.Item2.CreateConnection(cs)
                       ?? throw new InvalidOperationException("Factory returned null connection.");

            var dbConn = new DbConn(conn, info.Item2);
            dbConn.Open();
            return dbConn;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to open DB connection. DbKind={dbKind}, ConnectionString={cs}", ex);
        }
    }

    protected async Task<DbConn> OpenAsync(string dbKind, CancellationToken ct = default)
    {
        if (!_infos.TryGetValue(dbKind, out var info))
            throw new Exception($"Invalid DbKind={dbKind}");

        var cs = info.Item1.ConnectionString;

        try
        {
            var conn = info.Item2.CreateConnection(cs)
                       ?? throw new InvalidOperationException("Factory returned null connection.");

            var dbConn = new DbConn(conn, info.Item2);
            await dbConn.OpenAsync().WaitAsync(ct).ConfigureAwait(false);
            return dbConn;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to open database connection. DbKind={dbKind}, ConnectionString={cs}", ex);
        }
    }

    private class ConnectionInfo
    {
        public ConnectionInfo(
            string dbKind,
            string connectionString)
        {
            if (string.IsNullOrEmpty(dbKind))
                throw new ArgumentException($"Invalid DbKind={dbKind}", nameof(dbKind));

            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentException("ConnectionString is empty or null.");

            DbKind = dbKind;
            ConnectionString = connectionString;
        }

        public string DbKind { get; }
        public string ConnectionString { get; }
    }
}
