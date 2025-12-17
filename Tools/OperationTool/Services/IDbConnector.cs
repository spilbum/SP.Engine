using SP.Shared.Database;

namespace OperationTool.Services;

public interface IDbConnector
{
    bool CanOpen();
    DbConn Open();
    Task<DbConn> OpenAsync(CancellationToken ct);
    void AddOrUpdate(string connectionString);
}
