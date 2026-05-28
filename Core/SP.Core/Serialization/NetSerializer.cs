using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SP.Core.Serialization
{
    /// <summary>
    /// 클래스 전용 직렬화/역직렬화
    /// </summary>
    public static class NetSerializer<T> where T : class
    {
        private static readonly SerializerPair.WriteGenericFn<T> _writer;
        private static readonly SerializerPair.ReadIntoGenericFn<T> _reader;
        private static readonly SerializerPair.ResetGenericFn<T> _reset;
        
        static NetSerializer()
        {
            var pair = NetSerializer.GetOrBuild(typeof(T));
            _writer = (SerializerPair.WriteGenericFn<T>)pair.GenericWriter;
            _reader = (SerializerPair.ReadIntoGenericFn<T>)pair.GenericReaderInto;
            _reset = (SerializerPair.ResetGenericFn<T>)pair.GenericReset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Serialize(ref NetWriter w, T value) => _writer(ref w, value);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Deserialize(ref NetReader r, T instance) => _reader(ref r, instance);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Reset(T instance) => _reset.Invoke(instance);
    }
    
    public static class NetSerializer
    {
        private static readonly ConcurrentDictionary<Type, SerializerPair> Cache =
            new ConcurrentDictionary<Type, SerializerPair>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Serialize<T>(ref NetWriter w, T value)
        {
            if (Cache.TryGetValue(typeof(T), out var pair))
            {
                ((SerializerPair.WriteGenericFn<T>)pair.GenericWriter)(ref w, value);
                return;
            }
            
            InternalSerialize(ref w, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InternalSerialize<T>(ref NetWriter w, T value)
        {
            var pair = GetOrBuild(typeof(T));
            ((SerializerPair.WriteGenericFn<T>)pair.GenericWriter)(ref w, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Deserialize<T>(ref NetReader r)
        {
            return Cache.TryGetValue(typeof(T), out var pair) 
                ? ((SerializerPair.ReadGenericFn<T>)pair.GenericReader)(ref r) 
                : InternalDeserialize<T>(ref r);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T InternalDeserialize<T>(ref NetReader r)
        {
            var pair = GetOrBuild(typeof(T));
            return ((SerializerPair.ReadGenericFn<T>)pair.GenericReader)(ref r);
        }
        
        public static SerializerPair GetOrBuild(Type type) => Cache.GetOrAdd(type, Build);
        
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
            var rFn = new SerializerPair.ReadFn((ref NetReader r) => r.ReadBool() ? r.ReadString() : null);
            var wFn = new SerializerPair.WriteFn((ref NetWriter w, object v) =>
            {
                if (v == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                w.WriteString((string)v);
            });

            return new SerializerPair(
                rFn, wFn,
                (SerializerPair.ReadGenericFn<string>)((ref NetReader r) => r.ReadBool() ? r.ReadString() : null),
                (SerializerPair.WriteGenericFn<string>)((ref NetWriter w, string v) =>
                {
                    if (v == null)
                    {
                        w.WriteBool(false);
                        return;
                    }

                    w.WriteBool(true);
                    w.WriteString(v);
                }),
                null, null);
        }

        private static SerializerPair BuildByteArray()
        {
            return new SerializerPair(
                (ref NetReader r) => ReadGeneric(ref r),
                (ref NetWriter w, object v) => WriteGeneric(ref w, (byte[])v),
                (SerializerPair.ReadGenericFn<byte[]>)ReadGeneric,
                (SerializerPair.WriteGenericFn<byte[]>)WriteGeneric,
                null, null
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
        
        private static SerializerPair BuildDateTime()
        {
            return new SerializerPair(
                (ref NetReader r) => r.ReadBool() ? (object)new DateTime(r.ReadInt64(), DateTimeKind.Utc) : null,
                (ref NetWriter w, object v) =>
                {
                    if (v == null)
                    {
                        w.WriteBool(false);
                        return;
                    }
                    
                    w.WriteBool(true);
                    w.WriteInt64(((DateTime)v).ToUniversalTime().Ticks);
                }, 
                (SerializerPair.ReadGenericFn<DateTime>)((ref NetReader r) => new DateTime(r.ReadInt64(), DateTimeKind.Utc)),
                (SerializerPair.WriteGenericFn<DateTime>)((ref NetWriter w, DateTime v) => w.WriteInt64(v.ToUniversalTime().Ticks)),
                null, null);
        }

        private static SerializerPair BuildArray(Type arrayType)
        {
            var elemType = arrayType.GetElementType() ?? throw new InvalidOperationException("ElementType is null");
            var elemSer = GetOrBuild(elemType);

            var rFn = new SerializerPair.ReadFn(Read);
            var wfn = new SerializerPair.WriteFn(Write);

            return new SerializerPair(rFn, wfn, rFn, wfn, null, null);

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

            pair = new SerializerPair(rFn, wfn, rFn, wfn, null, null);
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

            pair = new SerializerPair(rFn, wfn, rFn, wfn, null, null);
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



        private static SerializerPair BuildDataClass(Type t) => NetSerializerBuilder.Build(t);
    }
    
    public class SerializerPair
    {
        public delegate object ReadFn(ref NetReader r);
        public delegate void WriteFn(ref NetWriter w, object v);
        public delegate T ReadGenericFn<out T>(ref NetReader r);
        public delegate void WriteGenericFn<in T>(ref NetWriter w, T value);
        public delegate void ReadIntoGenericFn<in T>(ref NetReader r, T instance);
        public delegate void ResetGenericFn<in T>(T instance);

        public ReadFn Reader { get; }
        public WriteFn Writer { get; }
        public object GenericReader { get; }
        public object GenericWriter { get; }
        public object GenericReaderInto { get; }
        public object GenericReset { get; }

        public SerializerPair(ReadFn reader, WriteFn writer, object genericReader, object genericWriter, object genericReaderInto, object genericReset)
        {
            Reader = reader; Writer = writer; GenericReader = genericReader; GenericWriter = genericWriter;
            GenericReaderInto = genericReaderInto; GenericReset = genericReset;
        }
    }
}
