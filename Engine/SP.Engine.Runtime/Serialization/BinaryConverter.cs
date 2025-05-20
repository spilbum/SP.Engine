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
        ByteArray,
        DateTime
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

    public static class BinaryConverter
    {
        private static readonly Dictionary<Type, Type> ElementTypeCache = new Dictionary<Type, Type>();
        private static readonly Dictionary<Type, Type[]> GenericArgCache = new Dictionary<Type, Type[]>();

        private static Type GetCachedElementType(Type type)
        {
            if (ElementTypeCache.TryGetValue(type, out var elementType)) return elementType;
            elementType = type.GetElementType()!;
            ElementTypeCache[type] = elementType;
            return elementType;
        }

        private static Type[] GetCachedGenericArgs(Type type)
        {
            if (GenericArgCache.TryGetValue(type, out var args)) return args;
            args = type.GetGenericArguments();
            GenericArgCache[type] = args;
            return args;
        }
        
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
                {
                    return ReadNullable(buffer, type);
                }

                case EDataType.Enum:
                {
                    if (type.IsNullable() && buffer.Read<bool>()) return null;
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
                    if (buffer.Read<bool>()) return null;
                    var instance = Activator.CreateInstance(type);
                    var accessor = RuntimeTypeAccessor.GetOrCreate(type);
                    foreach (var member in accessor.Members)
                    {
                        if (!member.CanRead) continue;
                        var val = Read(buffer, member.Type);
                        if (val != null)
                            accessor[instance, member.Name] = val;
                    }

                    return instance;
                }

                case EDataType.Array:
                {
                    var count = buffer.Read<int>();
                    var elementType = GetCachedElementType(type);
                    var array = Array.CreateInstance(elementType!, count);
                    for (var i = 0; i < count; i++)
                        array.SetValue(Read(buffer, elementType), i);
                    return array;
                }

                case EDataType.List:
                {
                    var count = buffer.Read<int>();
                    var itemType = GetCachedGenericArgs(type)[0];
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
                    for (var i = 0; i < count; i++)
                        list.Add(Read(buffer, itemType));
                    return list;
                }

                case EDataType.Dictionary:
                {
                    var dictCount = buffer.Read<int>();
                    var args = GetCachedGenericArgs(type);
                    
                    var dict = (IDictionary)Activator.CreateInstance(type);
                    for (var i = 0; i < dictCount; i++)
                    {
                        var key = Read(buffer, args[0]);
                        var val = Read(buffer, args[1]);
                        dict.Add(key, val);
                    }

                    return dict;
                }

                case EDataType.ByteArray:
                {
                    var length = buffer.Read<int>();
                    return buffer.ReadBytes(length);
                }

                case EDataType.DateTime:
                {
                    var kind = (DateTimeKind)buffer.Read<byte>();
                    var ticks = buffer.Read<long>();
                    return new DateTime(ticks, kind);
                }

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
                {
                    WriteNullable(buffer, obj, type);
                    break;
                }

                case EDataType.Enum:
                {
                    if (type.IsNullable())
                    {
                        var isNull = obj == null;
                        buffer.Write(isNull);
                        if (isNull) return;
                    }

                    // 실제 타입으로 변환
                    var underlyingValue = Convert.ChangeType(obj, Enum.GetUnderlyingType(type));
                    buffer.WriteObject(underlyingValue);
                    break;
                }

                case EDataType.String:
                {
                    buffer.WriteString((string)obj);
                    break;
                }

                case EDataType.Class:
                {
                    var isNull = obj == null;
                    buffer.Write(isNull);
                    if (!isNull)
                    {
                        var accessor = RuntimeTypeAccessor.GetOrCreate(type);
                        foreach (var member in accessor.Members)
                        {
                            if (!member.CanWrite) continue;
                            var val = accessor[obj, member.Name];
                            Write(buffer, val, member.Type);
                        }
                    }
                    break;
                }

                case EDataType.Array:
                {
                    if (obj is Array array && array.Length > 0)
                    {
                        buffer.Write(array.Length);
                        foreach (var item in array)
                            Write(buffer, item, GetCachedElementType(type));
                    }
                    else
                    {
                        buffer.Write(0);
                    }
                    break;
                }

                case EDataType.List:
                {
                    if (obj is IList list && list.Count > 0)
                    {
                        buffer.Write(list.Count);
                        foreach (var item in list)
                            Write(buffer, item, GetCachedGenericArgs(type)[0]);
                    }
                    else
                    {
                        buffer.Write(0);
                    }
                    break;
                }

                case EDataType.Dictionary:
                {
                    if (obj is IDictionary dictionary)
                    {
                        buffer.Write(dictionary.Count);
                        
                        var args = GetCachedGenericArgs(type);
                        foreach (DictionaryEntry entry in dictionary)
                        {
                            Write(buffer, entry.Key, args[0]);
                            Write(buffer, entry.Value, args[1]);
                        }
                    }
                    else
                    {
                        buffer.Write(0);
                    }
                    break;
                }

                case EDataType.ByteArray:
                {
                    var bytes = (byte[])obj;
                    buffer.Write(bytes.Length);
                    buffer.Write(bytes);
                    break;
                }

                case EDataType.DateTime:
                {
                    var dt = (DateTime)obj;
                    buffer.Write((byte)dt.Kind);
                    buffer.Write(dt.Ticks);
                    break;
                }

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

        public static object DeserializeObject(byte[] data, Type type)
        {
            using var buffer = new BinaryBuffer(data.Length);
            buffer.Write(data);
            return Read(buffer, type);
        }
    }
}
