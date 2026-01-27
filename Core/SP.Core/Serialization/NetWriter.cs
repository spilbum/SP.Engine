using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace SP.Core.Serialization
{
    public sealed class NetWriter : IDisposable
    {
        private readonly ArrayBufferWriter<byte> _buffer;
        private bool _disposed;

        public NetWriter(int initialCapacity = 1024)
        {
            _buffer = new ArrayBufferWriter<byte>(initialCapacity);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _buffer.Clear();
        }

        public byte[] ToArray() => _buffer.WrittenSpan.ToArray();
        public ReadOnlySpan<byte> WrittenSpan => _buffer.WrittenSpan;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Span<byte> GetSpan(int sizeHint) => _buffer.GetSpan(sizeHint);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Advance(int count) => _buffer.Advance(count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            var span = GetSpan(1);
            span[0] = value;
            Advance(1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSByte(sbyte value)
        {
            WriteByte(unchecked((byte)value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBool(bool value)
        {
            WriteByte(value ? (byte)1 : (byte)0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt16(short value)
        {
            var span = GetSpan(2);
            BinaryPrimitives.WriteInt16BigEndian(span, value);
            Advance(2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt16(ushort value)
        {
            var span = GetSpan(2);
            BinaryPrimitives.WriteUInt16BigEndian(span, value);
            Advance(2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32(int value)
        {
            var span = GetSpan(4);
            BinaryPrimitives.WriteInt32BigEndian(span, value);
            Advance(4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt32(uint value)
        {
            var span = GetSpan(4);
            BinaryPrimitives.WriteUInt32BigEndian(span, value);
            Advance(4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt64(long value)
        {
            var span = GetSpan(8);
            BinaryPrimitives.WriteInt64BigEndian(span, value);
            Advance(8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt64(ulong value)
        {
            var span = GetSpan(8);
            BinaryPrimitives.WriteUInt64BigEndian(span, value);
            Advance(8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSingle(float value)
        {
            var span = GetSpan(4);
            Unsafe.WriteUnaligned(ref span[0], value);
            Advance(4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDouble(double value)
        {
            var span = GetSpan(8);
            Unsafe.WriteUnaligned(ref span[0], value);
            Advance(8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarUInt(uint value)
        {
            Span<byte> tmp = stackalloc byte[5];
            var i = 0;
            while (value >= 0x80)
            {
                tmp[i++] = (byte)(value | 0x80);
                value >>= 7;
            }

            tmp[i++] = (byte)value;

            var span = GetSpan(i);
            tmp[..i].CopyTo(span);
            Advance(i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarInt(int value)
        {
            var zigzag = (uint)((value << 1) ^ (value >> 31));
            WriteVarUInt(zigzag);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarULong(ulong value)
        {
            Span<byte> tmp = stackalloc byte[10];
            var i = 0;
            while (value >= 0x80)
            {
                tmp[i++] = (byte)((byte)value | 0x80);
                value >>= 7;
            }

            tmp[i++] = (byte)value;
            var span = GetSpan(i);
            tmp[..i].CopyTo(span);
            Advance(i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVarLong(long value)
        {
            var zigzag = (ulong)((value << 1) ^ (value >> 63));
            WriteVarULong(zigzag);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteString(string value)
        {
            if (value == null)
            {
                WriteVarUInt(0);
                return;
            }

            var byteLen = Encoding.UTF8.GetByteCount(value);
            WriteVarUInt((uint)byteLen);

            var span = GetSpan(byteLen);
            var written = Encoding.UTF8.GetBytes(value, span);
            Advance(written);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytes(ReadOnlySpan<byte> data)
        {
            WriteVarUInt((uint)data.Length);
            var span = GetSpan(data.Length);
            data.CopyTo(span);
            Advance(data.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write<T>(T value) where T : unmanaged
        {
            if (typeof(T) == typeof(bool))
            {
                WriteBool(Unsafe.As<T, bool>(ref value));
                return;
            }

            if (typeof(T) == typeof(byte))
            {
                WriteByte(Unsafe.As<T, byte>(ref value));
                return;
            }

            if (typeof(T) == typeof(sbyte))
            {
                WriteSByte(Unsafe.As<T, sbyte>(ref value));
                return;
            }

            if (typeof(T) == typeof(short))
            {
                WriteInt16(Unsafe.As<T, short>(ref value));
                return;
            }

            if (typeof(T) == typeof(ushort))
            {
                WriteUInt16(Unsafe.As<T, ushort>(ref value));
                return;
            }

            if (typeof(T) == typeof(int))
            {
                WriteInt32(Unsafe.As<T, int>(ref value));
                return;
            }

            if (typeof(T) == typeof(uint))
            {
                WriteUInt32(Unsafe.As<T, uint>(ref value));
                return;
            }

            if (typeof(T) == typeof(long))
            {
                WriteInt64(Unsafe.As<T, long>(ref value));
                return;
            }

            if (typeof(T) == typeof(ulong))
            {
                WriteUInt64(Unsafe.As<T, ulong>(ref value));
                return;
            }

            if (typeof(T) == typeof(float))
            {
                WriteSingle(Unsafe.As<T, float>(ref value));
                return;
            }

            if (typeof(T) == typeof(double))
            {
                WriteDouble(Unsafe.As<T, double>(ref value));
                return;
            }

            if (typeof(T) == typeof(DateTime))
            {
                WriteInt64(Unsafe.As<T, DateTime>(ref value).Ticks);
            }

            if (Unsafe.SizeOf<T>() == 4) // enum: int, uint
            {
                WriteInt32(Unsafe.As<T, int>(ref value));
                return;
            }

            if (Unsafe.SizeOf<T>() == 1) // enum: byte, sbyte
            {
                WriteByte(Unsafe.As<T, byte>(ref value));
                return;
            }

            if (Unsafe.SizeOf<T>() == 2) // enum: short, ushort
            {
                WriteInt16(Unsafe.As<T, short>(ref value));
                return;
            }

            if (Unsafe.SizeOf<T>() == 8) // enum: long, ulong
            {
                WriteInt64(Unsafe.As<T, long>(ref value));
                return;
            }

            ThrowNotSupportedType(typeof(T));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNotSupportedType(Type t)
        {
            throw new NotSupportedException($"Type '{t.Name}' is not supported by NetWriter.Write<T>. Only primitives are allowed.");
        }
    }
}
