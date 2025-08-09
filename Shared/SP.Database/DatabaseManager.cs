
namespace SP.Database;

public static class DatabaseManager
{
    private class DbConnectionInfo
    {
        public string DbType { get; }
        public string? PrivateConnectionString { get; }
        public string? PublicConnectionString { get; }

        public DbConnectionInfo(
            string dbType,
            string? privateConnectionString,
            string? publicConnectionString)
        {
            if (string.IsNullOrEmpty(dbType))
                throw new ArgumentException($"Invalid DbType={dbType}", nameof(dbType));

            if (string.IsNullOrEmpty(privateConnectionString) && string.IsNullOrEmpty(publicConnectionString))
                throw new ArgumentException("ConnectionString is empty or null.");

            DbType = dbType;
            PrivateConnectionString = privateConnectionString;
            PublicConnectionString = publicConnectionString;
        }
    }
    
    private static readonly Dictionary<string, (DbConnectionInfo, IDatabaseProvider)> Infos = new();

    public static void Register(string dbType, IDatabaseProvider provider, string privateConnectionString, string publicConnectionString)
    {
        if (Infos.ContainsKey(dbType))
            throw new InvalidOperationException($"DatabaseType '{dbType}' is already registered.");
        
        var info = new DbConnectionInfo(dbType, privateConnectionString, publicConnectionString);
        Infos[dbType] = (info, provider);
    }

    public static DbConn Open(string dbType, bool isPublic = false)
    {
        if (!Infos.TryGetValue(dbType, out var info))
            throw new Exception($"Invalid DbType={dbType}");
                
        var cs = ResolveConnectionString(info.Item1, isPublic);
        
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
                $"Failed to open DB connection. DbType={dbType}, IsPublic={isPublic}, ConnectionString={cs}", ex);
        }
    }

    public static async Task<DbConn> OpenAsync(string dbType, bool isPublic = false, CancellationToken ct = default)
    {
        if (!Infos.TryGetValue(dbType, out var info))
            throw new Exception($"Invalid DbType={dbType}");
        
        var cs = ResolveConnectionString(info.Item1, isPublic);

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
                $"Failed to open database connection. Type={dbType}, IsPublic={isPublic}, ConnectionString={cs}", ex);
        }
    }

    private static string ResolveConnectionString(DbConnectionInfo info, bool isPublic)
    {
        var cs = isPublic ? info.PublicConnectionString : info.PrivateConnectionString;
        if (string.IsNullOrEmpty(cs))
            throw new ArgumentException(
                $"ConnectionString is not set. DbType='{info.DbType}', IsPublic={isPublic}");
        return cs;
    }
}


