using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Threading.Tasks;
using SP.Common.Accessor;

namespace SP.Database
{
    public enum ECommandType
    {
        None = 0,
        Text = CommandType.Text,
        StoredProcedure = CommandType.StoredProcedure,
    }

    public class SqlCommand : IDisposable
    {
        private readonly DbCommand _command;
        private readonly DbNamingService _namingService;
        private bool _disposed;

        public DbParameterCollection Parameters => _command.Parameters;

        public SqlCommand(DbCommand command, DbNamingService namingService)
        {
            _command = command ?? throw new ArgumentNullException(nameof(command));
            _namingService = namingService;
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
                _command?.Dispose();
            }
            
            _disposed = true;
        }
        
        public void AddWithInstance<TInstRecord>(TInstRecord record)
            where TInstRecord : BaseDbRecord
        {
            record.WriteData(this);
        }

        public DbParameter AddWithValue(string name, object value)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));

            var param = _command.CreateParameter();
            param.ParameterName = _namingService.ConvertParameterName(name);
            param.Value = value;
            _command.Parameters.Add(param);
            return param;
        }

        public DbParameter AddWithList(string name, IList list, Type valueType)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));
            
            var runtimeTypeAccessor = RuntimeTypeAccessor.GetOrCreate(valueType);
            var properties = runtimeTypeAccessor.Properties;

            var jsonBuilder = new StringBuilder();
            jsonBuilder.Append("[");
            
            var firstItem = true;
            foreach (var item in list)
            {
                if (!firstItem)
                    jsonBuilder.Append(",");
                jsonBuilder.Append("{");

                var firstProperty = true;
                foreach (var property in properties)
                {
                    if (!firstProperty)
                        jsonBuilder.Append(",");
                    var value = runtimeTypeAccessor[item, property.Name] ?? "null";

                    if (property.Type == typeof(string) || property.Type == typeof(DateTime))
                        jsonBuilder.Append($"\"{property.Name}\":\"{value}\"");
                    else if (property.Type == typeof(bool))
                        jsonBuilder.Append($"\"{property.Name}\":{value.ToString().ToLower()}");
                    else
                        jsonBuilder.Append($"\"{property.Name}\":{value}");

                    firstProperty = false;
                }
                jsonBuilder.Append("}");
                firstItem = false;
            }

            jsonBuilder.Append("]");

            var param = _command.CreateParameter();
            param.ParameterName = _namingService.ConvertParameterName(name);
            param.Value = jsonBuilder.ToString();
            _command.Parameters.Add(param);
            return param;
        }

        public DbParameter Add(string name, DbType type)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));
        
            var parameter = _command.CreateParameter();
            parameter.ParameterName = _namingService.ConvertParameterName(name);
            parameter.DbType = type;
            _command.Parameters.Add(parameter);
            return parameter;
        }

        public DbParameter Add(string name, DbType type, int size)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));
        
            var parameter = _command.CreateParameter();
            parameter.ParameterName = _namingService.ConvertParameterName(name);
            parameter.DbType = type;
            parameter.Size = size;
            _command.Parameters.Add(parameter);
            return parameter;
        }

        public T GetParameterValue<T>(string name)
        {
            var type = typeof(T);
            var value = _command.Parameters[_namingService.ConvertParameterName(name)].Value;
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

        public TInstRecord ExecuteReaderSingle<TInstRecord>()
            where TInstRecord : BaseDbRecord, new()
        {
            using (var reader = _command.ExecuteReader())
            {
                if (!reader.Read()) return null;
                var instance = new TInstRecord();
                instance.ReadData(reader, _namingService);
                return instance;
            }
        }

        public List<TInstRecord> ExecuteReader<TInstRecord>()
            where TInstRecord : BaseDbRecord, new()
        {
            using (var reader = _command.ExecuteReader())
            {
                var instances = new List<TInstRecord>();
                while (reader.Read())
                {
                    var instance = new TInstRecord();
                    instance.ReadData(reader, _namingService);
                    instances.Add(instance);
                }

                return instances;
            }
        }

        public async Task<List<TInstRecord>> ExecuteReaderAsync<TInstRecord>()
            where TInstRecord : BaseDbRecord, new()
        {
            using (var reader = await _command.ExecuteReaderAsync())
            {
                var instances = new List<TInstRecord>();
                while (await reader.ReadAsync())
                {
                    var instance = new TInstRecord();
                    instance.ReadData(reader, _namingService);
                    instances.Add(instance);
                }

                return instances;
            }
        }

        public List<object> ExecuteReader(Type instanceType)
        {
            using (var reader = _command.ExecuteReader())
            {
                var instances = new List<object>();
                while (reader.Read())
                {
                    var instance = Activator.CreateInstance(instanceType);
                    if (null == instance)
                        return instances;

                    var runtimeTypeAccessor = RuntimeTypeAccessor.GetOrCreate(instanceType);
                    foreach (var property in runtimeTypeAccessor.Properties)
                    {
                        var name = _namingService.ConvertColumnName(property.Name);
                        if (!reader.HasColumn(name))
                            continue;
                        
                        var value = reader[name];
                        property.SetValue(instance, value);
                    }

                    instances.Add(instance);
                }

                return instances;
            }
        }

        public List<TValue> ExecuteReaderValue<TValue>()
        {
            using (var reader = _command.ExecuteReader())
            {
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
}
