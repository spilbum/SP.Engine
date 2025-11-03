using SP.Sample.Common;
using SP.Shared.Database;

namespace SP.Sample.DatabaseHandler;

public class MySqlDbConnector : BaseDbConnector
{
    private readonly MySqlProvider _provider = new();
    
    public void Add(DbKind kind, string connectionString)
        => AddConnection(kind.ToString(), connectionString, _provider);
    
    public DbConn Open(DbKind kind)
        => Open(kind.ToString());
    
    public bool CanOpen(DbKind kind)
        => HasConnection(kind.ToString());
}
