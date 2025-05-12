using System;
using System.Collections;
using System.Collections.Generic;
using SP.Common.Buffer;
using SP.Common.Accessor;

namespace SP.Engine.Runtime.Serialization
{
	public enum EDataType
	{
		None = 0,
		Value,
		Enum,
		Class,
		String,
		Array,
		List,
		Dictionary,
		Max
	}

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
            { typeof(DateTime), EDataType.Value }
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

    public static class BinaryConverter
    {
        private static object ReadNullable(BinaryBuffer buffer, Type type)
        {
            if (!type.IsNullable())
                return buffer.ReadObject(type);

            var isNull = buffer.Read<bool>();
            if (isNull) return null;

            var realType = type.GetUnderlyingTypeOrSelf();
            return buffer.ReadObject(realType);
        }

        private static void WriteNullable(BinaryBuffer buffer, object value, Type type)
        {
            if (!type.IsNullable())
            {
                buffer.WriteObject(value);
                return;
            }

            var isNull = value == null;
            buffer.Write(isNull);

            if (!isNull)
            {
                buffer.WriteObject(value);
            }
        }

        private static object Read(BinaryBuffer buffer, Type type)
        {
            var dataType = type.GetDataType();
            switch (dataType)
            {
                case EDataType.Value:
                    return ReadNullable(buffer, type);

                case EDataType.Enum:
                    if (type.IsNullable() && buffer.Read<bool>()) return null;
                    var underlyingType = Enum.GetUnderlyingType(type);
                    var value = buffer.ReadObject(underlyingType);
                    return Enum.ToObject(type, value);

                case EDataType.String:
                    return buffer.ReadString();

                case EDataType.Class:
                    if (buffer.Read<bool>()) return null;
                    var instance = Activator.CreateInstance(type);
                    var accessor = RuntimeTypeAccessor.GetOrCreate(type);
                    foreach (var property in accessor.Properties)
                    {
                        var val = Read(buffer, property.Type);
                        if (val != null)
                            accessor[instance, property.Name] = val;
                    }

                    return instance;

                case EDataType.Array:
                    var count = buffer.Read<int>();
                    var elementType = type.GetElementType();
                    var array = Array.CreateInstance(elementType!, count);
                    for (var i = 0; i < count; i++)
                        array.SetValue(Read(buffer, elementType), i);
                    return array;

                case EDataType.List:
                    var listCount = buffer.Read<int>();
                    var itemType = type.GetGenericArguments()[0];
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
                    for (var i = 0; i < listCount; i++)
                        list.Add(Read(buffer, itemType));
                    return list;

                case EDataType.Dictionary:
                    var dictCount = buffer.Read<int>();
                    var keyType = type.GetGenericArguments()[0];
                    var valueType = type.GetGenericArguments()[1];
                    var dict = (IDictionary)Activator.CreateInstance(type);
                    for (var i = 0; i < dictCount; i++)
                    {
                        var key = Read(buffer, keyType);
                        var val = Read(buffer, valueType);
                        dict.Add(key, val);
                    }

                    return dict;

                default:
                    throw new NotSupportedException($"Unsupported data type: {dataType}");
            }
        }

        private static void Write(BinaryBuffer buffer, object obj, Type type)
        {
            var dataType = type.GetDataType();
            switch (dataType)
            {
                case EDataType.Value:
                    WriteNullable(buffer, obj, type);
                    break;

                case EDataType.Enum:
                    if (type.IsNullable())
                    {
                        var isNull = obj == null;
                        buffer.Write(isNull);
                        if (isNull) return;
                    }

                    var underlyingValue = Convert.ChangeType(obj, Enum.GetUnderlyingType(type));
                    buffer.WriteObject(underlyingValue);
                    break;

                case EDataType.String:
                    if (obj is string str)
                        buffer.Write(str);
                    else
                        buffer.Write(0);
                    break;

                case EDataType.Class:
                    var isNullClass = obj == null;
                    buffer.Write(isNullClass);
                    if (!isNullClass)
                    {
                        var accessor = RuntimeTypeAccessor.GetOrCreate(type);
                        foreach (var prop in accessor.Properties)
                        {
                            var val = accessor[obj, prop.Name];
                            Write(buffer, val, prop.Type);
                        }
                    }

                    break;

                case EDataType.Array:
                    if (obj is Array array)
                    {
                        buffer.Write(array.Length);
                        foreach (var item in array)
                            Write(buffer, item, type.GetElementType());
                    }
                    else buffer.Write(0);

                    break;

                case EDataType.List:
                    if (obj is IList list)
                    {
                        buffer.Write(list.Count);
                        foreach (var item in list)
                            Write(buffer, item, type.GetGenericArguments()[0]);
                    }
                    else buffer.Write(0);

                    break;

                case EDataType.Dictionary:
                    if (obj is IDictionary dictionary)
                    {
                        buffer.Write(dictionary.Count);
                        foreach (DictionaryEntry entry in dictionary)
                        {
                            Write(buffer, entry.Key, type.GetGenericArguments()[0]);
                            Write(buffer, entry.Value, type.GetGenericArguments()[1]);
                        }
                    }
                    else buffer.Write(0);

                    break;

                default:
                    throw new NotSupportedException($"Unsupported data type: {dataType}");
            }
        }

        public static byte[] SerializeObject(object obj, Type type)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            using var buffer = new BinaryBuffer();
            Write(buffer, obj, type);
            return buffer.ToArray();
        }

        public static byte[] SerializeObject<T>(T obj) where T : class
        {
            return SerializeObject(obj, typeof(T));
        }

        public static object DeserializeObject(byte[] bytes, Type type)
        {
            using var buffer = new BinaryBuffer(bytes.Length);
            buffer.Write(bytes);
            return Read(buffer, type);
        }

        public static T DeserializeObject<T>(byte[] bytes) where T : class, new()
        {
            return (T)DeserializeObject(bytes, typeof(T));
        }
    }
}
