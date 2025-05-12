using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using SP.Common.Accessor;

namespace DatabaseHandler
{
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
            var runtimeTypeAccessor = RuntimeTypeAccessor.GetOrCreate(type);
            foreach (var property in runtimeTypeAccessor.Properties)
            {
                var name = property.Name;
                if (!reader.HasColumn(name))
                    continue;
                
                var value = reader[name];
                property.SetValue(this, value == DBNull.Value ? null : value);
            }
        }

        public void WriteData(DatabaseCommand command)
        {
            var type = GetType();
            var runtimeTypeAccessor = RuntimeTypeAccessor.GetOrCreate(type);
            foreach (var property in runtimeTypeAccessor.Properties)
            {
                var value = runtimeTypeAccessor[this, property.Name];
                if (property.Type.IsGenericType && property.Type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    if (value is not IList list || list.Count == 0)
                        continue;
                        
                    var valueType = property.Type.GetGenericArguments()[0];
                    command.AddWithList(property.Name, list, valueType);
                }
                else
                {
                    command.AddWithValue(property.Name, value);
                }
            }
        }
    }
}
