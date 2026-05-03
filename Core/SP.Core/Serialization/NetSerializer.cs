using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using DateTime = System.DateTime;

namespace SP.Core.Serialization
{
    public static class NetSerializer<T>
    {
        private static readonly SerializerPair.WriteGenericFn<T> _writer;
        private static readonly SerializerPair.ReadGenericFn<T> _reader;
        
        static NetSerializer()
        {
            var pair = NetSerializer.GetOrBuild(typeof(T));
            _writer = (SerializerPair.WriteGenericFn<T>)pair.GenericWriter;
            _reader = (SerializerPair.ReadGenericFn<T>)pair.GenericReader;
        }

        public static void Serialize(ref NetWriter w, T value) => _writer(ref w, value);
        public static T Deserialize(ref NetReader r) => _reader(ref r);
    }
    
    public static class NetSerializer
    {
        private static readonly ConcurrentDictionary<Type, SerializerPair> Cache =
            new ConcurrentDictionary<Type, SerializerPair>();

        public static void Serialize<T>(ref NetWriter w, T value)
            => NetSerializer<T>.Serialize(ref w, value);

        public static T Deserialize<T>(ref NetReader r)
            => NetSerializer<T>.Deserialize(ref r);

        public static SerializerPair GetOrBuild(Type type)
            => Cache.GetOrAdd(type, Build);
        
        private static SerializerPair Build(Type t)
        {
            if (t == typeof(string)) return BuildString();
            if (t == typeof(byte[])) return BuildByteArray();
            if (t == typeof(DateTime)) return BuildDateTime();
            
            if (t.IsArray) return BuildArray(t);
            if (TryBuildList(t, out var p)) return p;
            if (TryBuildDictionary(t, out p)) return p;
            
            return BuildDataClass(t);
        }

        private static SerializerPair BuildString()
        {
            return new SerializerPair(
                Reader, 
                Writer, 
                (SerializerPair.ReadGenericFn<string>)ReaderGeneric,
                (SerializerPair.WriteGenericFn<string>)WriterGeneric);

            object Reader(ref NetReader r) => ReaderGeneric(ref r);
            void Writer(ref NetWriter w, object v) => WriterGeneric(ref w, (string)v);

            string ReaderGeneric(ref NetReader r) => r.ReadBool() ? r.ReadString() : null;
            void WriterGeneric(ref NetWriter w, string v)
            {
                if (v == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                w.WriteString(v);
            }
        }

        private static SerializerPair BuildByteArray()
        {
            return new SerializerPair(
                (ref NetReader r) => ReadGeneric(ref r),
                (ref NetWriter w, object v) => WriteGeneric(ref w, (byte[])v),
                (SerializerPair.ReadGenericFn<byte[]>)ReadGeneric,
                (SerializerPair.WriteGenericFn<byte[]>)WriteGeneric
            );

            void WriteGeneric(ref NetWriter w, byte[] v)
            {
                if (v == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                w.WriteBytes(v);
            }

            byte[] ReadGeneric(ref NetReader r)
            {
                if (!r.ReadBool()) return null;
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

            var rFn = new SerializerPair.ReadFn(Read);
            var wfn = new SerializerPair.WriteFn(Write);

            return new SerializerPair(rFn, wfn, rFn, wfn);

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
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(List<>)) return false;

            var elemType = t.GetGenericArguments()[0];
            var elemSer = GetOrBuild(elemType);
            
            var rFn = new SerializerPair.ReadFn(Read);
            var wfn = new SerializerPair.WriteFn(Write);

            pair = new SerializerPair(rFn, wfn, rFn, wfn);
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
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(Dictionary<,>)) return false;

            var args = t.GetGenericArguments();
            var kSer = GetOrBuild(args[0]);
            var vSer = GetOrBuild(args[1]);
            
            var rFn = new SerializerPair.ReadFn(Read);
            var wfn = new SerializerPair.WriteFn(Write);

            pair = new SerializerPair(rFn, wfn, rFn, wfn);
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
            return new SerializerPair(
                Read, 
                Write, 
                (SerializerPair.ReadGenericFn<DateTime>)ReadGeneric,
                (SerializerPair.WriteGenericFn<DateTime>)WriteGeneric);

            DateTime ReadGeneric(ref NetReader r)
            {
                return new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            }
            
            void WriteGeneric(ref NetWriter w, DateTime v)
            {
                w.WriteInt64(v.ToUniversalTime().Ticks);
            }

            object Read(ref NetReader r) => r.ReadBool() ? (object)ReadGeneric(ref r) : null;

            void Write(ref NetWriter w, object v)
            {
                if (v == null)
                {
                    w.WriteBool(false);
                    return;
                }
                
                w.WriteBool(true);
                WriteGeneric(ref w, (DateTime)v);
            }
        }

        private static SerializerPair BuildEnum(Type t)
        {
            var readMethod = typeof(NetSerializer).GetMethod(nameof(ReadEnumInternal), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(t);
            var writeMethod = typeof(NetSerializer).GetMethod(nameof(WriteEnumInternal), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(t);
            
            var genericReader = readMethod.CreateDelegate(typeof(SerializerPair.ReadGenericFn<>).MakeGenericType(t));
            var genericWriter = writeMethod.CreateDelegate(typeof(SerializerPair.WriteGenericFn<>).MakeGenericType(t));

            var rFn = new SerializerPair.ReadFn(Read);
            var wFn = new SerializerPair.WriteFn(Write);
            
            return new SerializerPair(rFn, wFn, genericReader, genericWriter);

            object Read(ref NetReader r)
            {
                var pair = GetOrBuild(t);
                return pair.Reader(ref r);
            }

            void Write(ref NetWriter w, object v)
            {
                var pair = GetOrBuild(t);
                pair.Writer(ref w, v);
            }
        }
        
        private static T ReadEnumInternal<T>(ref NetReader r) where T : unmanaged => r.Read<T>();
        private static void WriteEnumInternal<T>(ref NetWriter w, T v) where T : unmanaged => w.Write(v);

        private static SerializerPair BuildDataClass(Type t)
        {
            return NetSerializerBuilder.Build(t);
        }
    }
    
    public class SerializerPair
    {
        public delegate object ReadFn(ref NetReader r);
        public delegate void WriteFn(ref NetWriter w, object v);
        
        public delegate T ReadGenericFn<out T>(ref NetReader r);
        public delegate void WriteGenericFn<in T>(ref NetWriter w, T value);

        public ReadFn Reader { get; }
        public WriteFn Writer { get; }
        public object GenericReader { get; }
        public object GenericWriter { get; }

        public SerializerPair(ReadFn reader, WriteFn writer, object genericReader, object genericWriter)
        {
            Reader = reader;
            Writer = writer;
            GenericReader = genericReader;
            GenericWriter = genericWriter;
        }
    }
}
