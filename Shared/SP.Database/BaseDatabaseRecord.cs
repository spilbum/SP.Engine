using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using SP.Common.Accessor;

namespace SP.Database;

public static class DbDataReaderExtensions
{
    public static bool HasColumn(this IDataReader reader, string columnName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

public abstract class BaseDatabaseRecord
{
    public void ReadData(DbDataReader reader)
    {
        var type = GetType();
        var accessor = RuntimeTypeAccessor.GetOrCreate(type);
        foreach (var member in accessor.Members)
        {
            var name = member.Name;
            if (!reader.HasColumn(name))
                continue;

            var value = reader[name];
            member.SetValue(this, value == DBNull.Value ? null : value);
        }
    }

    public void WriteData(DatabaseCommand command)
    {
        var type = GetType();
        var accessor = RuntimeTypeAccessor.GetOrCreate(type);
        foreach (var member in accessor.Members)
        {
            var value = accessor[this, member.Name];
            if (value is IList { Count: > 0 } list)
            {
                var elementType = GetElementType(member.Type);
                if (elementType == null)
                    throw new InvalidOperationException($"Unsupported list type '{member.Type}'");
                command.AddWithList(member.Name, list, elementType);
            }
            else
            {
                command.AddWithValue(member.Name, value);
            }
        }
    }

    private static Type? GetElementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (type.IsGenericType && typeof(IEnumerable<>).IsAssignableFrom(type.GetGenericTypeDefinition())) 
            return type.GetGenericArguments()[0];
        if (typeof(IEnumerable).IsAssignableFrom(type) && type.IsGenericType)
            return type.GetGenericArguments()[0];
        return null;
    }
}
