using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SP.Core.Accessor;

namespace SP.Engine.Runtime.Serialization
{
    public static class NetSerializer
    {
        private static readonly ConcurrentDictionary<Type, SerializerPair> Cache =
            new ConcurrentDictionary<Type, SerializerPair>();

        public static T Deserialize<T>(ref NetReader r)
        {
            return (T)Deserialize(ref r, typeof(T));
        }

        public static object Deserialize(ref NetReader r, Type type)
        {
            return GetOrBuild(type).Reader(ref r);
        }

        public static void Serialize<T>(ref NetWriter w, T value)
        {
            Serialize(ref w, typeof(T), value);
        }

        public static void Serialize(ref NetWriter w, Type type, object value)
        {
            GetOrBuild(type).Writer(ref w, value);
        }

        private static SerializerPair GetOrBuild(Type type)
        {
            return Cache.GetOrAdd(type, Build);
        }

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
            return t == typeof(DateTime) ? BuildDateTime() : BuildDataClass(t);
        }

        private static bool TryBuildPrimitive(Type t, out SerializerPair pair)
        {
            pair = null;
            if (t == typeof(bool))
            {
                pair = new SerializerPair(
                    (ref NetReader r) => r.ReadBool(),
                    (ref NetWriter w, object v) => w.WriteBool((bool)v));
                return true;
            }

            if (t == typeof(byte))
            {
                pair = new SerializerPair(
                    (ref NetReader r) => r.ReadByte(),
                    (ref NetWriter w, object v) => w.WriteByte((byte)v));
                return true;
            }

            if (t == typeof(sbyte))
            {
                pair = new SerializerPair(
                    (ref NetReader r) => (sbyte)r.ReadByte(),
                    (ref NetWriter w, object v) => w.WriteByte(unchecked((byte)(sbyte)v)));
                return true;
            }

            if (t == typeof(short))
            {
                pair = new SerializerPair(
                    (ref NetReader r) => r.ReadInt16(),
                    (ref NetWriter w, object v) => w.WriteInt16((short)v));
                return true;
            }

            if (t == typeof(ushort))
            {
                pair = new SerializerPair(
                    (ref NetReader r) => r.ReadUInt16(),
                    (ref NetWriter w, object v) => w.WriteUInt16((ushort)v));
                return true;
            }

            if (t == typeof(int))
            {
                pair = new SerializerPair(
                    (ref NetReader r) => r.ReadInt32(),
                    (ref NetWriter w, object v) => w.WriteInt32((int)v));
                return true;
            }

            if (t == typeof(uint))
            {
                pair = new SerializerPair(
                    (ref NetReader r) => r.ReadUInt32(),
                    (ref NetWriter w, object v) => w.WriteUInt32((uint)v));
                return true;
            }

            if (t == typeof(long))
            {
                pair = new SerializerPair(
                    (ref NetReader r) => r.ReadInt64(),
                    (ref NetWriter w, object v) => w.WriteInt64((long)v));
                return true;
            }

            if (t == typeof(ulong))
            {
                pair = new SerializerPair(
                    (ref NetReader r) => r.ReadUInt64(),
                    (ref NetWriter w, object v) => w.WriteUInt64((ulong)v));
                return true;
            }

            if (t == typeof(float))
            {
                pair = new SerializerPair(
                    (ref NetReader r) => r.ReadSingle(),
                    (ref NetWriter w, object v) => w.WriteSingle((float)v));
                return true;
            }

            if (t == typeof(double))
            {
                pair = new SerializerPair(
                    (ref NetReader r) => r.ReadDouble(),
                    (ref NetWriter w, object v) => w.WriteDouble((double)v));
                return true;
            }

            if (t == typeof(decimal))
            {
                pair = new SerializerPair(
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
        }

        private static SerializerPair BuildString()
        {
            return new SerializerPair(Read, Write);

            object Read(ref NetReader r)
            {
                var has = r.ReadBool();
                return has ? r.ReadString() : null;
            }

            void Write(ref NetWriter w, object v)
            {
                if (v == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                w.WriteString((string)v);
            }
        }

        private static SerializerPair BuildByteArray()
        {
            return new SerializerPair(Read, Write);

            void Write(ref NetWriter w, object value)
            {
                var bytes = (byte[])value;
                if (bytes == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                w.WriteBytes(bytes);
            }

            object Read(ref NetReader r)
            {
                var has = r.ReadBool();
                if (!has) return null;
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

            void Write(ref NetWriter w, object v)
            {
                if (v == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                var raw = Convert.ChangeType(v, underlying);
                uSer.Writer(ref w, raw);
            }

            object Read(ref NetReader r)
            {
                var has = r.ReadBool();
                if (!has) return null;

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

            void Write(ref NetWriter w, object value)
            {
                if (value == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                ser.Writer(ref w, value);
            }
        }

        private static SerializerPair BuildArray(Type arrayType)
        {
            var elemType = arrayType.GetElementType() ?? throw new InvalidOperationException("elemType is null");
            var elemSer = GetOrBuild(elemType);

            return new SerializerPair(Read, Write);

            object Read(ref NetReader r)
            {
                var has = r.ReadBool();
                if (!has) return null;

                var n = (int)r.ReadVarUInt();
                var arr = Array.CreateInstance(elemType, n);
                for (var i = 0; i < n; i++)
                    arr.SetValue(elemSer.Reader(ref r), i);
                return arr;
            }

            void Write(ref NetWriter w, object value)
            {
                var arr = (Array)value;
                if (arr == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                w.WriteVarUInt((uint)arr.Length);
                for (var i = 0; i < arr.Length; i++)
                    elemSer.Writer(ref w, arr.GetValue(i));
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
                var list = (IList)v;
                if (list == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                w.WriteVarUInt((uint)list.Count);
                foreach (var it in list) elemSer.Writer(ref w, it);
            }

            object Read(ref NetReader r)
            {
                var has = r.ReadBool();
                if (!has) return null;

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
                var has = r.ReadBool();
                if (!has) return null;

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
                var dict = (IDictionary)v;
                if (dict == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
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

            void Write(ref NetWriter w, object v)
            {
                if (v == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                var dt = ((DateTime)v).ToUniversalTime();
                w.WriteInt64(dt.Ticks);
            }

            object Read(ref NetReader r)
            {
                var has = r.ReadBool();
                if (!has) return null;

                var ticks = r.ReadInt64();
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        private static SerializerPair BuildDataClass(Type t)
        {
            var accessor = RuntimeTypeAccessor.GetOrCreate(t);
            var members = accessor.Members;

            var memberSerializers = new SerializerPair[members.Count];
            for (var i = 0; i < members.Count; i++)
                memberSerializers[i] = GetOrBuild(members[i].Type);

            return new SerializerPair(Read, Write);

            object Read(ref NetReader r)
            {
                var has = r.ReadBool();
                if (!has) return null;

                var obj = Activator.CreateInstance(t);
                for (var i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    if (!m.CanSet || m.IgnoreSet) continue;

                    var ser = memberSerializers[i];
                    var val = ser.Reader(ref r);
                    m.SetValue(obj, val);
                }

                return obj;
            }

            void Write(ref NetWriter w, object v)
            {
                if (v == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                for (var i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    if (!m.CanGet || m.IgnoreGet) continue;

                    var ser = memberSerializers[i];
                    var val = m.GetValue(v);
                    ser.Writer(ref w, val);
                }
            }
        }

        private class SerializerPair
        {
            public delegate object ReadFn(ref NetReader r);

            public delegate void WriteFn(ref NetWriter w, object value);

            public SerializerPair(ReadFn reader, WriteFn writer)
            {
                Reader = reader;
                Writer = writer;
            }

            public ReadFn Reader { get; }
            public WriteFn Writer { get; }
        }
    }
}
