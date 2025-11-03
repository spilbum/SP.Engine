using System.Data;
using System.Data.Common;

namespace SP.Shared.Database;

public sealed class DbConn(DbConnection connection, IDbProvider provider) : IDisposable
{
    private readonly DbConnection _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    private bool _disposed;
    private DbTransaction? _transaction;

    public string ConnectionString => _connection.ConnectionString;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~DbConn()
    {
        Dispose(false);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            try
            {
                _transaction?.Rollback();
            }
            catch (Exception)
            {
                /* ignored */
            }

            _transaction = null;
            _connection.Dispose();
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DbConn), "Connection is already disposed.");
    }

    public void Open()
    {
        ThrowIfDisposed();
        _connection.Open();
    }

    public async Task OpenAsync()
    {
        ThrowIfDisposed();
        await _connection.OpenAsync().ConfigureAwait(false);
    }

    public void BeginTransaction()
    {
        ThrowIfDisposed();

        if (_transaction != null)
            throw new InvalidOperationException("Transaction already started.");

        _transaction = _connection.BeginTransaction();
    }

    public async Task BeginTransactionAsync()
    {
        ThrowIfDisposed();

        if (_transaction != null)
            throw new InvalidOperationException("Transaction already started.");

        _transaction = await _connection.BeginTransactionAsync();
    }

    public void Commit()
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to commit.");

        _transaction.Commit();
        _transaction = null;
    }

    public async Task CommitAsync()
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to commit.");

        await _transaction.CommitAsync();
        _transaction = null;
    }

    public void Rollback()
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to rollback.");

        _transaction.Rollback();
        _transaction = null;
    }

    public async Task RollbackAsync()
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to rollback.");

        await _transaction.RollbackAsync();
        _transaction = null;
    }

    public DbCmd CreateCommand()
    {
        ThrowIfDisposed();

        var command = _connection.CreateCommand();
        return new DbCmd(command, provider);
    }

    public DbCmd CreateCommand(CommandType commandType, string commandText)
    {
        ThrowIfDisposed();

        var command = _connection.CreateCommand();
        command.CommandType = commandType;
        command.CommandText = commandText;
        return new DbCmd(command, provider);
    }
}
