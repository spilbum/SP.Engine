using System.Data;
using System.Data.Common;
using SP.Common.Accessor;

namespace SP.Database;

public class DbCmd(DbCommand command, IDatabaseProvider provider) : IDisposable
{
    private readonly DbCommand _command = command ?? throw new ArgumentNullException(nameof(command));
    private bool _disposed;

    public DbParameter CreateParameter() => _command.CreateParameter();

    public DbParameter Add(string name, DbType dbType)
    {
        var param = _command.CreateParameter();
        param.ParameterName = provider.FormatParameterName(name);
        param.DbType = dbType;
        _command.Parameters.Add(param);
        return param;
    }
    
    public DbParameter AddWithValue(string name, object? value, DbType? dbType = null, int? size = null)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));

        var param = _command.CreateParameter();
        param.ParameterName = provider.FormatParameterName(name);
        param.Value = value ?? DBNull.Value;

        if (dbType.HasValue)
            param.DbType = dbType.Value;

        if (size.HasValue)
            param.Size = size.Value;

        _command.Parameters.Add(param);
        return param;
    }

    public void AddWithRecord<TDbRecord>(TDbRecord record)
        where TDbRecord : BaseDbRecord
    {
        record.WriteData(this);
    }
    
    public T? GetParameterValue<T>(string name)
    {
        var value = _command.Parameters[provider.FormatParameterName(name)].Value;
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

    public TDbRecord? ExecuteReader<TDbRecord>()
        where TDbRecord : BaseDbRecord, new()
    {
        using var reader = _command.ExecuteReader();
        if (!reader.Read()) return null;

        var instance = new TDbRecord();
        instance.ReadData(reader);
        return instance;
    }

    public List<TBaseDbRecord> ExecuteReaderList<TBaseDbRecord>()
        where TBaseDbRecord : BaseDbRecord, new()
    {
        using var reader = _command.ExecuteReader();
        var list = new List<TBaseDbRecord>();
        while (reader.Read())
        {
            var instance = new TBaseDbRecord();
            instance.ReadData(reader);
            list.Add(instance);
        }

        return list;
    }

    public async Task<List<TDbRecord>> ExecuteReaderListAsync<TDbRecord>()
        where TDbRecord : BaseDbRecord, new()
    {
        await using var reader = await _command.ExecuteReaderAsync();
        var list = new List<TDbRecord>();
        while (await reader.ReadAsync())
        {
            var instance = new TDbRecord();
            instance.ReadData(reader);
            list.Add(instance);
        }

        return list;
    }

    public List<object> ExecuteReaderList(Type instanceType)
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
