using System.Data;

namespace SP.Database;

public readonly record struct DbParamSpec(
    DbType DbType,
    int? Size,
    object Value
);

public static class DbParamUtils
{
    public static DbParamSpec ResolveDbParamSpec(Type type, object? value)
    {
        if (value is null) return new DbParamSpec(DbType.Object, null, DBNull.Value);

        var t = Nullable.GetUnderlyingType(type) ?? type;

        switch (Type.GetTypeCode(t))
        {
            case TypeCode.String:
                return new DbParamSpec(DbType.String, null, value);

            case TypeCode.Int32: return new DbParamSpec(DbType.Int32, null, value);
            case TypeCode.Int64: return new DbParamSpec(DbType.Int64, null, value);
            case TypeCode.Single: return new DbParamSpec(DbType.Single, null, value);
            case TypeCode.Double: return new DbParamSpec(DbType.Double, null, value);
            case TypeCode.Boolean: return new DbParamSpec(DbType.Boolean, null, value);
            case TypeCode.Decimal: return new DbParamSpec(DbType.Decimal, null, value);
            case TypeCode.Byte: return new DbParamSpec(DbType.Byte, null, value);
            case TypeCode.Int16: return new DbParamSpec(DbType.Int16, null, value);
            case TypeCode.DateTime: return new DbParamSpec(DbType.DateTime, null, value);
            case TypeCode.UInt32: return new DbParamSpec(DbType.UInt32, null, value);
            case TypeCode.UInt64: return new DbParamSpec(DbType.UInt64, null, value);
            case TypeCode.UInt16: return new DbParamSpec(DbType.UInt16, null, value);
            case TypeCode.SByte: return new DbParamSpec(DbType.SByte, null, value);
            case TypeCode.Char: return new DbParamSpec(DbType.StringFixedLength, 1, value.ToString()!);
            case TypeCode.Object:
                if (t == typeof(byte[]))
                {
                    var b = (byte[])value;
                    return new DbParamSpec(DbType.Binary, b.Length, b);
                }
                if (t == typeof(Guid)) return new DbParamSpec(DbType.Guid, null, value);
                if (t == typeof(DateTimeOffset)) return new DbParamSpec(DbType.DateTimeOffset, null, value);
                return new DbParamSpec(DbType.Object, null, value);
            default:
                return new DbParamSpec(DbType.Object, null, value);
        }
    }
}
