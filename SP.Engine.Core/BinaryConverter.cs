using System;
using System.Collections;
using System.Collections.Generic;
using SP.Engine.Common.Accessor;

namespace SP.Engine.Core
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
            { typeof(decimal), EDataType.Value }, { typeof(string), EDataType.String }
        };
        
        /// <summary>
        /// 데이터 타입 정보
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
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

        /// <summary>
        /// null 가능 여부 확인
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static bool IsNullable(this Type type)
        {
            return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
        }
    }
    public static class BinaryConverter
    {
        public static T DeserializeObject<T>(byte[] bytes) where T : class, new()
        {
            if (null == bytes)
                throw new ArgumentNullException(nameof(bytes));

            using var buffer = new Buffer(bytes.Length);
            buffer.Write(bytes);
            var obj = Read(buffer, typeof(T));
            return (T)obj;
        }

        public static object DeserializeObject(byte[] bytes, Type type)
        {
            if (null == bytes)
                throw new ArgumentNullException(nameof(bytes));

            using var buffer = new Buffer(bytes.Length);
            buffer.Write(bytes);
            var obj = Read(buffer, type);
            return obj;
        }

        public static byte[] SerializeObject<T>(T obj) where T : class
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            using var buffer = new Buffer();
            Write(buffer, obj, typeof(T));
            return buffer.ToArray();
        }

        public static byte[] SerializeObject(object obj, Type type)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            using var buffer = new Buffer();
            Write(buffer, obj, type);
            return buffer.ToArray();
        }

        private static object Read(Buffer buffer, Type type)
        {
            var dataType = type.GetDataType();
            switch (dataType)
            {
                case EDataType.Value:
                    {
                        if (!type.IsNullable()) 
                            return buffer.ReadObject(type);
                        
                        return buffer.Read<bool>() ? null : buffer.ReadObject(type);
                    }

                case EDataType.Enum:
                    {
                        if (type.IsNullable() && buffer.Read<bool>())
                        {
                            return null;
                        }

                        var underlyingType = Enum.GetUnderlyingType(type);
                        var value = buffer.ReadObject(underlyingType);
                        return Enum.ToObject(type, value);
                    }                    

                case EDataType.String:
                    {
                        return buffer.ReadString();
                    }

                case EDataType.Class:
                    {
                        if (buffer.Read<bool>())
                        {
                            return null;
                        }

                        var instance = Activator.CreateInstance(type);
                        if (null == instance) 
                            throw new InvalidOperationException($"instance is null. type={type.FullName}");
                        
                        var runtimeTypeAccessor = RuntimeTypeAccessor.GetOrCreate(type);
                        var properties = runtimeTypeAccessor.Properties;
                        foreach (var property in properties)
                        {
                            var value = Read(buffer, property.Type);
                            if (value != null)
                                runtimeTypeAccessor[instance, property.Name] = value;
                        }
                        return instance;
                    }

                case EDataType.Array:
                    {
                        var count = buffer.Read<int>();
                        var elementType = type.GetElementType() ?? throw new InvalidOperationException("Array type has no element type.");

                        if (!(Activator.CreateInstance(type, count) is IList list))
                            throw new InvalidOperationException($"Unable to create instance of {type}");

                        for (var i = 0; i < count; i++)
                        {
                            var item = Read(buffer, elementType);
                            list[i] = item;
                        }
                        return list;
                    }

                case EDataType.List:
                    {
                        var count = buffer.Read<int>();
                        var listType = typeof(List<>).MakeGenericType(type.GetGenericArguments());

                        if (!(Activator.CreateInstance(listType) is IList list))
                            throw new InvalidOperationException($"Unable to create instance of {listType}");

                        var itemType = type.GetGenericArguments()[0];
                        for (var i = 0; i < count; i++)
                        {
                            var item = Read(buffer, itemType);
                            list.Add(item);
                        }
                        return list;
                    }

                case EDataType.Dictionary:
                    {
                        var count = buffer.Read<int>();
                        var keyType = type.GetGenericArguments()[0];
                        var valueType = type.GetGenericArguments()[1];

                        if (!(Activator.CreateInstance(type) is IDictionary dictionary))
                            throw new InvalidOperationException($"Unable to create instance of {type}");

                        for (var i = 0; i < count; i++)
                        {
                            var key = Read(buffer, keyType);
                            var value = Read(buffer, valueType);
                            if (key != null)
                                dictionary.Add(key, value);
                        }
                        return dictionary;
                    }
                default:
                    throw new NotSupportedException($"Unsupported data type: {dataType}");
            }
        }

        private static void Write(Buffer buffer, object obj, Type type)
        {
            var dataType = type.GetDataType();
            switch (dataType)
            {
                case EDataType.Value:
                    {
                        if (type.IsNullable())
                        {
                            var isNull = null == obj;
                            buffer.Write(isNull);

                            if (isNull) return;
                        }

                        if (null != obj)
                            buffer.WriteObject(obj);
                    }
                    break;

                case EDataType.Enum:
                    {
                        if (type.IsNullable())
                        {
                            var isNull = null == obj;
                            buffer.Write(isNull);

                            if (isNull) return;
                        }

                        var underlyingValue = Convert.ChangeType(obj, Enum.GetUnderlyingType(type));
                        if (null != underlyingValue)
                            buffer.WriteObject(underlyingValue);
                    }                    
                    break;

                case EDataType.String:
                    {
                        if (obj is string str)
                            buffer.Write(str);
                        else
                            buffer.Write(0);
                    }                    
                    break;

                case EDataType.Class:
                    {
                        var isNull = null == obj;
                        buffer.Write(isNull);

                        if (!isNull)
                        {
                            var runtimeTypeAccessor = RuntimeTypeAccessor.GetOrCreate(type);
                            var properties = runtimeTypeAccessor.Properties;
                            foreach (var property in properties)
                            {
                                var value = runtimeTypeAccessor[obj, property.Name];
                                Write(buffer, value, property.Type);
                            }
                        }
                    }                    
                    break;

                case EDataType.Array:
                    {
                        if (obj is Array array)
                        {
                            buffer.Write(array.Length);
                            foreach (var item in array)
                                Write(buffer, item, type.GetElementType());
                        }
                        else
                        {
                            buffer.Write(0);
                        }
                    }
                    break;

                case EDataType.List:
                    {
                        if (obj is IList list)
                        {
                            buffer.Write(list.Count);
                            foreach (var item in list)
                                Write(buffer, item, type.GetGenericArguments()[0]);
                        }
                        else
                        {
                            buffer.Write(0);
                        }
                    }
                    break;

                case EDataType.Dictionary:
                    {
                        if (obj is IDictionary dictionary)
                        {
                            buffer.Write(dictionary.Count);
                            foreach (DictionaryEntry entry in dictionary)
                            {
                                Write(buffer, entry.Key, type.GetGenericArguments()[0]);
                                Write(buffer, entry.Value, type.GetGenericArguments()[1]);
                            }
                        }
                        else
                        {
                            buffer.Write(0);
                        }
                    }
                    break;

                default:
                    throw new NotSupportedException($"Unsupported data type: {dataType}");
            }
        }
    }


}
