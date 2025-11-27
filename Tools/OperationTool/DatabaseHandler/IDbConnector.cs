using SP.Shared.Database;

namespace OperationTool.DatabaseHandler;

public interface IDbConnector
{
    bool CanOpen();
    Task<DbConn> OpenAsync(CancellationToken ct);
}
