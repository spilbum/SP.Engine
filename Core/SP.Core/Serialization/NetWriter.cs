using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace SP.Core.Serialization
{
    public interface IBufferResizer
    {
        Span<byte> Resize(int size, int position);
    }
    
    public ref struct NetWriter
    {
        private Span<byte> _buffer;
        private int _position;
        private readonly IBufferResizer _resizer;

        public NetWriter(Span<byte> buffer, IBufferResizer resizer = null)
        {
            _buffer = buffer;
            _resizer = resizer;
            _position = 0;
        }
        
        public int WrittenCount => _position;
        public ReadOnlySpan<byte> WrittenSpan => _buffer[.._position];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            _position += count;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            CheckAndGrow(sizeHint);
            return _buffer[_position..];
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckAndGrow(int sizeHint)
        {
            var size = sizeHint <= 0 ? 1 : sizeHint;
            if (_position + size <= _buffer.Length) return;
            if (_resizer == null) throw new InvalidOperationException("Buffer overflow and no resizer provided.");
            
            _buffer = _resizer.Resize(_position + size, _position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            GetSpan(1)[0] = value;
            Advance(1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt16(short value)
        {
            BinaryPrimitives.WriteInt16BigEndian(GetSpan(2), value);
            Advance(2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt16(ushort value)
        {
            BinaryPrimitives.WriteUInt16BigEndian( GetSpan(2), value);
            Advance(2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32(int value)
        {
            BinaryPrimitives.WriteInt32BigEndian(GetSpan(4), value);
            Advance(4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt32(uint value)
        {
            BinaryPrimitives.WriteUInt32BigEndian(GetSpan(4), value);
            Advance(4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt64(long value)
        {
            BinaryPrimitives.WriteInt64BigEndian(GetSpan(8), value);
            Advance(8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt64(ulong value)
        {
            BinaryPrimitives.WriteUInt64BigEndian(GetSpan(8), value);
            Advance(8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteSingle(float value)
        {
            var i = Unsafe.As<float, int>(ref value);
            BinaryPrimitives.WriteInt32BigEndian(GetSpan(4), i);
            Advance(4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDouble(double value)
        {
            var l = Unsafe.As<double, long>(ref value);
            BinaryPrimitives.WriteInt64BigEndian(GetSpan(8), l);
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
            if (typeof(T) == typeof(byte)) WriteByte(Unsafe.As<T, byte>(ref value));
            else if (typeof(T) == typeof(sbyte)) WriteSByte(Unsafe.As<T, sbyte>(ref value));
            else if (typeof(T) == typeof(bool)) WriteBool(Unsafe.As<T, bool>(ref value));
            else if (typeof(T) == typeof(short)) WriteInt16(Unsafe.As<T, short>(ref value));
            else if (typeof(T) == typeof(ushort)) WriteUInt16(Unsafe.As<T, ushort>(ref value));
            else if (typeof(T) == typeof(int)) WriteInt32(Unsafe.As<T, int>(ref value));
            else if (typeof(T) == typeof(uint)) WriteUInt32(Unsafe.As<T, uint>(ref value));
            else if (typeof(T) == typeof(long)) WriteInt64(Unsafe.As<T, long>(ref value));
            else if (typeof(T) == typeof(ulong)) WriteUInt64(Unsafe.As<T, ulong>(ref value));
            else if (typeof(T) == typeof(float)) WriteSingle(Unsafe.As<T, float>(ref value));
            else if (typeof(T) == typeof(double)) WriteDouble(Unsafe.As<T, double>(ref value));
            else
            {
                ThrowNotSupportedType(typeof(T));
            }
        }
        
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNotSupportedType(Type t)
        {
            throw new NotSupportedException($"Type '{t.Name}' is not supported by NetWriter.");
        }
    }
}
