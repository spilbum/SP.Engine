using System.Data;
using System.Data.Common;

namespace SP.Database;

public sealed class DbSession(DbConnection connection, IDbProvider provider) : IDisposable, IAsyncDisposable
{
    private readonly DbConnection _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    private readonly IDbProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private bool _disposed;
    private DbTransaction? _transaction;

    public string ConnectionString => _connection.ConnectionString;

    public void Dispose()
    {
        if (_disposed) return;

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

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_transaction != null)
        {
            try
            {
                await _transaction.RollbackAsync().ConfigureAwait(false);
            }
            catch { /* ignored */ }
            _transaction = null;
        }

        if (_connection is IAsyncDisposable disposable)
            await disposable.DisposeAsync().ConfigureAwait(false);
        else
            _connection.Dispose();

        _disposed = true;
    }



    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DbSession), "Connection is already disposed.");
    }

    public void Open()
    {
        ThrowIfDisposed();
        _connection.Open();
    }

    public async Task OpenAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _connection.OpenAsync(ct).ConfigureAwait(false);
    }

    public void BeginTransaction()
    {
        ThrowIfDisposed();

        if (_transaction != null)
            throw new InvalidOperationException("Transaction already started.");

        _transaction = _connection.BeginTransaction();
    }

    public async Task BeginTransactionAsync(CancellationToken ct)
    {
        ThrowIfDisposed();

        if (_transaction != null)
            throw new InvalidOperationException("Transaction already started.");

        _transaction = await _connection.BeginTransactionAsync(ct).ConfigureAwait(false);
    }

    public void Commit()
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to commit.");

        _transaction.Commit();
        _transaction = null;
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to commit.");

        await _transaction.CommitAsync(ct).ConfigureAwait(false);
        _transaction = null;
    }

    public void Rollback()
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to rollback.");

        _transaction.Rollback();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to rollback.");

        await _transaction.RollbackAsync(ct).ConfigureAwait(false);
        _transaction = null;
    }

    public DbExecutor CreateExecutor(string commandText, CommandType commandType = CommandType.Text)
    {
        ThrowIfDisposed();
        var command = _connection.CreateCommand();
        command.CommandType = commandType;
        command.CommandText = commandText;
        command.Transaction = _transaction;
        return new DbExecutor(command, _provider);
    }
}
