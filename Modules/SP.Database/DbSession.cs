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

        if (_transaction != null)
        {
            try
            {
                _transaction.Rollback();
            }
            catch
            {
                /* ignored */
            }
            finally
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }

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
            catch
            {
                /* ignored */
            }
            finally
            {
                await _transaction.DisposeAsync().ConfigureAwait(false);
                _transaction = null;
            }
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_transaction != null)
            throw new InvalidOperationException("Transaction already started.");

        _transaction = await _connection.BeginTransactionAsync(ct).ConfigureAwait(false);
    }

    public void Commit()
    {
        ThrowIfDisposed();

        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to commit.");

        try
        {
            _transaction.Commit();
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to commit.");

        try
        {
            await _transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }
    }

    public void Rollback()
    {
        ThrowIfDisposed();

        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to rollback.");

        try
        {
            _transaction.Rollback();
        }
        finally
        {
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to rollback.");

        try
        {
            await _transaction.RollbackAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }
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
