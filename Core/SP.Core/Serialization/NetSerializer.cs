using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SP.Core.Serialization
{
    public static class NetSerializer<T>
    {
        private static readonly TypeSerializer.WriteDelegate<T> _writer;
        private static readonly TypeSerializer.ReadDelegate<T> _reader;

        static NetSerializer()
        {
            var pair = NetSerializer.GetOrBuild(typeof(T));
            _writer = (TypeSerializer.WriteDelegate<T>)pair.Writer;
            _reader = (TypeSerializer.ReadDelegate<T>)pair.Reader;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Serialize(ref NetWriter w, T value) => _writer(ref w, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Deserialize(ref NetReader r) => _reader(ref r);
    }

    public static class NetObject<T> where T : class
    {
        private static readonly TypeSerializer.WriteDelegate<T> _writer;
        private static readonly TypeSerializer.PopulateDelegate<T> _populate;
        private static readonly TypeSerializer.ResetDelegate<T> _reset;

        static NetObject()
        {
            var pair = NetSerializer.GetOrBuild(typeof(T));
            _writer = (TypeSerializer.WriteDelegate<T>)pair.Writer;
            _populate = (TypeSerializer.PopulateDelegate<T>)pair.Populate;
            _reset = (TypeSerializer.ResetDelegate<T>)pair.Reset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Serialize(ref NetWriter w, T value) => _writer(ref w, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Deserialize(ref NetReader r, T instance) => _populate(ref r, instance);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Reset(T instance) => _reset.Invoke(instance);
    }

    public static class NetSerializer
    {
        private static readonly ConcurrentDictionary<Type, TypeSerializer> Cache =
            new ConcurrentDictionary<Type, TypeSerializer>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Serialize<T>(ref NetWriter w, T value) => NetSerializer<T>.Serialize(ref w, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Deserialize<T>(ref NetReader r) => NetSerializer<T>.Deserialize(ref r);

        public static TypeSerializer GetOrBuild(Type type) => Cache.GetOrAdd(type, Build);

        private static TypeSerializer Build(Type t)
        {
            if (t == typeof(string)) return BuildString();
            if (t == typeof(byte[])) return BuildByteArray();
            if (t == typeof(DateTime)) return BuildDateTime();

            if (t.IsArray) return BuildArray(t);
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                return BuildList(t);

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                return BuildDictionary(t);

            return BuildDataClass(t);
        }

        private static TypeSerializer BuildString()
        {
            return new TypeSerializer(
                (TypeSerializer.ReadDelegate<string>)Read,
                (TypeSerializer.WriteDelegate<string>)Write,
                null, null);

            void Write(ref NetWriter w, string v)
            {
                if (v == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                w.WriteString(v);
            }

            string Read(ref NetReader r)
            {
                return r.ReadBool() ? r.ReadString() : null;
            }
        }

        private static TypeSerializer BuildByteArray()
        {
            return new TypeSerializer(
                (TypeSerializer.ReadDelegate<byte[]>)Read,
                (TypeSerializer.WriteDelegate<byte[]>)Write,
                null, null
            );

            void Write(ref NetWriter w, byte[] v)
            {
                if (v == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                w.WriteBytes(v);
            }

            byte[] Read(ref NetReader r)
            {
                if (!r.ReadBool()) return null;
                var span = r.ReadBytes();
                var arr = new byte[span.Length];
                span.CopyTo(arr);
                return arr;
            }
        }

        private static TypeSerializer BuildDateTime()
        {
            return new TypeSerializer(
                (TypeSerializer.ReadDelegate<DateTime>)Read,
                (TypeSerializer.WriteDelegate<DateTime>)Write,
                null, null);

            void Write(ref NetWriter w, DateTime v)
            {
                w.WriteInt64(v.ToUniversalTime().Ticks);
            }

            DateTime Read(ref NetReader r)
            {
                return new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            }
        }

        private static TypeSerializer BuildArray(Type t)
        {
            var elementType = t.GetElementType() ?? throw new InvalidOperationException("ElementType is null");
            var helperType = typeof(ArrayHelper<>).MakeGenericType(elementType);
            var readerType = typeof(TypeSerializer.ReadDelegate<>).MakeGenericType(t);
            var writerType = typeof(TypeSerializer.WriteDelegate<>).MakeGenericType(t);
            return new TypeSerializer(
                Delegate.CreateDelegate(readerType, helperType.GetMethod(nameof(ArrayHelper<int>.Read))!),
                Delegate.CreateDelegate(writerType, helperType.GetMethod(nameof(ArrayHelper<int>.Write))!),
                null, null);
        }

        private static TypeSerializer BuildList(Type t)
        {
            var helperType = typeof(ListHelper<>).MakeGenericType(t.GetGenericArguments()[0]);
            var readerType = typeof(TypeSerializer.ReadDelegate<>).MakeGenericType(t);
            var writerType = typeof(TypeSerializer.WriteDelegate<>).MakeGenericType(t);
            return new TypeSerializer(
                Delegate.CreateDelegate(readerType, helperType.GetMethod(nameof(ListHelper<int>.Read))!),
                Delegate.CreateDelegate(writerType, helperType.GetMethod(nameof(ListHelper<int>.Write))!),
                null, null);
        }

        private static TypeSerializer BuildDictionary(Type t)
        {
            var args = t.GetGenericArguments();
            var helperType = typeof(DictHelper<,>).MakeGenericType(args[0], args[1]);
            var readerType = typeof(TypeSerializer.ReadDelegate<>).MakeGenericType(t);
            var writerType = typeof(TypeSerializer.WriteDelegate<>).MakeGenericType(t);
            return new TypeSerializer(
                Delegate.CreateDelegate(readerType, helperType.GetMethod(nameof(DictHelper<int, int>.Read))!),
                Delegate.CreateDelegate(writerType, helperType.GetMethod(nameof(DictHelper<int, int>.Write))!),
                null, null);
        }

        private static class ArrayHelper<T>
        {
            public static void Write(ref NetWriter w, T[] arr)
            {
                if (arr == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                w.WriteVarUInt((uint)arr.Length);
                for (var i = 0; i < arr.Length; i++) NetSerializer<T>.Serialize(ref w, arr[i]);
            }

            public static T[] Read(ref NetReader r)
            {
                if (!r.ReadBool()) return null;
                var count = r.ReadVarUInt();
                var arr = new T[count];
                for (var i = 0; i < count; i++)
                    arr[i] = NetSerializer<T>.Deserialize(ref r);
                return arr;
            }
        }

        private static class ListHelper<T>
        {
            public static void Write(ref NetWriter w, List<T> list)
            {
                if (list == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                w.WriteVarUInt((uint)list.Count);
                foreach (var item in list) NetSerializer<T>.Serialize(ref w, item);
            }

            public static List<T> Read(ref NetReader r)
            {
                if (!r.ReadBool()) return null;
                var count = (int)r.ReadVarUInt();
                var list = new List<T>(count);
                for (var i = 0; i < count; i++) list.Add(NetSerializer<T>.Deserialize(ref r));
                return list;
            }
        }

        private static class DictHelper<TKey, TValue>
        {
            public static void Write(ref NetWriter w, Dictionary<TKey, TValue> dict)
            {
                if (dict == null)
                {
                    w.WriteBool(false);
                    return;
                }

                w.WriteBool(true);
                w.WriteVarUInt((uint)dict.Count);
                foreach (var kvp in dict)
                {
                    NetSerializer<TKey>.Serialize(ref w, kvp.Key);
                    NetSerializer<TValue>.Serialize(ref w, kvp.Value);
                }
            }

            public static Dictionary<TKey, TValue> Read(ref NetReader r)
            {
                if (!r.ReadBool()) return null;
                var count = (int)r.ReadVarUInt();
                var dict = new Dictionary<TKey, TValue>(count);
                for (var i = 0; i < count; i++)
                {
                    dict.Add(NetSerializer<TKey>.Deserialize(ref r), NetSerializer<TValue>.Deserialize(ref r));
                }
                return dict;
            }
        }

        private static TypeSerializer BuildDataClass(Type t) => DynamicSerializerBuilder.Build(t);
    }

    public class TypeSerializer
    {
        public delegate T ReadDelegate<out T>(ref NetReader r);
        public delegate void WriteDelegate<in T>(ref NetWriter w, T value);
        public delegate void PopulateDelegate<in T>(ref NetReader r, T instance);
        public delegate void ResetDelegate<in T>(T instance);

        public object Reader { get; }
        public object Writer { get; }
        public object Populate { get; }
        public object Reset { get; }

        public TypeSerializer(object reader, object writer, object populate, object reset)
        {
            Reader = reader;
            Writer = writer;
            Populate = populate;
            Reset = reset;
        }
    }
}
