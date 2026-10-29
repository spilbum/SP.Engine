using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace SP.Core.Serialization
{
    public ref struct NetReader
    {
        private readonly ReadOnlySpan<byte> _span;
        private int _position;

        public NetReader(ReadOnlySpan<byte> span)
        {
            _span = span;
            _position = 0;
        }

        public int Remaining => _span.Length - _position;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<byte> Slice(int count)
        {
            if (count > Remaining)
                throw new InvalidDataException($"Insufficient data: need {count}, remaining {Remaining}");
            var span = _span.Slice(_position, count);
            _position += count;
            return span;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(int count)
        {
            if (count > Remaining)
                throw new InvalidDataException($"Advance overflow: {count} > {Remaining}");
            _position += count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> PeekSpan(int count)
        {
            if (count > Remaining)
                throw new InvalidDataException($"Insufficient data: need {count}, remaining {Remaining}");
            return _span.Slice(_position, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> ReadSpan(int count) => Slice(count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte() => Slice(1)[0];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool() => ReadByte() != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short ReadInt16() => BinaryPrimitives.ReadInt16BigEndian(Slice(2));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(Slice(2));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(Slice(4));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(Slice(4));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadInt64() => BinaryPrimitives.ReadInt64BigEndian(Slice(8));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64BigEndian(Slice(8));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadSingle()
        {
            var i = ReadInt32();
            return Unsafe.As<int, float>(ref i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ReadDouble()
        {
            var l = ReadInt64();
            return Unsafe.As<long, double>(ref l);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DateTime ReadDateTime() => new DateTime(ReadInt64());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadVarUInt()
        {
            // LEB128 (up to 5 bytes for 32-bit)
            uint result = 0;
            var shift = 0;
            for (var i = 0; i < 5; i++)
            {
                var b = ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }

            throw new InvalidDataException("VarUInt too long");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadVarInt()
        {
            var u = ReadVarUInt();
            // ZigZag decode
            var value = (int)((u >> 1) ^ (uint)-(int)(u & 1));
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadVarULong()
        {
            // Up to 10 bytes for 64-bit
            ulong result = 0;
            var shift = 0;
            for (var i = 0; i < 10; i++)
            {
                var b = ReadByte();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }

            throw new InvalidDataException("VarULong too long");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadVarLong()
        {
            var u = ReadVarULong();
            return (long)((u >> 1) ^ (ulong)-(long)(u & 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ReadString()
        {
            if (!ReadBool()) return null;
            var byteLen = (int)ReadVarUInt();
            if (byteLen < 0) throw new InvalidDataException("Negative string length");
            return Encoding.UTF8.GetString(ReadSpan(byteLen));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> ReadBytes()
        {
            var len = (int)ReadVarUInt();
            if (len < 0) throw new InvalidDataException("Negative length");
            return ReadSpan(len);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte[] ReadByteArray()
        {
            if (!ReadBool()) return null;
            var span = ReadBytes();
            var arr = new byte[span.Length];
            span.CopyTo(arr);
            return arr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TEnum ReadEnum<TEnum>() where TEnum : unmanaged, Enum
        {
            if (Unsafe.SizeOf<TEnum>() == 1)
            {
                var v = ReadByte();
                return Unsafe.As<byte, TEnum>(ref v);
            }

            if (Unsafe.SizeOf<TEnum>() == 2)
            {
                var v = ReadUInt16();
                return Unsafe.As<ushort, TEnum>(ref v);
            }

            if (Unsafe.SizeOf<TEnum>() == 4)
            {
                var v = ReadUInt32();
                return Unsafe.As<uint, TEnum>(ref v);
            }

            if (Unsafe.SizeOf<TEnum>() == 8)
            {
                var v = ReadUInt64();
                return Unsafe.As<ulong, TEnum>(ref v);
            }

            ThrowNotSupportedType(typeof(TEnum));
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T ReadData<T>() where T : class, INetSerializable, new()
        {
            if (!ReadBool()) return null;
            var instance = new T();
            instance.Deserialize(ref this);
            return instance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T ReadValue<T>()
        {
            if (typeof(T) == typeof(bool))
            {
                var v = ReadBool();
                return Unsafe.As<bool, T>(ref v);
            }

            if (typeof(T) == typeof(byte))
            {
                var v = ReadByte();
                return Unsafe.As<byte, T>(ref v);
            }

            if (typeof(T) == typeof(sbyte))
            {
                var v = ReadSByte();
                return Unsafe.As<sbyte, T>(ref v);
            }

            if (typeof(T) == typeof(short))
            {
                var v = ReadInt16();
                return Unsafe.As<short, T>(ref v);
            }

            if (typeof(T) == typeof(ushort))
            {
                var v = ReadUInt16();
                return Unsafe.As<ushort, T>(ref v);
            }

            if (typeof(T) == typeof(int))
            {
                var v = ReadInt32();
                return Unsafe.As<int, T>(ref v);
            }

            if (typeof(T) == typeof(uint))
            {
                var v = ReadUInt32();
                return Unsafe.As<uint, T>(ref v);
            }

            if (typeof(T) == typeof(long))
            {
                var v = ReadInt64();
                return Unsafe.As<long, T>(ref v);
            }

            if (typeof(T) == typeof(ulong))
            {
                var v = ReadUInt64();
                return Unsafe.As<ulong, T>(ref v);
            }

            if (typeof(T) == typeof(float))
            {
                var v = ReadSingle();
                return Unsafe.As<float, T>(ref v);
            }

            if (typeof(T) == typeof(double))
            {
                var v = ReadDouble();
                return Unsafe.As<double, T>(ref v);
            }

            if (typeof(T) == typeof(DateTime))
            {
                var v = ReadDateTime();
                return Unsafe.As<DateTime, T>(ref v);
            }

            if (typeof(T) == typeof(string))
            {
                var v = ReadString();
                return Unsafe.As<string, T>(ref v);
            }

            if (typeof(T) == typeof(byte[]))
            {
                var v = ReadByteArray();
                return Unsafe.As<byte[], T>(ref v);
            }

            if (typeof(T).IsEnum)
            {
                if (Unsafe.SizeOf<T>() == 1)
                {
                    var v = ReadByte();
                    return Unsafe.As<byte, T>(ref v);
                }

                if (Unsafe.SizeOf<T>() == 2)
                {
                    var v = ReadUInt16();
                    return Unsafe.As<ushort, T>(ref v);
                }

                if (Unsafe.SizeOf<T>() == 4)
                {
                    var v = ReadUInt32();
                    return Unsafe.As<uint, T>(ref v);
                }

                if (Unsafe.SizeOf<T>() == 8)
                {
                    var v = ReadUInt64();
                    return Unsafe.As<ulong, T>(ref v);
                }
            }

            ThrowNotSupportedType(typeof(T));
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[] ReadArray<T>()
        {
            if (!ReadBool()) return null;
            var count = (int)ReadVarUInt();
            var arr = new T[count];
            for (var i = 0; i < count; i++)
            {
                arr[i] = ReadValue<T>();
            }
            return arr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[] ReadDataArray<T>() where T : class, INetSerializable, new()
        {
            if (!ReadBool()) return null;
            var count = (int)ReadVarUInt();
            var arr = new T[count];
            for (var i = 0; i < count; i++)
            {
                arr[i] = ReadData<T>();
            }
            return arr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<T> ReadList<T>(List<T> existing = null)
        {
            if (!ReadBool()) return null;
            var count = (int)ReadVarUInt();
            var list = PrepareList(existing, count);
            for (var i = 0; i < count; i++)
            {
                list.Add(ReadValue<T>());
            }
            return list;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<T> ReadDataList<T>(List<T> existing = null) where T : class, INetSerializable, new()
        {
            if (!ReadBool()) return null;
            var count = (int)ReadVarUInt();
            var list = PrepareList(existing, count);
            for (var i = 0; i < count; i++)
            {
                list.Add(ReadData<T>());
            }
            return list;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Dictionary<TKey, TValue> ReadDict<TKey, TValue>(Dictionary<TKey, TValue> existing = null)
        {
            if (!ReadBool()) return null;
            var count = (int)ReadVarUInt();
            var dict = PrepareDict(existing, count);
            for (var i = 0; i < count; i++)
            {
                dict[ReadValue<TKey>()] = ReadValue<TValue>();
            }
            return dict;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Dictionary<TKey, TValue> ReadDataDict<TKey, TValue>(Dictionary<TKey, TValue> existing = null) where TValue : class, INetSerializable, new()
        {
            if (!ReadBool()) return null;
            var count = (int)ReadVarUInt();
            var dict = PrepareDict(existing, count);
            for (var i = 0; i < count; i++)
            {
                dict[ReadValue<TKey>()] = ReadData<TValue>();
            }
            return dict;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static List<T> PrepareList<T>(List<T> existing, int count)
        {
            if (existing == null)
                return new List<T>(count);

            existing.Clear();
            if (existing.Capacity < count)
                existing.Capacity = count;
            return existing;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Dictionary<TKey, TValue> PrepareDict<TKey, TValue>(Dictionary<TKey, TValue> existing, int count)
        {
            if (existing == null)
                return new Dictionary<TKey, TValue>(count);

            existing.Clear();
            return existing;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNotSupportedType(Type t)
        {
            throw new NotSupportedException($"Type '{t.Name}' is not supported by NetReader.");
        }
    }
}
