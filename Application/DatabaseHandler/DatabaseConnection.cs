using System.Data;
using System.Data.Common;

namespace DatabaseHandler
{
    public sealed class DatabaseConnection(DbConnection connection, ESqlType sqlType) : IDisposable
    {
        private readonly DbConnection _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        private DbTransaction? _transaction;
        private bool _disposed;
        
        public string ConnectionString => _connection.ConnectionString;

        ~DatabaseConnection()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _transaction?.Rollback();
                _transaction = null;             

                _connection.Dispose();
            }
            
            _disposed = true;
        }

        public DatabaseTransactionScope BeginTransactionScope()
        {
            return new DatabaseTransactionScope(this);
        }

        public void Open()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DatabaseConnection), "Cannot open a disposed connection.");

            _connection.Open();
        }

        public async Task OpenAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DatabaseConnection), "Cannot open a disposed connection.");

            await _connection.OpenAsync().ConfigureAwait(false);
        }

        public void BeginTransaction()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DatabaseConnection), "Cannot open a disposed connection.");

            if (_transaction != null)
                throw new InvalidOperationException("Transaction already started.");
            
            _transaction = _connection.BeginTransaction();
        }

        public async Task BeginTransactionAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DatabaseConnection), "Cannot open a disposed connection.");
            
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

        public DatabaseCommand CreateCommand(ECommandType commandType, string commandText)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DatabaseConnection), "Cannot create command after disposing connection.");

            var command = _connection.CreateCommand();
            command.CommandType = (CommandType)commandType;
            command.CommandText = commandText;
            return new DatabaseCommand(command, sqlType);
        }
    }


}
