using SP.Shared.Database;

namespace ResourceServer.DatabaseHandler;

public sealed class MySqlDbConnector : BaseDbConnector
{
    private const string DbKind = "Resource";
    private readonly MySqlDbProvider _provider = new();
    
    public MySqlDbConnector(string connectionString)
        => Register(DbKind, connectionString, _provider);
    
    public DbConn Open()
        => Open(DbKind);

    public Task<DbConn> OpenAsync(CancellationToken ct)
        => OpenAsync(DbKind, ct);
    
    public bool CanOpen()
        => HasConnection(DbKind);
}
