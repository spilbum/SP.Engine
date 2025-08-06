using System.Collections;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using SP.Common.Accessor;

namespace SP.Database;

public class DatabaseCommand(DbCommand command, IDatabaseEngineAdapter adapter) : IDisposable
    {
        private readonly DbCommand _command = command ?? throw new ArgumentNullException(nameof(command));
        private readonly IDatabaseEngineAdapter _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        private bool _disposed;

        public DbParameter AddWithList(string name, IList list, Type elementType)
        {
            if (!_adapter.SupportsListParameter)
                throw new NotSupportedException("List parameters are not supported by this database engine.");
            return _adapter.AddWithList(_command, name, list, elementType);
        }
        
        public DbParameter AddWithValue(string name, object? value, DbType? dbType = null, int? size = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));

            var param = _command.CreateParameter();
            param.ParameterName = name;
            param.Value = value ?? DBNull.Value;
            
            if (dbType.HasValue)
                param.DbType = dbType.Value;
            
            if (size.HasValue)
                param.Size = size.Value;
            
            _command.Parameters.Add(param);
            return param;
        }

        public void AddWithRecord<TDatabaseRecord>(TDatabaseRecord record)
            where TDatabaseRecord : BaseDatabaseRecord
        {
            record.WriteData(this);
        }

        public T? GetParameterValue<T>(string name)
        {
            var value = _command.Parameters[name].Value;
            if (value == null || value == DBNull.Value)
                return default;

            var targetType = typeof(T);
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlyingType.IsEnum)
                return (T)Enum.Parse(underlyingType, value.ToString() ?? string.Empty);

            return (T)Convert.ChangeType(value, underlyingType);
        }

        public int ExecuteNonQuery()
        {
            try
            {
                return _command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new Exception($"Error executing SQL command: {_command.CommandText}", ex);
            }
        }
        
        public async Task<int> ExecuteNonQueryAsync()
        {
            try
            {
                return await _command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Error executing SQL command: {_command.CommandText}", ex);
            }
        }

        public TInstRecord? ExecuteReaderSingle<TInstRecord>()
            where TInstRecord : BaseDatabaseRecord, new()
        {
            using var reader = _command.ExecuteReader();
            if (!reader.Read()) return null;
            
            var instance = new TInstRecord();
            instance.ReadData(reader);
            return instance;
        }

        public List<TDatabaseRecord> ExecuteReader<TDatabaseRecord>()
            where TDatabaseRecord : BaseDatabaseRecord, new()
        {
            using var reader = _command.ExecuteReader();
            var list = new List<TDatabaseRecord>();
            while (reader.Read())
            {
                var instance = new TDatabaseRecord();
                instance.ReadData(reader);
                list.Add(instance);
            }

            return list;
        }

        public async Task<List<TDatabaseRecord>> ExecuteReaderAsync<TDatabaseRecord>()
            where TDatabaseRecord : BaseDatabaseRecord, new()
        {
            await using var reader = await _command.ExecuteReaderAsync();
            var list = new List<TDatabaseRecord>();
            while (await reader.ReadAsync())
            {
                var instance = new TDatabaseRecord();
                instance.ReadData(reader);
                list.Add(instance);
            }

            return list;
        }

        public List<object> ExecuteReader(Type instanceType)
        {
            using var reader = _command.ExecuteReader();
            var instances = new List<object>();
            var accessor = RuntimeTypeAccessor.GetOrCreate(instanceType);
            
            while (reader.Read())
            {
                var instance = Activator.CreateInstance(instanceType);
                if (null == instance) continue;

                foreach (var member in accessor.Members)
                {
                    if (!reader.HasColumn(member.Name)) continue;
                        
                    var value = reader[member.Name];
                    member.SetValue(instance, value == DBNull.Value ? null : value);
                }

                instances.Add(instance);
            }

            return instances;
        }

        public List<TValue> ExecuteReaderValue<TValue>()
        {
            using var reader = _command.ExecuteReader();
            var list = new List<TValue>();
            while (reader.Read())
            {
                var value = (TValue)Convert.ChangeType(reader.GetValue(0), typeof(TValue));
                list.Add(value);
            }

            return list;
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            
            if (disposing)
                _command.Dispose();
            
            _disposed = true;
        }
    }
