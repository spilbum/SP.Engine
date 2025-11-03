using SP.Sample.Common;
using SP.Shared.Database;

namespace SP.Sample.DatabaseHandler;

public sealed class MySqlDbConnector : BaseDbConnector
{
    private readonly MySqlDbProvider _provider = new();
    
    public void Register(DbKind kind, string connectionString)
        => Register(Key(kind), connectionString, _provider);
    
    public DbConn Open(DbKind kind)
        => Open(Key(kind));

    public Task<DbConn> OpenAsync(DbKind kind, CancellationToken ct = default)
        => OpenAsync(Key(kind), ct);
    
    public bool CanOpen(DbKind kind)
        => HasConnection(Key(kind));

    private static string Key(DbKind kind) => kind switch
    {
        DbKind.Game => "Game",
        DbKind.Rank => "Rank",
        _ => kind.ToString()
    };
}
