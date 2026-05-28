using System;
using System.Buffers.Binary;
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
        public void Align(int alignment)
        {
            if (alignment <= 1) return;
            var mis = _position % alignment;
            if (mis == 0) return;
            Advance(alignment - mis);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Read<T>() where T : unmanaged
        {
            if (typeof(T) == typeof(bool))
            {
                var val = ReadBool();
                return Unsafe.As<bool, T>(ref val);
            }

            if (typeof(T) == typeof(byte))
            {
                var value = ReadByte();
                return Unsafe.As<byte, T>(ref value);
            }

            if (typeof(T) == typeof(sbyte))
            {
                var value = ReadSByte();
                return Unsafe.As<sbyte, T>(ref value);
            }

            if (typeof(T) == typeof(short))
            {
                var value = ReadInt16();
                return Unsafe.As<short, T>(ref value);
            }

            if (typeof(T) == typeof(ushort))
            {
                var value = ReadUInt16();
                return Unsafe.As<ushort, T>(ref value);
            }

            if (typeof(T) == typeof(int))
            {
                var value = ReadInt32();
                return Unsafe.As<int, T>(ref value);
            }

            if (typeof(T) == typeof(uint))
            {
                var value = ReadUInt32();
                return Unsafe.As<uint, T>(ref value);
            }

            if (typeof(T) == typeof(long))
            {
                var value = ReadInt64();
                return Unsafe.As<long, T>(ref value);
            }

            if (typeof(T) == typeof(ulong))
            {
                var value = ReadUInt64();
                return Unsafe.As<ulong, T>(ref value);
            }

            if (typeof(T) == typeof(float))
            {
                var value = ReadSingle();
                return Unsafe.As<float, T>(ref value);
            }

            if (typeof(T) == typeof(double))
            {
                var value = ReadDouble();
                return Unsafe.As<double, T>(ref value);
            }
            
            ThrowNotSupportedType(typeof(T));
            return default;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNotSupportedType(Type t)
        {
            throw new NotSupportedException($"Type '{t.Name}' is not supported by NetReader<T>. Only primitives are allowed.");
        }
    }
}
