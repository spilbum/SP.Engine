using System;
using System.Collections;
using System.Collections.Generic;

namespace SP.Engine.Runtime.Serialization
{
    public static class BinaryConverterExtensions
    {
        private static readonly Dictionary<Type, DataType> TypeCache = new Dictionary<Type, DataType>
        {
            { typeof(bool), DataType.Value }, { typeof(byte), DataType.Value },
            { typeof(sbyte), DataType.Value }, { typeof(char), DataType.Value },
            { typeof(short), DataType.Value }, { typeof(ushort), DataType.Value },
            { typeof(int), DataType.Value }, { typeof(uint), DataType.Value },
            { typeof(long), DataType.Value }, { typeof(ulong), DataType.Value },
            { typeof(float), DataType.Value }, { typeof(double), DataType.Value },
            { typeof(decimal), DataType.Value }, { typeof(string), DataType.String },
            { typeof(DateTime), DataType.DateTime }, { typeof(byte[]), DataType.ByteArray }
        };

        public static bool IsNullableEnum(this Type t)
        {
            var u = Nullable.GetUnderlyingType(t);
            return u is { IsEnum: true };
        }

        public static bool IsNullableDateTime(this Type t)
        {
            var u = Nullable.GetUnderlyingType(t);
            return u != null && u == typeof(DateTime);
        }
        
        public static DataType GetDataType(this Type type)
        {
            if (type == null) return DataType.None;
            if (TypeCache.TryGetValue(type, out var dataType)) return dataType;
            if (type.IsNullableDateTime()) return DataType.DateTime;
            if (type.IsArray) return DataType.Array;
            if (type.IsEnum || type.IsNullableEnum()) return DataType.Enum;
            if (typeof(IList).IsAssignableFrom(type)) return DataType.List;
            if (typeof(IDictionary).IsAssignableFrom(type)) return DataType.Dictionary;
            return type.IsClass ? DataType.Class : DataType.None;
        }

        public static bool IsNullable(this Type type)
        {
            return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
        }

        public static Type GetUnderlyingTypeOrSelf(this Type type)
        {
            return Nullable.GetUnderlyingType(type) ?? type;
        }
    }
}
