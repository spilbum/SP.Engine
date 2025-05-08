using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;

namespace SP.Database
{
    public sealed class SqlConnection : IDisposable
    {
        private readonly DbConnection _connection;
        private readonly DbNamingService _namingService;
        private DbTransaction _transaction;
        private bool _disposed;
        
        public string ConnectionString => _connection.ConnectionString;

        public SqlConnection(DbConnection connection, DbNamingService namingService)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _namingService = namingService;
        }

        ~SqlConnection()
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

                _connection?.Dispose();
            }
            
            _disposed = true;
        }

        public void Open()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqlConnection), "Cannot open a disposed connection.");

            _connection.Open();
        }

        public async Task OpenAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqlConnection), "Cannot open a disposed connection.");

            await _connection.OpenAsync().ConfigureAwait(false);
        }

        public void BeginTransaction()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqlConnection), "Cannot open a disposed connection.");

            if (_transaction != null)
                throw new InvalidOperationException("Transaction already started.");
            
            _transaction = _connection.BeginTransaction();
        }

        public async Task BeginTransactionAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqlConnection), "Cannot open a disposed connection.");
            
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

        public SqlCommand CreateCommand(string commandText, ECommandType commandType)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqlConnection), "Cannot create command after disposing connection.");

            var command = _connection.CreateCommand();
            command.CommandType = (CommandType)commandType;
            command.CommandText = commandType == ECommandType.StoredProcedure
                ? _namingService.ConvertStoredProcedureName(commandText)
                : commandText;

            return new SqlCommand(command, _namingService);
        }
    }


}
