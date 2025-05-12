using System.Collections;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using SP.Common.Accessor;

namespace DatabaseHandler
{
    public enum ECommandType
    {
        None = 0,
        Text = CommandType.Text,
        StoredProcedure = CommandType.StoredProcedure,
    }
    
    public class DatabaseCommand(DbCommand command, ESqlType sqlType) : IDisposable
    {
        private readonly DbCommand _command = command ?? throw new ArgumentNullException(nameof(command));
        private bool _disposed;

        public DbParameter AddWithList(string name, IList list, Type type)
        {
            return sqlType switch
            {
                ESqlType.SqlServer => AddWithDataTable(name, list, type),
                ESqlType.MySql => AddWithJson(name, list),
                _ => throw new NotSupportedException($"Unsupported DB type: {sqlType}")
            };
        }
        private SqlParameter AddWithDataTable(string name, IList list, Type type)
        {
            var accessor = RuntimeTypeAccessor.GetOrCreate(type);
            var dataTable = new DataTable(accessor.Name);

            foreach (var property in accessor.Properties)
            {
                dataTable.Columns.Add(property.Name, property.Type).AllowDBNull = property.IsNullable();
            }

            foreach (var item in list)
            {
                var row = dataTable.NewRow();
                foreach (var property in accessor.Properties)
                {
                    var value = accessor[item, property.Name] ?? DBNull.Value;
                    row[property.Name] = value;
                }
                dataTable.Rows.Add(row);
            }
            
            var param = (SqlParameter)_command.CreateParameter();
            param.ParameterName = name;
            param.SqlDbType = SqlDbType.Structured;
            param.Value = dataTable;
            _command.Parameters.Add(param);
            return param;
        }

        private DbParameter AddWithJson(string name, IList list)
        {
            var json = JsonSerializer.Serialize(list);
            var param = _command.CreateParameter();
            param.ParameterName = name;
            param.Value = json;
            _command.Parameters.Add(param);
            return param;
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
                _command.Dispose();
            }
            
            _disposed = true;
        }

        public void AddWithInstance<TInstRecord>(TInstRecord record)
            where TInstRecord : BaseDatabaseRecord
        {
            record.WriteData(this);
        }
        
        public DbParameter AddWithValue(string name, object value)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));

            var param = _command.CreateParameter();
            param.ParameterName = name;
            param.Value = value;
            _command.Parameters.Add(param);
            return param;
        }
        
        public DbParameter Add(string name, DbType type)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));
        
            var parameter = _command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type;
            _command.Parameters.Add(parameter);
            return parameter;
        }

        public DbParameter Add(string name, DbType type, int size)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));
        
            var parameter = _command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type;
            parameter.Size = size;
            _command.Parameters.Add(parameter);
            return parameter;
        }

        public T? GetParameterValue<T>(string name)
        {
            var type = typeof(T);
            var value = _command.Parameters[name].Value;
            if (value == null)
                return default;

            return type.IsEnum
                ? (T)Enum.Parse(type, value.ToString() ?? string.Empty)
                : (T)Convert.ChangeType(value, type);
        }

        public async Task<int> ExecuteNonQueryAsync()
        {
            try
            {
                return await _command.ExecuteNonQueryAsync();
            }
            catch (Exception e)
            {
                throw new Exception($"Error executing SQL command: {_command.CommandText}", e);
            }
        }

        public int ExecuteNonQuery()
        {
            try
            {
                return _command.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                throw new Exception($"Error executing SQL command: {_command.CommandText}", e);
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

        public List<TInstRecord> ExecuteReader<TInstRecord>()
            where TInstRecord : BaseDatabaseRecord, new()
        {
            using var reader = _command.ExecuteReader();
            var instances = new List<TInstRecord>();
            while (reader.Read())
            {
                var instance = new TInstRecord();
                instance.ReadData(reader);
                instances.Add(instance);
            }

            return instances;
        }

        public async Task<List<TInstRecord>> ExecuteReaderAsync<TInstRecord>()
            where TInstRecord : BaseDatabaseRecord, new()
        {
            await using var reader = await _command.ExecuteReaderAsync();
            var instances = new List<TInstRecord>();
            while (await reader.ReadAsync())
            {
                var instance = new TInstRecord();
                instance.ReadData(reader);
                instances.Add(instance);
            }

            return instances;
        }

        public List<object> ExecuteReader(Type instanceType)
        {
            using var reader = _command.ExecuteReader();
            var instances = new List<object>();
            while (reader.Read())
            {
                var instance = Activator.CreateInstance(instanceType);
                if (null == instance)
                    return instances;

                var runtimeTypeAccessor = RuntimeTypeAccessor.GetOrCreate(instanceType);
                foreach (var property in runtimeTypeAccessor.Properties)
                {
                    var name = property.Name;
                    if (!reader.HasColumn(name))
                        continue;
                        
                    var value = reader[name];
                    property.SetValue(instance, value);
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
    }
}
