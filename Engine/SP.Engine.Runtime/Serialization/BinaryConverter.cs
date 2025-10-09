using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SP.Common.Accessor;

namespace SP.Engine.Runtime.Serialization
{
    public static class BinaryConverter
    {

        private static readonly ConcurrentDictionary<Type, SerializerPair> Cache =
            new ConcurrentDictionary<Type, SerializerPair>();

        private class SerializerPair
        {
            public delegate object ReadFn(ref NetReader r);

            public delegate void WriteFn(ref NetWriter w, object value);

            public ReadFn Reader { get; }
            public WriteFn Writer { get; }

            public SerializerPair(ReadFn reader, WriteFn writer)
            {
                Reader = reader;
                Writer = writer;
            }
        }

        public static T Deserialize<T>(ref NetReader r) => (T)Deserialize(ref r, typeof(T));
        public static object Deserialize(ref NetReader r, Type type) => GetOrBuild(type).Reader(ref r);

        public static void Serialize<T>(ref NetWriter w, T value) => Serialize(ref w, typeof(T), value);
        public static void Serialize(ref NetWriter w, Type type, object value) => GetOrBuild(type).Writer(ref w, value);

        private static SerializerPair GetOrBuild(Type type) => Cache.GetOrAdd(type, Build);

        private static SerializerPair Build(Type t)
        {
            if (TryBuildPrimitive(t, out var p)) return p;

            if (t == typeof(string)) return BuildString();
            if (t == typeof(byte[])) return BuildByteArray();
            
            if (t.IsEnum) return BuildEnum(t);
        
            var ut = Nullable.GetUnderlyingType(t);
            if (ut != null) return BuildNullable(ut);
            
            if (t.IsArray) return BuildArray(t);
            if (TryBuildList(t, out p)) return p;
            if (TryBuildDictionary(t, out p)) return p;
            
            return t == typeof(DateTime) ? BuildDateTime() : BuildPoco(t);
        }

        private static bool TryBuildPrimitive(Type t, out SerializerPair pair)
        {
            pair = null;
            if (t == typeof(bool))
            {
                pair = Generate((ref NetReader r) => r.ReadBool(),
                    (ref NetWriter w, object v) => w.WriteBool((bool)v));
                return true;
            }

            if (t == typeof(byte))
            {
                pair = Generate((ref NetReader r) => r.ReadByte(),
                    (ref NetWriter w, object v) => w.WriteByte((byte)v));
                return true;
            }

            if (t == typeof(sbyte))
            {
                pair = Generate((ref NetReader r) => (sbyte)r.ReadByte(),
                    (ref NetWriter w, object v) => w.WriteByte(unchecked((byte)(sbyte)v)));
                return true;
            }

            if (t == typeof(short))
            {
                pair = Generate((ref NetReader r) => r.ReadInt16(),
                    (ref NetWriter w, object v) => w.WriteInt16((short)v));
                return true;
            }

            if (t == typeof(ushort))
            {
                pair = Generate((ref NetReader r) => r.ReadUInt16(),
                    (ref NetWriter w, object v) => w.WriteUInt16((ushort)v));
                return true;
            }

            if (t == typeof(int))
            {
                pair = Generate((ref NetReader r) => r.ReadInt32(),
                    (ref NetWriter w, object v) => w.WriteInt32((int)v));
                return true;
            }

            if (t == typeof(uint))
            {
                pair = Generate((ref NetReader r) => r.ReadUInt32(),
                    (ref NetWriter w, object v) => w.WriteUInt32((uint)v));
                return true;
            }

            if (t == typeof(long))
            {
                pair = Generate((ref NetReader r) => r.ReadInt64(),
                    (ref NetWriter w, object v) => w.WriteInt64((long)v));
                return true;
            }

            if (t == typeof(ulong))
            {
                pair = Generate((ref NetReader r) => r.ReadUInt64(),
                    (ref NetWriter w, object v) => w.WriteUInt64((ulong)v));
                return true;
            }

            if (t == typeof(float))
            {
                pair = Generate((ref NetReader r) => r.ReadSingle(),
                    (ref NetWriter w, object v) => w.WriteSingle((float)v));
                return true;
            }

            if (t == typeof(double))
            {
                pair = Generate((ref NetReader r) => r.ReadDouble(),
                    (ref NetWriter w, object v) => w.WriteDouble((double)v));
                return true;
            }

            if (t == typeof(decimal))
            {
                pair = Generate(
                    (ref NetReader r) =>
                    {
                        var a = r.ReadInt32();
                        var b = r.ReadInt32();
                        var c = r.ReadInt32();
                        var d = r.ReadInt32();
                        return new decimal(new[] { a, b, c, d });
                    },
                    (ref NetWriter w, object v) =>
                    {
                        var bits = decimal.GetBits((decimal)v);
                        w.WriteInt32(bits[0]);
                        w.WriteInt32(bits[1]);
                        w.WriteInt32(bits[2]);
                        w.WriteInt32(bits[3]);
                    });
                return true;
            }

            return false;

            SerializerPair Generate(SerializerPair.ReadFn r, SerializerPair.WriteFn w)
                => new SerializerPair(r, w);
        }

        private static SerializerPair BuildString()
        {
            return new SerializerPair(Read, Write);

            object Read(ref NetReader r)
            {
                return r.ReadString();
            }

            void Write(ref NetWriter w, object v)
            {
                w.WriteString((string)v);
            }
        }

