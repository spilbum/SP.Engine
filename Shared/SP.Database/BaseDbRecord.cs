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

public abstract class BaseDbRecord
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

    public void WriteData(DbCmd command)
    {
        var type = GetType();
        var accessor = RuntimeTypeAccessor.GetOrCreate(type);
        foreach (var member in accessor.Members)
        {
            var value = accessor[this, member.Name];
            command.AddWithValue(member.Name, value);
        }
    }
}
