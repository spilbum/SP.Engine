using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace SP.Database;

public sealed class DbExecutor(DbCommand command, IDbProvider provider) : IDisposable, IAsyncDisposable
{
    private readonly DbCommand _command = command ?? throw new ArgumentNullException(nameof(command));
    private readonly IDbProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _command.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _command.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public DbParameter CreateParameter()
    {
        ThrowIfDisposed();
        return _command.CreateParameter();
    }

    public DbParameter Add(string name, DbType dbType, object? value, int? size = null)
    {
        ThrowIfDisposed();
        var parameter = _command.CreateParameter();
        parameter.ParameterName = _provider.FormatParameterName(name);
        parameter.DbType = dbType;
        if (size.HasValue) parameter.Size = size.Value;
        parameter.Value = value ?? DBNull.Value;
        _command.Parameters.Add(parameter);
        return parameter;
    }

    public void AddParameters<T>(T parameters) where T : class
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(parameters);
        var meta = DbEntityBinder.Get(typeof(T));
        meta.Write(parameters, this);
    }

    public DbParameter AddOut(string name, DbType dbType, int? size = null)
    {
        ThrowIfDisposed();
        var parameter = _command.CreateParameter();
        parameter.ParameterName = _provider.FormatParameterName(name);
        parameter.DbType = dbType;
        parameter.Direction = ParameterDirection.Output;
        if (size.HasValue) parameter.Size = size.Value;
        _command.Parameters.Add(parameter);
        return parameter;
    }

    public DbParameter AddInOut(string name, DbType dbType, object? value, int? size = null)
    {
        ThrowIfDisposed();
        var parameter = _command.CreateParameter();
        parameter.ParameterName = _provider.FormatParameterName(name);
        parameter.DbType = dbType;
        parameter.Direction = ParameterDirection.InputOutput;
        if (size.HasValue) parameter.Size = size.Value;
        parameter.Value = value ?? DBNull.Value;
        _command.Parameters.Add(parameter);
        return parameter;
    }

    public T? GetOut<T>(string name)
    {
        ThrowIfDisposed();
        var value = _command.Parameters[_provider.FormatParameterName(name)].Value;
        return ConvertScalar<T>(value);
    }

    public int ExecuteNonQuery()
    {
        ThrowIfDisposed();
        try
        {
            return _command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error executing SQL command (NonQuery).", ex);
        }
    }

    public async Task<int> ExecuteNonQueryAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        try
        {
            return await _command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error executing SQL command (NonQuery).", ex);
        }
    }

    public T? ExecuteReader<T>() where T : class
    {
        ThrowIfDisposed();
        using var reader = _command.ExecuteReader();
        if (!reader.Read()) return null;

        var meta = DbEntityBinder.Get(typeof(T));
        var ordinals = GetOrdinals(reader, meta.ColumnNames);

        var instance = (T)meta.Create();
        meta.Read(instance, reader, ordinals);
        return instance;
    }

    public async Task<T?> ExecuteReaderAsync<T>(CancellationToken ct = default) where T : class
    {
        ThrowIfDisposed();
        await using var reader = await _command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;

        var meta = DbEntityBinder.Get(typeof(T));
        var ordinals = GetOrdinals(reader, meta.ColumnNames);

        var instance = (T)meta.Create();
        meta.Read(instance, reader, ordinals);
        return instance;
    }

    public List<T> ExecuteReaderList<T>() where T : class
    {
        ThrowIfDisposed();
        using var reader = _command.ExecuteReader();
        if (!reader.HasRows) return [];

        var list = new List<T>();
        var meta = DbEntityBinder.Get(typeof(T));
        var ordinals = GetOrdinals(reader, meta.ColumnNames);

        while (reader.Read())
        {
            var instance = (T)meta.Create();
            meta.Read(instance, reader, ordinals);
            list.Add(instance);
        }

        return list;
    }

    public async Task<List<T>> ExecuteReaderListAsync<T>(CancellationToken ct = default) where T : class
    {
        ThrowIfDisposed();
        await using var reader = await _command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!reader.HasRows) return [];

        var list = new List<T>();
        var meta = DbEntityBinder.Get(typeof(T));
        var ordinals = GetOrdinals(reader, meta.ColumnNames);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var instance = (T)meta.Create();
            meta.Read(instance, reader, ordinals);
            list.Add(instance);
        }

        return list;
    }

    public async IAsyncEnumerable<T> ExecuteReaderEnumerableAsync<T>([EnumeratorCancellation] CancellationToken ct = default) where T : class
    {
        ThrowIfDisposed();
        await using var reader = await _command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!reader.HasRows) yield break;

        var meta = DbEntityBinder.Get(typeof(T));
        var ordinals = GetOrdinals(reader, meta.ColumnNames);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var instance = (T)meta.Create();
            meta.Read(instance, reader, ordinals);
            yield return instance;
        }
    }

    private static int[] GetOrdinals(DbDataReader reader, string[] columnNames)
    {
        var ordinals = new int[columnNames.Length];
        for (var i = 0; i < columnNames.Length; i++)
        {
            ordinals[i] = DbValueMapper.GetOrdinal(reader, columnNames[i]);
        }
        return ordinals;
    }

    public T? ExecuteScalar<T>()
    {
        ThrowIfDisposed();
        var obj = _command.ExecuteScalar();
        return ConvertScalar<T>(obj);
    }

    public async Task<T?> ExecuteScalarAsync<T>(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var obj = await _command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return ConvertScalar<T>(obj);
    }

    private static T? ConvertScalar<T>(object? obj)
    {
        if (obj is null or DBNull) return default;

        var type = typeof(T);
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType.IsEnum)
        {
            if (obj is int or long or byte or short)
                return (T)Enum.ToObject(underlyingType, obj);

            return (T)Enum.Parse(underlyingType, obj.ToString() ?? string.Empty, ignoreCase: true);
        }

        return underlyingType.IsInstanceOfType(obj)
            ? (T)obj
            : (T)Convert.ChangeType(obj, underlyingType);
    }
}