        private static SerializerPair BuildByteArray()
        {
            return new SerializerPair(Read, Write);

            void Write(ref NetWriter writer, object value)
            {
                var bytes = (byte[])value ?? Array.Empty<byte>();
                writer.WriteBytes(bytes);
            }

            object Read(ref NetReader r)
            {
                var s = r.ReadBytes();
                var arr = new byte[s.Length];
                s.CopyTo(arr);
                return arr;
            }
        }

        private static SerializerPair BuildEnum(Type enumType)
        {
            var underlying = Enum.GetUnderlyingType(enumType);
            var uSer = GetOrBuild(underlying);

            return new SerializerPair(Read, Write);

            void Write(ref NetWriter writer, object value)
            {
                var raw = Convert.ChangeType(value, underlying);
                uSer.Writer(ref writer, raw);
            }

            object Read(ref NetReader r)
            {
                var raw = uSer.Reader(ref r);
                return Enum.ToObject(enumType, raw);
            }
        }

        private static SerializerPair BuildNullable(Type type)
        {
            var ser = GetOrBuild(type);

            return new SerializerPair(Read, Write);

            object Read(ref NetReader r)
            {
                var has = r.ReadBool();
                return !has ? null : ser.Reader(ref r);
            }

            void Write(ref NetWriter writer, object value)
            {
                if (value == null)
                {
                    writer.WriteBool(false);
                    return;
                }

                writer.WriteBool(true);
                ser.Writer(ref writer, value);
            }
        }

        private static SerializerPair BuildArray(Type arrayType)
        {
            var elemType = arrayType.GetElementType();
            if (elemType == null) throw new InvalidOperationException("elemType is null");
            var elemSer = GetOrBuild(elemType);

            return new SerializerPair(Read, Write);

            object Read(ref NetReader r)
            {
                var n = (int)r.ReadVarUInt();
                var arr = Array.CreateInstance(elemType, n);
                for (var i = 0; i < n; i++)
                    arr.SetValue(elemSer.Reader(ref r), i);
                return arr;
            }

            void Write(ref NetWriter writer, object value)
            {
                var arr = (Array)value ?? Array.CreateInstance(elemType, 0);
                writer.WriteVarUInt((uint)arr.Length);
                for (var i = 0; i < arr.Length; i++)
                    elemSer.Writer(ref writer, arr.GetValue(i));
            }
        }

        private static bool TryBuildList(Type t, out SerializerPair pair)
        {
            pair = null;
            if (!t.IsGenericType) return false;
            var gen = t.GetGenericTypeDefinition();
            if (gen != typeof(List<>)) return false;
            
            var elemType = t.GetGenericArguments()[0];
            var elemSer = GetOrBuild(elemType);

            pair = new SerializerPair(Read, Write);
            return true;

            void Write(ref NetWriter w, object v)
            {
                var list = (IList)v ?? (IList)Activator.CreateInstance(t);
                w.WriteVarUInt((uint)list.Count);
                foreach (var it in list) elemSer.Writer(ref w, it);
            }

            object Read(ref NetReader r)
            {
                var n = (int)r.ReadVarUInt();
                var list = (IList)Activator.CreateInstance(t);
                for (var i = 0; i < n; i++)
                    list.Add(elemSer.Reader(ref r));
                return list;
            }
        }

        private static bool TryBuildDictionary(Type t, out SerializerPair pair)
        {
            pair = null;
            if (!t.IsGenericType) return false;
            var gen = t.GetGenericTypeDefinition();
            if (gen != typeof(Dictionary<,>)) return false;
            
            var args = t.GetGenericArguments();
            var kSer = GetOrBuild(args[0]);
            var vSer = GetOrBuild(args[1]);

            pair = new SerializerPair(Read, Write);
            return true;
            
            object Read(ref NetReader r)
            {
                var n = (int)r.ReadVarUInt();
                var dict = (IDictionary)Activator.CreateInstance(t);
                for (var i = 0; i < n; i++)
                {
                    var k = kSer.Reader(ref r);
                    var v = vSer.Reader(ref r);
                    dict.Add(k, v);
                }
                return dict;
            }

            void Write(ref NetWriter w, object v)
            {
                var dict = (IDictionary)v ?? (IDictionary)Activator.CreateInstance(t);
                w.WriteVarUInt((uint)dict.Count);
                foreach (DictionaryEntry e in dict)
                {
                    kSer.Writer(ref w, e.Key);
                    vSer.Writer(ref w, e.Value);
                }
            }
        }

        private static SerializerPair BuildDateTime()
        {
            return new SerializerPair(Read, Write);
            void Write(ref NetWriter w, object v) => w.WriteInt64(((DateTime)v).ToUniversalTime().Ticks);
            object Read(ref NetReader r) => new DateTime(r.ReadInt64(), DateTimeKind.Utc);
        }

        private static SerializerPair BuildPoco(Type t)
        {
            var accessor = RuntimeTypeAccessor.GetOrCreate(t);
            var members = accessor.Members;
            
            return new SerializerPair(Read, Write);

            object Read(ref NetReader r)
            {
                var obj = Activator.CreateInstance(t);
                foreach (var m in members)
                {
                    var ser = GetOrBuild(m.Type);
                    var val = ser.Reader(ref r);
                    m.SetValue(obj, val);
                }
                return obj;
            }

            void Write(ref NetWriter w, object v)
            {
                foreach (var m in members)
                {
                    var ser = GetOrBuild(m.Type);
                    ser.Writer(ref w, m.GetValue(v));
                }
            }
        }
    }
}
