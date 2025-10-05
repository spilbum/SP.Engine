using System;
using System.Collections;
using System.Collections.Concurrent;
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

        public static void Serialize(object obj, Type type, INetWriter w)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            Write(w, obj, type);
        }

        public static object Deserialize(INetReader r, Type type)
        {
            return Read(r, type);
        }
        
        public static byte[] SerializeObject(object obj, Type type)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            using var buf = new BinaryBuffer(4096);
            var w = new NetWriterBuffer(buf);
            Write(w, obj, type);
            return buf.ToArray();
        }

        public static object DeserializeObject(byte[] data, Type type)
        {
            using var buf = new BinaryBuffer(data.Length);
            buf.Write(data);
            var r = new NetReaderBuffer(buf);
            return Read(r, type);
        }
        
        private static object Read(INetReader r, Type type)
        {
            var dataType = type.GetDataType();
            switch (dataType)
            {
                case DataType.Value:
                {
                    return ReadNullableValue(r, type);
                }

                case DataType.Enum:
                {
                    var ut = Nullable.GetUnderlyingType(type);
                    if (ut is { IsEnum: true })
                    {
                        if (r.ReadBool()) return null;
                    }
                    else
                    {
                        ut = type;
                    }
                    
                    ut = Enum.GetUnderlyingType(ut);
                    object raw;
                    if (ut == typeof(byte)) raw = r.ReadByte();
                    else if (ut == typeof(sbyte)) raw = unchecked((sbyte)r.ReadByte());
                    else if (ut == typeof(short)) raw = r.ReadInt16();
                    else if (ut == typeof(ushort)) raw = r.ReadUInt16();
                    else if (ut == typeof(int)) raw = r.ReadInt32();
                    else if (ut == typeof(uint)) raw = r.ReadUInt32();
                    else if (ut == typeof(long)) raw = r.ReadInt64();
                    else if (ut == typeof(ulong)) raw = r.ReadUInt64();
                    else throw new NotSupportedException($"Unsupported enum type: {ut.FullName}");
                    return Enum.ToObject(ut, raw);
                }

                case DataType.String:
                {
                    return r.ReadString();
                }

                case DataType.Class:
                {
                    if (r.ReadBool()) return null;
                    var instance = Activator.CreateInstance(type);
                    var accessor = RuntimeTypeAccessor.GetOrCreate(type);
                    foreach (var member in accessor.Members)
                    {
                        var val = Read(r, member.Type);
                        if (val != null || member.Type.IsNullable())
                            accessor[instance, member.Name] = val;
                    }

                    return instance;
                }

                case DataType.Array:
                {
                    if (r.ReadBool()) return null;
                    var count = r.ReadInt32();
                    var elemType = GetCachedElementType(type);
                    var array = Array.CreateInstance(elemType!, count);
                    for (var i = 0; i < count; i++)
                        array.SetValue(Read(r, elemType), i);
                    return array;
                }

                case DataType.List:
                {
                    if (r.ReadBool()) return null;
                    var count = r.ReadInt32();
                    var itemType = GetCachedGenericArgs(type)[0];
                    var list = (IList)Activator.CreateInstance(type);
                    for (var i = 0; i < count; i++)
                        list.Add(Read(r, itemType));
                    return list;
                }

                case DataType.Dictionary:
                {
                    if (r.ReadBool()) return null;
                    var dictCount = r.ReadInt32();
                    var args = GetCachedGenericArgs(type);
                    var dict = (IDictionary)Activator.CreateInstance(type);
                    for (var i = 0; i < dictCount; i++)
                    {
                        var key = Read(r, args[0]);
                        var val = Read(r, args[1]);
                        dict.Add(key, val);
                    }

                    return dict;
                }

                case DataType.ByteArray:
                {
                    return r.ReadByteArray();
                }

                case DataType.DateTime:
                {
                    if (r.ReadBool()) return null;
                    var kind = (DateTimeKind)r.ReadByte();
                    var ticks = r.ReadInt64();
                    return new DateTime(ticks, kind);
                }

                default:
                    throw new NotSupportedException($"Unsupported data type: {dataType}");
            }
        }

        private static void Write(INetWriter w, object obj, Type type)
        {
            var dataType = type.GetDataType();
            switch (dataType)
            {
                case DataType.Value:
                {
                    WriteNullableValue(w, obj, type);
                    break;
                }

                case DataType.Enum:
                {
                    var ut = Nullable.GetUnderlyingType(type);
                    if (ut is { IsEnum: true })
                    {
                        var isNull = obj == null;
                        w.WriteBool(isNull);
                        if (isNull) return;
                    }
                    else
                    {
                        ut = type;
                    }

                    ut = Enum.GetUnderlyingType(ut);
                    if (ut == typeof(byte)) w.WriteByte((byte)obj);
                    else if (ut == typeof(sbyte)) w.WriteByte(unchecked((byte)(sbyte)obj));
                    else if (ut == typeof(short)) w.WriteInt16((short)obj);
                    else if (ut == typeof(ushort)) w.WriteUInt16((ushort)obj);
                    else if (ut == typeof(int)) w.WriteInt32((int)obj);
                    else if (ut == typeof(uint)) w.WriteUInt32((uint)obj);
                    else if (ut == typeof(long)) w.WriteInt64((long)obj);
                    else if (ut == typeof(ulong)) w.WriteUInt64((ulong)obj);
                    else throw new NotSupportedException($"Unsupported enum type: {ut.FullName}");
                    break;
                }

                case DataType.String:
                {
                    w.WriteString((string)obj);
                    break;
                }

                case DataType.Class:
                {
                    var isNull = obj == null;
                    w.WriteBool(isNull);
                    if (!isNull)
                    {
                        var accessor = RuntimeTypeAccessor.GetOrCreate(type);
                        foreach (var member in accessor.Members)
                        {
                            var val = accessor[obj, member.Name];
                            Write(w, val, member.Type);
                        }
                    }
                    break;
                }

                case DataType.Array:
                {
                    var arr = obj as Array;
                    var isNull = arr == null;
                    w.WriteBool(isNull);
                    if (!isNull)
                    {
                        var len = arr.Length;
                        w.WriteInt32(len);
                        var elemType = GetCachedElementType(type);
                        for (var i = 0; i < len; i++)
                            Write(w, arr.GetValue(i), elemType);
                    }
                    break;
                }

                case DataType.List:
                {
                    var list = obj as IList;
                    var isNull = list == null;
                    w.WriteBool(isNull);
                    if (!isNull)
                    {
                        w.WriteInt32(list.Count);
                        var itemType = GetCachedGenericArgs(type)[0];
                        foreach (var item in list)
                            Write(w, item, itemType);
                    }
                    break;
                }

                case DataType.Dictionary:
                {
                    var dict = obj as IDictionary;
                    var isNull = dict == null;
                    w.WriteBool(isNull);
                    if (!isNull)
                    {
                        w.WriteInt32(dict.Count);
                        var args = GetCachedGenericArgs(type);
                        foreach (DictionaryEntry e in dict)
                        {
                            Write(w, e.Key, args[0]);
                            Write(w, e.Value, args[1]);
                        }
                    }
                    break;
                }

                case DataType.ByteArray:
                {
                    w.WriteByteArray((byte[])obj);
                    break;
                }

                case DataType.DateTime:
                {
                    var isNull = obj == null;
                    w.WriteBool(isNull);
                    if (!isNull)
                    {
                        var dt = (DateTime)obj;
                        w.WriteByte((byte)dt.Kind);
                        w.WriteInt64(dt.Ticks);
                    }
                    break;
                }

                default:
                    throw new NotSupportedException($"Unsupported data type: {dataType}");
            }
        }

        private static object ReadNullableValue(INetReader r, Type type)
        {
            if (!type.IsNullable())
            {
                if (type == typeof(bool)) return r.ReadBool();
                if (type == typeof(byte)) return r.ReadByte();
                if (type == typeof(sbyte)) return unchecked((sbyte)r.ReadByte());
                if (type == typeof(short)) return r.ReadInt16();
                if (type == typeof(ushort)) return r.ReadUInt16();
                if (type == typeof(int)) return r.ReadInt32();
                if (type == typeof(uint)) return r.ReadUInt32();
                if (type == typeof(long)) return r.ReadInt64();
                if (type == typeof(ulong)) return r.ReadUInt64();
                if (type == typeof(float)) return r.ReadSingle();
                if (type == typeof(double)) return r.ReadDouble();
                if (type == typeof(decimal))
                {
                    var a = r.ReadInt32();
                    var b = r.ReadInt32();
                    var c = r.ReadInt32();
                    var d = r.ReadInt32();
                    return new decimal(new int[] {a, b, c, d});
                }
                throw new NotSupportedException($"Unsupported value type: {type.FullName}");
            }
            
            if (r.ReadBool()) return null;
            var ut = Nullable.GetUnderlyingType(type);
            if (ut == typeof(bool)) return (bool?)r.ReadBool();
            if (ut == typeof(byte)) return (byte?)r.ReadByte();
            if (ut == typeof(sbyte)) return (sbyte?)unchecked((sbyte)r.ReadByte());
            if (ut == typeof(short)) return (short?)r.ReadInt16();
            if (ut == typeof(ushort)) return (ushort?)r.ReadUInt16();
            if (ut == typeof(int)) return (int?)r.ReadInt32();
            if (ut == typeof(uint)) return (uint?)r.ReadUInt32();
            if (ut == typeof(long)) return (long?)r.ReadInt64();
            if (ut == typeof(ulong)) return (ulong?)r.ReadUInt64();
            if (ut == typeof(float)) return (float?)r.ReadSingle();
            if (ut == typeof(double)) return (double?)r.ReadDouble();
            if (ut == typeof(decimal))
            {
                var a = r.ReadInt32();
                var b = r.ReadInt32();
                var c = r.ReadInt32();
                var d = r.ReadInt32();
                return (decimal?)new decimal(new int[] {a, b, c, d});
            }
            throw new NotSupportedException($"Unsupported nullable value type: {ut?.FullName}");
        }
        
        private static void WriteNullableValue(INetWriter w, object obj, Type type)
        {
            if (!type.IsNullable())
            {
                if (type == typeof(bool)) { w.WriteBool((bool)obj); return; }
                if (type == typeof(byte)) { w.WriteByte((byte)obj); return; }
                if (type == typeof(sbyte)) { w.WriteByte(unchecked((byte)(sbyte)obj)); return; }
                if (type == typeof(short)) { w.WriteInt16((short)obj); return; }
                if (type == typeof(ushort)) { w.WriteUInt16((ushort)obj); return; }
                if (type == typeof(int)) { w.WriteInt32((int)obj); return; }
                if (type == typeof(uint)) { w.WriteUInt32((uint)obj); return; }
                if (type == typeof(long)) { w.WriteInt64((long)obj); return; }
                if (type == typeof(ulong)) { w.WriteUInt64((ulong)obj); return; }
                if (type == typeof(float)) { w.WriteSingle((float)obj); return; }
                if (type == typeof(double)) { w.WriteDouble((double)obj); return; }
                if (type == typeof(decimal))
                {
                    var bits = decimal.GetBits((decimal)obj);
                    w.WriteInt32(bits[0]);
                    w.WriteInt32(bits[1]);
                    w.WriteInt32(bits[2]);
                    w.WriteInt32(bits[3]);
                    return;
                }
                throw new NotSupportedException($"Unsupported value type: {type.FullName}");
            }

            var isNull = obj == null;
            w.WriteBool(isNull);
            if (isNull) return;

            var ut = Nullable.GetUnderlyingType(type)!;
            WriteNullableValue(w, Convert.ChangeType(obj, ut), ut);
        }
    }
}
