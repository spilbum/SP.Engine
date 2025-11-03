using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace SP.Engine.Runtime.Serialization
{
    public sealed class NetWriter : IDisposable
    {
        private ArrayBufferWriter<byte> _buffer;
        private bool _disposed;

        public NetWriter(int initialCapacity = 128)
        {
            _buffer = new ArrayBufferWriter<byte>(initialCapacity);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }

        public byte[] ToArray()
        {
            return _buffer.WrittenSpan.ToArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Span<byte> GetSpan(int sizeHint)
        {
            return _buffer.GetSpan(sizeHint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Advance(int count)
        {
            _buffer.Advance(count);
        }

        public void Clear()
        {
            _buffer = new ArrayBufferWriter<byte>(_buffer.WrittenCount);
        }

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

        public void WriteBytes(ReadOnlySpan<byte> data)
        {
            WriteVarUInt((uint)data.Length);
            var span = GetSpan(data.Length);
            data.CopyTo(span);
            Advance(data.Length);
        }
    }
}
