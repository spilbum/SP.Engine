using System.Data;
using System.Data.Common;
using SP.Core.Accessor;

namespace SP.Shared.Database;

public static class DbDataReaderExtensions
{
    public static bool HasColumn(this IDataReader reader, string columnName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
            if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}

public readonly record struct DbParamSpec(
    DbType DbType,
    int? Size,
    object Value
);

public static class DbParamUtils
{
    public static DbParamSpec ResolveDbParamSpec(Type type, object? value)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;

        if (value is null) return new DbParamSpec(DbType.Object, null, DBNull.Value);

        if (t == typeof(string))
        {
            var s = (string)value;
            return new DbParamSpec(DbType.String, s.Length, s);
        }

        if (t == typeof(byte[]))
            return new DbParamSpec(DbType.Binary, ((byte[])value).Length, value);

        if (t == typeof(bool)) return new DbParamSpec(DbType.Boolean, null, value);
        if (t == typeof(byte)) return new DbParamSpec(DbType.Byte, null, value);
        if (t == typeof(sbyte)) return new DbParamSpec(DbType.SByte, null, value);
        if (t == typeof(short)) return new DbParamSpec(DbType.Int16, null, value);
        if (t == typeof(ushort)) return new DbParamSpec(DbType.UInt16, null, value);
        if (t == typeof(int)) return new DbParamSpec(DbType.Int32, null, value);
        if (t == typeof(uint)) return new DbParamSpec(DbType.UInt32, null, value);
        if (t == typeof(long)) return new DbParamSpec(DbType.Int64, null, value);
        if (t == typeof(ulong)) return new DbParamSpec(DbType.UInt64, null, value);
        if (t == typeof(float)) return new DbParamSpec(DbType.Single, null, value);
        if (t == typeof(double)) return new DbParamSpec(DbType.Double, null, value);
        if (t == typeof(decimal)) return new DbParamSpec(DbType.Decimal, null, value);
        if (t == typeof(Guid)) return new DbParamSpec(DbType.Guid, null, value);
        if (t == typeof(DateTimeOffset)) return new DbParamSpec(DbType.DateTimeOffset, null, value);
        if (t == typeof(DateTime)) return new DbParamSpec(DbType.DateTime, null, value);
        return new DbParamSpec(DbType.Object, null, value);
    }
}

public abstract class BaseDbEntity : IDbEntity
{
    public void ReadData(DbDataReader reader)
    {
        var type = GetType();
        var accessor = RuntimeTypeAccessor.GetOrCreate(type);
        foreach (var m in accessor.Members)
        {
            if (!m.CanSet || m.IgnoreSet) continue;

            var name = m.Name;
            if (!reader.HasColumn(name))
                continue;

            var value = reader[name];
            if (value == DBNull.Value) continue;
            m.SetValue(this, value);
        }
    }

    public void WriteData(DbCmd command)
    {
        var type = GetType();
        var accessor = RuntimeTypeAccessor.GetOrCreate(type);
        foreach (var m in accessor.Members)
        {
            if (!m.CanGet || m.IgnoreGet) continue;

            var val = m.GetValue(this);
            var spec = DbParamUtils.ResolveDbParamSpec(m.Type, val);
            command.Add(m.Name, spec.DbType, spec.Value, spec.Size);
        }
    }
}
