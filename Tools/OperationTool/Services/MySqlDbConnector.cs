using SP.Shared.Database;

namespace OperationTool.Services;

public sealed class MySqlDbConnector : BaseDbConnector, IDbConnector
{
    private const string DbKind = "Resource";
    
    public DbConn Open()
        => Open(DbKind);
    
    public async Task<DbConn> OpenAsync(CancellationToken ct)
        => await OpenAsync(DbKind, ct);

    public bool CanOpen()
        => HasConnection(DbKind);
    
    public void AddOrUpdate(string connectionString)
        => AddOrUpdate(DbKind, connectionString, new MySqlDbProvider());
}
