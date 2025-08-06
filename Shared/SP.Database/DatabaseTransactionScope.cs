namespace SP.Database;

public sealed class DatabaseTransactionScope : IDisposable
{
    private readonly DatabaseConnection _connection;
    private bool _committed;
    private bool _disposed;

    public DatabaseTransactionScope(DatabaseConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _connection.BeginTransaction();
    }

    public void Commit()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DatabaseTransactionScope));
        
        if (_committed)
            throw new InvalidOperationException("Transaction has already been committed");
        
        _connection.Commit();
        _committed = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (!_committed)
        {
            try
            {   
                _connection.Rollback();
            }
            catch
            {
                // ignored
            }
        }

        _disposed = true;
    }
}
