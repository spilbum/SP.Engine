using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SP.Common.Buffer;
using SP.Common.Accessor;

namespace SP.Engine.Runtime.Serialization
{
    public static class BinaryConverter
    {
        private static readonly ConcurrentDictionary<Type, Type> ElementTypeCache = new ConcurrentDictionary<Type, Type>();
        private static readonly ConcurrentDictionary<Type, Type[]> GenericArgCache = new ConcurrentDictionary<Type, Type[]>();

        private static Type GetCachedElementType(Type type) 
            => ElementTypeCache.GetOrAdd(type, t => t.GetElementType());

        private static Type[] GetCachedGenericArgs(Type type)
            => GenericArgCache.GetOrAdd(type, t => t.GetGenericArguments());
        
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
                case DataType.Value:
                {
                    return ReadNullable(buffer, type);
                }

                case DataType.Enum:
                {
                    if (type.IsNullableEnum())
                    {
                        var isNull = buffer.Read<bool>();
                        if (isNull) return null;
                    }
                    
                    var enumType = Nullable.GetUnderlyingType(type) ?? type;
                    var underlying = Enum.GetUnderlyingType(enumType);
                    var raw = buffer.ReadObject(underlying);
                    return Enum.ToObject(enumType, raw);
                }

                case DataType.String:
                {
                    return buffer.ReadString();
                }

                case DataType.Class:
                {
                    if (buffer.Read<bool>()) return null;
                    
                    var instance = Activator.CreateInstance(type);
                    var accessor = RuntimeTypeAccessor.GetOrCreate(type);
                    foreach (var member in accessor.Members)
                    {
                        var val = Read(buffer, member.Type);
                        if (val != null || member.Type.IsNullable())
                            accessor[instance, member.Name] = val;
                    }

                    return instance;
                }

                case DataType.Array:
                {
                    var count = buffer.Read<int>();
                    var elementType = GetCachedElementType(type);
                    var array = Array.CreateInstance(elementType!, count);
                    for (var i = 0; i < count; i++)
                        array.SetValue(Read(buffer, elementType), i);
                    return array;
                }

                case DataType.List:
                {
                    var count = buffer.Read<int>();
                    var itemType = GetCachedGenericArgs(type)[0];
                    var list = (IList)Activator.CreateInstance(type);
                    for (var i = 0; i < count; i++)
                        list.Add(Read(buffer, itemType));
                    return list;
                }

                case DataType.Dictionary:
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

                case DataType.ByteArray:
                {
                    if (buffer.Read<bool>()) 
                        return null;
                    
                    var length = buffer.Read<int>();
                    return buffer.ReadBytes(length);
                }

                case DataType.DateTime:
                {
                    if (buffer.Read<bool>()) 
                        return null;
                    
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
                case DataType.Value:
                {
                    WriteNullable(buffer, obj, type);
                    break;
                }

                case DataType.Enum:
                {
                    if (type.IsNullableEnum())
                    {
                        var isNull = obj == null;
                        buffer.Write(isNull);
                        if (isNull) return;
                        type = Nullable.GetUnderlyingType(type)!;
                    }

                    // 실제 타입으로 변환
                    var underlying = Enum.GetUnderlyingType(type);
                    var raw = Convert.ChangeType(obj, underlying);
                    buffer.WriteObject(raw);
                    break;
                }

                case DataType.String:
                {
                    buffer.WriteString((string)obj);
                    break;
                }

                case DataType.Class:
                {
                    var isNull = obj == null;
                    buffer.Write(isNull);
                    if (!isNull)
                    {
                        var accessor = RuntimeTypeAccessor.GetOrCreate(type);
                        foreach (var member in accessor.Members)
                        {
                            var val = accessor[obj, member.Name];
                            Write(buffer, val, member.Type);
                        }
                    }
                    break;
                }

                case DataType.Array:
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

                case DataType.List:
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

                case DataType.Dictionary:
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

                case DataType.ByteArray:
                {
                    var isNull = obj == null;
                    buffer.Write(isNull);

                    if (!isNull)
                    {
                        var bytes = (byte[])obj;
                        buffer.Write(bytes.Length);
                        buffer.Write(bytes);   
                    }
                    break;
                }

                case DataType.DateTime:
                {
                    var isNull = obj == null;
                    buffer.Write(isNull);

                    if (!isNull)
                    {
                        var dt = (DateTime)obj;
                        buffer.Write((byte)dt.Kind);
                        buffer.Write(dt.Ticks);
                    }
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
