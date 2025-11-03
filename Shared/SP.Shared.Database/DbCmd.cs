using System.Data;
using System.Data.Common;
using SP.Core.Accessor;

namespace SP.Shared.Database;

public class DbCmd(DbCommand command, IDbProvider provider) : IDisposable
{
    private readonly DbCommand _command = command ?? throw new ArgumentNullException(nameof(command));
    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public DbParameter CreateParameter()
    {
        return _command.CreateParameter();
    }

    public DbParameter Add(string name, DbType dbType, object? value, int? size = null)
    {
        var param = _command.CreateParameter();
        param.ParameterName = provider.FormatParameterName(name);
        param.DbType = dbType;
        if (size.HasValue) param.Size = size.Value;
        param.Value = value ?? DBNull.Value;
        _command.Parameters.Add(param);
        return param;
    }

    // public DbParameter AddWithValue(string name, object? value, DbType? dbType = null, int? size = null)
    // {
    //     if (string.IsNullOrEmpty(name))
    //         throw new ArgumentException("Parameter name cannot be null or empty.", nameof(name));
    //
    //     var param = _command.CreateParameter();
    //     param.ParameterName = provider.FormatParameterName(name);
    //     param.Value = value ?? DBNull.Value;
    //
    //     if (dbType.HasValue)
    //         param.DbType = dbType.Value;
    //
    //     if (size.HasValue)
    //         param.Size = size.Value;
    //
    //     _command.Parameters.Add(param);
    //     return param;
    // }

    public void AddWithEntity<TDbEntity>(TDbEntity entity)
        where TDbEntity : IDbEntity
    {
        entity.WriteData(this);
    }

    public DbParameter AddOut(string name, DbType dbType, int? size = null)
    {
        var p = _command.CreateParameter();
        p.ParameterName = provider.FormatParameterName(name);
        p.DbType = dbType;
        p.Direction = ParameterDirection.Output;
        if (size.HasValue) p.Size = size.Value;
        _command.Parameters.Add(p);
        return p;
    }

    public DbParameter AddInOut(string name, DbType dbType, object? value, int? size = null)
    {
        var p = _command.CreateParameter();
        p.ParameterName = provider.FormatParameterName(name);
        p.DbType = dbType;
        p.Direction = ParameterDirection.InputOutput;
        if (size.HasValue) p.Size = size.Value;
        p.Value = value ?? DBNull.Value;
        _command.Parameters.Add(p);
        return p;
    }

    public T? GetOut<T>(DbParameter parameter)
    {
        var value = parameter.Value;
        if (value == DBNull.Value) return default;
        return (T)Convert.ChangeType(value, typeof(T))!;
    }

    public T? GetOut<T>(string name)
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

    public TDbEntity? ExecuteReader<TDbEntity>()
        where TDbEntity : BaseDbEntity, new()
    {
        using var reader = _command.ExecuteReader();
        if (!reader.Read()) return null;

        var instance = new TDbEntity();
        instance.ReadData(reader);
        return instance;
    }

    public List<TDbEntity> ExecuteReaderList<TDbEntity>()
        where TDbEntity : BaseDbEntity, new()
    {
        using var reader = _command.ExecuteReader();
        var list = new List<TDbEntity>();
        while (reader.Read())
        {
            var instance = new TDbEntity();
            instance.ReadData(reader);
            list.Add(instance);
        }

        return list;
    }

    public async Task<List<TDbEntity>> ExecuteReaderListAsync<TDbEntity>()
        where TDbEntity : BaseDbEntity, new()
    {
        await using var reader = await _command.ExecuteReaderAsync();
        var list = new List<TDbEntity>();
        while (await reader.ReadAsync())
        {
            var instance = new TDbEntity();
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

    public TValue ExecuteReaderValue<TValue>()
    {
        using var reader = _command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("Reader not found.");

        var value = (TValue)Convert.ChangeType(reader.GetValue(0), typeof(TValue));
        return value;
    }

    public List<TValue> ExecuteReaderValues<TValue>()
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

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
            _command.Dispose();

        _disposed = true;
    }
}
