using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SP.Core.Serialization
{
    public static class NetSerializer
    {
        private static readonly ConcurrentDictionary<Type, SerializerPair> Cache =
            new ConcurrentDictionary<Type, SerializerPair>();

        public static void Serialize<T>(NetWriter w, T value)
            => Serialize(w, typeof(T), value);

        public static void Serialize(NetWriter w, Type type, object value)
            => GetOrBuild(type).Writer(ref w, value);
        
        public static T Deserialize<T>(ref NetReader r)
            => (T)Deserialize(ref r, typeof(T));

        public static object Deserialize(ref NetReader r, Type type)
            => GetOrBuild(type).Reader(ref r);

        private static SerializerPair GetOrBuild(Type type)
            => Cache.GetOrAdd(type, Build);

        private static SerializerPair Build(Type t)
        {
            if (t == typeof(string)) return BuildString();
            if (t == typeof(byte[])) return BuildByteArray();
            if (t.IsArray) return BuildArray(t);
            if (TryBuildList(t, out var p)) return p;
            if (TryBuildDictionary(t, out p)) return p;
            return t == typeof(DateTime) 
                ? BuildDateTime() 
                : BuildDataClass(t);
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
            var writer = NetSerializerBuilder.BuildWriter(t);
            var reader = NetSerializerBuilder.BuildReader(t);
            return new SerializerPair(reader, writer);
        }

        public class SerializerPair
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
