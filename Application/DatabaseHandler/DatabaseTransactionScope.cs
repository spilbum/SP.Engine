namespace DatabaseHandler;

public sealed class DatabaseTransactionScope : IDisposable
{
    private readonly DatabaseConnection _connection;
    private bool _committed;

    public DatabaseTransactionScope(DatabaseConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _connection.BeginTransaction();
    }

    public void Commit()
    {
        _connection.Commit();
        _committed = true;
    }

    public void Dispose()
    {
        if (_committed) return;
        
        try
        {   
            _connection.Rollback();
        }
        catch
        {
            // ignored
        }
    }
}
