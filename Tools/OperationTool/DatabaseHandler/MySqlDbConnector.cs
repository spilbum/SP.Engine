using OperationTool.Storage;
using SP.Shared.Database;

namespace OperationTool.DatabaseHandler;

public sealed class MySqlDbConnector : BaseDbConnector, IDbConnector
{
    private const string DbKind = "Resource";
    private readonly ISettingsProvider _settingsProvider;
    private readonly IDbProvider _dbProvider;

    public MySqlDbConnector(ISettingsProvider settingsProvider, IDbProvider dbProvider)
    {
        _settingsProvider = settingsProvider;
        _dbProvider = dbProvider;
    }

    public async Task<DbConn> OpenAsync(CancellationToken ct)
    {
        var connectionString = BuildConnectionString();

        var current = GetConnectionString(DbKind);
        if (!string.IsNullOrEmpty(current) && current != connectionString)
        {
            Unregister(DbKind);
        }

        if (!HasConnection(DbKind))
        {
            Register(DbKind, connectionString, _dbProvider);
        }
        
        return await OpenAsync(DbKind, ct);
    }

    public bool CanOpen()
    {
        var s = _settingsProvider.Current.Database;
        return !string.IsNullOrWhiteSpace(s.Host)
               && !string.IsNullOrWhiteSpace(s.Database)
               && !string.IsNullOrWhiteSpace(s.User);
    }

    private string BuildConnectionString()
    {
        var s = _settingsProvider.Current.Database;
        return $"Server={s.Host};Port={s.Port};Database={s.Database};User Id={s.User};Password={s.Password}";
    }
}
