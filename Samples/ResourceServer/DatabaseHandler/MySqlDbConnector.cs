using SP.Shared.Database;

namespace ResourceServer.DatabaseHandler;

public interface IDbConnector
{
    Task<DbConn> OpenAsync(CancellationToken ct);
}

public sealed class MySqlDbConnector : BaseDbConnector, IDbConnector
{
    private const string DbKind = "Resource";
    private readonly MySqlDbProvider _provider = new();
    
    public MySqlDbConnector(string connectionString)
        => AddOrUpdate(DbKind, connectionString, _provider);
    
    public Task<DbConn> OpenAsync(CancellationToken ct)
        => OpenAsync(DbKind, ct);
}
