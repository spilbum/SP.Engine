using System;
using System.Collections;
using System.Collections.Generic;

namespace SP.Engine.Runtime.Serialization
{
    public static class BinaryConverterExtensions
    {
        private static readonly Dictionary<Type, EDataType> TypeCache = new Dictionary<Type, EDataType>
        {
            { typeof(bool), EDataType.Value }, { typeof(byte), EDataType.Value },
            { typeof(sbyte), EDataType.Value }, { typeof(char), EDataType.Value },
            { typeof(short), EDataType.Value }, { typeof(ushort), EDataType.Value },
            { typeof(int), EDataType.Value }, { typeof(uint), EDataType.Value },
            { typeof(long), EDataType.Value }, { typeof(ulong), EDataType.Value },
            { typeof(float), EDataType.Value }, { typeof(double), EDataType.Value },
            { typeof(decimal), EDataType.Value }, { typeof(string), EDataType.String },
            { typeof(DateTime), EDataType.DateTime }, { typeof(byte[]), EDataType.ByteArray }
        };

        public static EDataType GetDataType(this Type type)
        {
            if (type == null) return EDataType.None;
            if (TypeCache.TryGetValue(type, out var dataType)) return dataType;
            if (type.IsArray) return EDataType.Array;
            if (type.IsEnum) return EDataType.Enum;
            if (typeof(IList).IsAssignableFrom(type)) return EDataType.List;
            if (typeof(IDictionary).IsAssignableFrom(type)) return EDataType.Dictionary;
            return type.IsClass ? EDataType.Class : EDataType.None;
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
